using FeriasCampos.Properties;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FeriasCampos.Controllers;
using FeriasCampos.Models;

namespace FeriasCampos.Views;

public partial class VacationAgendaWindow : Window
{
    private readonly DashboardController _controller;
    private readonly CultureInfo _culture = CultureInfo.GetCultureInfo("pt-BR");
    private IReadOnlyList<FeriasAgendaItem> _agenda = [];
    private List<EmployeeOption> _employees = [];
    private DateTime _month = new(DateTime.Today.Year, DateTime.Today.Month, 1);
    private bool _updating;
    private bool _loaded;
    private bool _syncingDate;
    private DateTime _navigationDate = DateTime.Today;
    private static readonly string[] Colors = ["#2563EB", "#047857", "#B45309", "#BE185D", "#7C3AED", "#0E7490", "#C2410C", "#4338CA"];

    public VacationAgendaWindow(DashboardController controller)
    {
        InitializeComponent();
        _controller = controller;
        EmployeeSearch.AddHandler(TextBox.TextChangedEvent, new TextChangedEventHandler(SearchChanged));
        Loaded += LoadAsync;
    }

    private async void LoadAsync(object sender, RoutedEventArgs e)
    {
        try
        {
            _agenda = await _controller.ListarAgendaAsync();
            _employees = (await _controller.ListarColaboradoresAsync())
                .OrderBy(x => x.Nome).Select(x => new EmployeeOption(x.Id, x.Nome,
                    string.Format(ScreenTexts.VacationAgendaWindow_ColaboradorNomeId, x.Nome, x.Id))).ToList();
            EmployeeSearch.ItemsSource = _employees;
            _loaded = true;
            Render();
        }
        catch (Exception)
        {
            StatusText.Text = ScreenTexts.VacationAgendaWindow_NaoFoiPossivelCarregarAAgendaFecheE;
        }
    }

    private void SearchChanged(object sender, TextChangedEventArgs e)
    {
        if (_updating || !_loaded || e.OriginalSource is not TextBox editor) return;
        // A seleção usa a identidade do cadastro, mesmo quando há nomes iguais.
        if (EmployeeSearch.SelectedItem is EmployeeOption selected && editor.Text == selected.Label) return;
        var query = editor.Text;
        var caret = editor.CaretIndex;
        _updating = true;
        EmployeeSearch.SelectedItem = null;
        EmployeeSearch.ItemsSource = _employees.Where(x => Matches(x.Name, query) || Matches(x.Label, query)).ToList();
        EmployeeSearch.Text = query;
        editor.CaretIndex = Math.Min(caret, query.Length);
        EmployeeSearch.IsDropDownOpen = editor.IsKeyboardFocused;
        _updating = false;
        UpdateVacationPeriods();
        Render();
    }

