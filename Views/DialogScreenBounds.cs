using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace FeriasCampos.Views;

/// <summary>Applies monitor bounds and a scrollable layout to every application window.</summary>
internal static class DialogScreenBounds
{
    public static void Register() => EventManager.RegisterClassHandler(
        typeof(Window), FrameworkElement.LoadedEvent, new RoutedEventHandler(OnLoaded));

    private static void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is Window window && e.OriginalSource == window &&
            window.Content is FrameworkElement content && window.Content is not ScreenViewport)
            new WindowBounds(window, content).Attach();
    }

    private sealed class WindowBounds(Window window, FrameworkElement content)
    {
        private HwndSource? source;
        private bool queued;
        private bool updating;
        private bool closed;

        public void Attach()
        {
            // Keep a finite layout area: star rows and virtualized lists must never
            // be measured with the infinite space of an ordinary ScrollViewer.
            var dpi = VisualTreeHelper.GetDpi(window);
            var handle = new WindowInteropHelper(window).Handle;
            GetWindowRect(handle, out var outer);
            GetClientRect(handle, out var client);
            var chromeWidth = (outer.Right - outer.Left - client.Right) / dpi.DpiScaleX;
            var chromeHeight = (outer.Bottom - outer.Top - client.Bottom) / dpi.DpiScaleY;
            var width = double.IsFinite(window.Width) ? window.Width - chromeWidth : content.ActualWidth;
            var height = double.IsFinite(window.Height) ? window.Height - chromeHeight : content.ActualHeight;
            window.Content = null;
            window.Content = new ScreenViewport(content, Math.Max(1, width), Math.Max(1, height));
            window.SizeToContent = SizeToContent.Manual;
            window.MinWidth = 0;
            window.MinHeight = 0;
            source = HwndSource.FromHwnd(handle);
            source?.AddHook(Hook);
            window.LocationChanged += Changed;
            window.SizeChanged += Changed;
            window.StateChanged += Changed;
            window.Closed += Closed;
            Constrain();
        }

        private void Closed(object? sender, EventArgs e)
        {
            closed = true;
            source?.RemoveHook(Hook);
            window.LocationChanged -= Changed;
            window.SizeChanged -= Changed;
            window.StateChanged -= Changed;
            window.Closed -= Closed;
        }

        private void Changed(object? sender, EventArgs e) => Queue();

        private void Queue()
        {
            if (queued || updating || closed) return;
            queued = true;
            window.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
            {
                queued = false;
                if (!closed) Constrain();
            }));
        }

        private IntPtr Hook(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            // Display settings, work area (taskbar), DPI, and moving between monitors.
            if (message is 0x007E or 0x001A or 0x02E0 or 0x0232) Queue();
            if (message == 0x0024 && TryWorkArea(hwnd, out var info))
            {
                var limits = Marshal.PtrToStructure<MinMaxInfo>(lParam);
                limits.MaxPosition = new NativePoint(info.Work.Left - info.Monitor.Left, info.Work.Top - info.Monitor.Top);
                limits.MaxSize = new NativePoint(info.Work.Right - info.Work.Left, info.Work.Bottom - info.Work.Top);
                limits.MaxTrackSize = limits.MaxSize;
                limits.MinTrackSize = new NativePoint(Math.Min(limits.MinTrackSize.X, limits.MaxSize.X),
                    Math.Min(limits.MinTrackSize.Y, limits.MaxSize.Y));
                Marshal.StructureToPtr(limits, lParam, false);
                handled = true;
            }
            return IntPtr.Zero;
        }

        private void Constrain()
        {
            var handle = new WindowInteropHelper(window).Handle;
            if (window.WindowState == WindowState.Minimized || !TryWorkArea(handle, out var info) ||
                !GetWindowRect(handle, out var bounds)) return;
            var work = info.Work;
            var width = Math.Min(bounds.Right - bounds.Left, work.Right - work.Left);
            var height = Math.Min(bounds.Bottom - bounds.Top, work.Bottom - work.Top);
            var left = Math.Clamp(bounds.Left, work.Left, work.Right - width);
            var top = Math.Clamp(bounds.Top, work.Top, work.Bottom - height);
            if (window.WindowState != WindowState.Normal ||
                (left == bounds.Left && top == bounds.Top && width == bounds.Right - bounds.Left && height == bounds.Bottom - bounds.Top)) return;
            updating = true;
            try { SetWindowPos(handle, IntPtr.Zero, left, top, width, height, 0x0014); }
            finally { updating = false; }
        }
    }

    private sealed class ScreenViewport : ScrollViewer
    {
        private readonly FrameworkElement layout;
        private readonly double layoutWidth;
        private readonly double layoutHeight;

        public ScreenViewport(FrameworkElement content, double width, double height)
        {
            layout = new Grid();
            ((Grid)layout).Children.Add(content);
            Content = layout;
            layoutWidth = width;
            layoutHeight = height;
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            CanContentScroll = false;
        }

        protected override Size MeasureOverride(Size constraint)
        {
            // Reserve scrollbar space at small sizes, avoiding clipping at the
            // opposite edge and keeping the original layout usable at any scale.
            var width = double.IsFinite(constraint.Width) ? constraint.Width : layoutWidth;
            var height = double.IsFinite(constraint.Height) ? constraint.Height : layoutHeight;
            var vertical = height < layoutHeight;
            var horizontal = width < layoutWidth + (vertical ? SystemParameters.VerticalScrollBarWidth : 0);
            vertical |= height < layoutHeight + (horizontal ? SystemParameters.HorizontalScrollBarHeight : 0);
            layout.Width = Math.Max(layoutWidth, width - (vertical ? SystemParameters.VerticalScrollBarWidth : 0));
            layout.Height = Math.Max(layoutHeight, height - (horizontal ? SystemParameters.HorizontalScrollBarHeight : 0));
            return base.MeasureOverride(constraint);
        }
    }

    private static bool TryWorkArea(IntPtr handle, out MonitorInfo info)
    {
        info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        return GetMonitorInfo(MonitorFromWindow(handle, 2), ref info);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint(int x, int y) { public int X = x, Y = y; }
    [StructLayout(LayoutKind.Sequential)]
    private struct MinMaxInfo { public NativePoint Reserved, MaxSize, MaxPosition, MinTrackSize, MaxTrackSize; }
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo { public int Size; public NativeRect Monitor, Work; public uint Flags; }
    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr window, out NativeRect rect);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetClientRect(IntPtr window, out NativeRect rect);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr window, IntPtr insertAfter, int x, int y, int width, int height, uint flags);
}
