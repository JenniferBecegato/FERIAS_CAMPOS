using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using Xunit;

namespace FeriasCampos.Tests;

public sealed class ScreenBoundsTests
{
    [Theory]
    [InlineData(640, 360)]
    [InlineData(800, 600)]
    [InlineData(1024, 768)]
    [InlineData(1920, 1080)]
    [InlineData(2560, 1440)]
    public void Conteudo_permanece_acessivel_ao_reduzir_e_ampliar_viewport(int width, int height)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var type = typeof(App).Assembly.GetType("FeriasCampos.Views.DialogScreenBounds+ScreenViewport", true)!;
                var content = new Grid();
                content.RowDefinitions.Add(new RowDefinition());
                var button = new Button { Content = "Salvar", VerticalAlignment = VerticalAlignment.Bottom };
                content.Children.Add(button);
                var viewer = (ScrollViewer)Activator.CreateInstance(type,
                    BindingFlags.Instance | BindingFlags.Public, null, new object[] { content, 1140d, 780d }, null)!;
                foreach (var size in new[] { new Size(width, height), new Size(480, 320), new Size(2560, 1440) })
                {
                    viewer.Measure(size);
                    viewer.Arrange(new Rect(size));
                    viewer.UpdateLayout();
                    Assert.True(viewer.DesiredSize.Width <= size.Width);
                    Assert.True(viewer.DesiredSize.Height <= size.Height);
                    Assert.True(content.ActualWidth >= 1140);
                    Assert.True(content.ActualHeight >= 780);
                    Assert.True(double.IsFinite(content.ActualHeight));
                    viewer.ScrollToBottom();
                    viewer.ScrollToRightEnd();
                    viewer.UpdateLayout();
                    Assert.True(viewer.ExtentHeight - viewer.ViewportHeight <= viewer.VerticalOffset + 1);
                    Assert.True(viewer.ExtentWidth - viewer.ViewportWidth <= viewer.HorizontalOffset + 1);
                }
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)));
        Assert.Null(failure);
    }
}