    private bool Matches(string name, string query) => _culture.CompareInfo.IndexOf(name, query.Trim(),
        CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0;

    private void EmployeeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_updating && _loaded)
        {
            UpdateVacationPeriods();
            Render();
        }
    }

    private void UpdateVacationPeriods()
    {
        _updating = true;
        var employee = EmployeeSearch.SelectedItem as EmployeeOption;
        var periods = employee is null ? [] : _agenda.Where(x => x.ColaboradorId == employee.Id)
            .OrderByDescending(x => x.Inicio).Select(x => new VacationOption(x,
                string.Format(ScreenTexts.VacationAgendaWindow_IntervaloPeriodo, x.Inicio, x.Fim))).ToList();
        periods.Insert(0, new VacationOption(null, employee is null ? ScreenTexts.VacationAgendaWindow_SelecioneUmColaborador :
            periods.Count == 0 ? ScreenTexts.VacationAgendaWindow_SemFeriasAgendadas : ScreenTexts.VacationAgendaWindow_TodosOsPeriodos));
        VacationPeriodSelector.ItemsSource = periods;
        VacationPeriodSelector.SelectedIndex = 0;
        VacationPeriodSelector.IsEnabled = employee is not null && periods.Count > 1;
        _updating = false;
    }

    private void VacationPeriodChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updating || !_loaded) return;
        if (VacationPeriodSelector.SelectedItem is VacationOption { Item: { } item })
            _month = new DateTime(item.Inicio.Year, item.Inicio.Month, 1);
        Render();
    }

    private void ClearClick(object sender, RoutedEventArgs e)
    {
        _updating = true;
        EmployeeSearch.SelectedItem = null;
        EmployeeSearch.Text = "";
        EmployeeSearch.ItemsSource = _employees;
        EmployeeSearch.IsDropDownOpen = false;
        _updating = false;
        UpdateVacationPeriods();
        Render();
    }

    private void PreviousClick(object sender, RoutedEventArgs e) { if (_month.Year > 1 || _month.Month > 1) { _month = _month.AddMonths(-1); Render(); } }
    private void NextClick(object sender, RoutedEventArgs e) { if (_month.Year < 9999 || _month.Month < 12) { _month = _month.AddMonths(1); Render(); } }
    private void TodayClick(object sender, RoutedEventArgs e) => NavigateTo(DateTime.Today);
    private void NavigateTo(DateTime date)
    {
        _navigationDate = date.Date;
        _month = new(date.Year, date.Month, 1);
        Render();
    }

    private void NavigationDateChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_syncingDate && _loaded && NavigationDate.SelectedDate is DateTime date) NavigateTo(date);
    }

    private void MoveDayClick(object sender, RoutedEventArgs e)
    {
        var delta = int.Parse((string)((Button)sender).Tag);
        if (delta < 0 && _navigationDate == DateTime.MinValue.Date ||
            delta > 0 && _navigationDate == DateTime.MaxValue.Date) return;
        NavigateTo(_navigationDate.AddDays(delta));
    }

    private void MoveYearClick(object sender, RoutedEventArgs e)
    {
        var delta = int.Parse((string)((Button)sender).Tag);
        if (_navigationDate.Year + delta / 12 is < 1 or > 9999) return;
        NavigateTo(_navigationDate.AddMonths(delta));
    }
    private void CloseClick(object sender, RoutedEventArgs e) => Close();
    private static Brush ColorFor(int id) => (Brush)new BrushConverter().ConvertFromString(Colors[(int)((uint)id % Colors.Length)])!;

    private void Render()
    {
        if (_navigationDate.Year != _month.Year || _navigationDate.Month != _month.Month)
            _navigationDate = _month.AddDays(Math.Min(_navigationDate.Day, DateTime.DaysInMonth(_month.Year, _month.Month)) - 1);
        _syncingDate = true;
        NavigationDate.SelectedDate = _navigationDate;
        NavigationDate.DisplayDate = _navigationDate;
        _syncingDate = false;
        MonthTitle.Text = _culture.TextInfo.ToTitleCase(_month.ToString("MMMM 'de' yyyy", _culture));
        var end = new DateTime(_month.Year, _month.Month, DateTime.DaysInMonth(_month.Year, _month.Month));
        var selected = EmployeeSearch.SelectedItem as EmployeeOption;
        var period = (VacationPeriodSelector.SelectedItem as VacationOption)?.Item;
        var items = _agenda.Where(x => x.Inicio.Date <= end && x.Fim.Date >= _month)
            .Where(x => selected is not null ? x.ColaboradorId == selected.Id : Matches(x.Colaborador, EmployeeSearch.Text))
            .Where(x => period is null || x == period)
            .OrderBy(x => x.Inicio).ThenBy(x => x.Colaborador).ToList();
        AgendaTable.ItemsSource = items.Select(x => new
        {
            Name = x.Colaborador, Dates = string.Format(ScreenTexts.VacationAgendaWindow_DatasFerias, x.Inicio, x.Fim),
            Days = (x.Fim.Date - x.Inicio.Date).Days + 1, Color = ColorFor(x.ColaboradorId)
        }).ToList();
        SummaryText.Text = string.Format(ScreenTexts.VacationAgendaWindow_ColaboradorEsPeriodoSNesteMes, items.Select(x => x.ColaboradorId).Distinct().Count(), items.Count);
        EmptyText.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        WeeksPanel.Children.Clear();
        var offset = (int)_month.DayOfWeek;
        var weeks = (offset + end.Day + 6) / 7;
        for (var week = 0; week < weeks; week++)
        {
            var firstDay = week * 7 - offset + 1;
            var weekStart = _month.AddDays(Math.Max(1, firstDay) - 1);
            var weekEnd = _month.AddDays(Math.Min(end.Day, firstDay + 6) - 1);
            var active = items.Where(x => x.Inicio.Date <= weekEnd && x.Fim.Date >= weekStart).ToList();
            var grid = new Grid { MinHeight = 92, Margin = new Thickness(0, 0, 0, 4) };
            for (var col = 0; col < 7; col++) grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(30) });
            for (var row = 0; row < active.Count; row++) grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(27) });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            for (var col = 0; col < 7; col++)
            {
                var dayNumber = firstDay + col;
                var valid = dayNumber >= 1 && dayNumber <= end.Day;
                var today = valid && _month.AddDays(dayNumber - 1) == DateTime.Today;
                var background = new Border { Background = today ? new SolidColorBrush(Color.FromRgb(219, 234, 254)) : valid ? (col is 0 or 6 ? Brushes.AliceBlue : Brushes.White) : Brushes.WhiteSmoke,
                    BorderBrush = (Brush)new BrushConverter().ConvertFromString("#E8EDF3")!, BorderThickness = new Thickness(.5), CornerRadius = new CornerRadius(5) };
                if (valid)
                {
                    var date = _month.AddDays(dayNumber - 1);
                    if (date == _navigationDate)
                    {
                        background.BorderBrush = Brushes.CornflowerBlue;
                        background.BorderThickness = new Thickness(2);
                    }
                    background.Cursor = System.Windows.Input.Cursors.Hand;
                    background.MouseLeftButtonUp += (_, _) => NavigateTo(date);
                }
                Grid.SetColumn(background, col); Grid.SetRowSpan(background, grid.RowDefinitions.Count); grid.Children.Add(background);
                var number = new TextBlock { Text = valid ? dayNumber.ToString() : "", Margin = new Thickness(8, 4, 0, 0),
                    FontWeight = FontWeights.SemiBold, Foreground = today ? Brushes.White : Brushes.SlateGray };
                var badge = new Border { Child = number, Background = today ? ColorFor(0) : Brushes.Transparent, CornerRadius = new CornerRadius(12), Width = 30, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(3, 1, 0, 1) };
                Grid.SetColumn(badge, col); grid.Children.Add(badge);
            }
            for (var lane = 0; lane < active.Count; lane++)
            {
                var item = active[lane];
                var start = item.Inicio.Date < weekStart ? weekStart : item.Inicio.Date;
                var finish = item.Fim.Date > weekEnd ? weekEnd : item.Fim.Date;
                var label = string.Format(ScreenTexts.VacationAgendaWindow_FaixaColaborador, (item.Inicio.Date < start ? "‹ " : "☀ "), item.Colaborador, (item.Fim.Date > finish ? " ›" : ""));
                var bar = new Border { Background = ColorFor(item.ColaboradorId), CornerRadius = new CornerRadius(5),
                    Margin = new Thickness(2, 1, 2, 2), Padding = new Thickness(6, 2, 4, 2),
                    ToolTip = string.Format(ScreenTexts.VacationAgendaWindow_ADias, item.Colaborador, item.Inicio, item.Fim, (item.Fim.Date - item.Inicio.Date).Days + 1),
                    Child = new TextBlock { Text = label, Foreground = Brushes.White, FontSize = 11, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis } };
                Grid.SetColumn(bar, (int)start.DayOfWeek); Grid.SetColumnSpan(bar, (finish - start).Days + 1); Grid.SetRow(bar, lane + 1);
                grid.Children.Add(bar);
            }
            WeeksPanel.Children.Add(grid);
        }
    }

    private sealed record EmployeeOption(int Id, string Name, string Label);
    private sealed record VacationOption(FeriasAgendaItem? Item, string Label);
}
