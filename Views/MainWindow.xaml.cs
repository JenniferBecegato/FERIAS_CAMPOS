using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using FeriasCampos.Controllers;
using FeriasCampos.Models;
using FeriasCampos.Services;

namespace FeriasCampos.Views;

public partial class MainWindow : Window
{
    private readonly DashboardController _controller;
    private readonly IConfiguracaoService _configuracao;
    private PeriodoAquisitivo? _selectedPeriod;
    private IReadOnlyList<PeriodoRow> _periodRows = [];
    private List<PeriodoRow> _employeeRows = [];
    private readonly Dictionary<int, int> _selectedPeriodIds = [];
    private readonly ObservableCollection<PeriodoRow> _visiblePeriodRows = [];
    private int _currentPage = 1;
    private const int PageSize = 10;
    private bool _updatingPeriodSelector;
    private IReadOnlyList<FeriasAgendaItem> _agenda = [];
    private DateTime _agendaMonth = new(DateTime.Today.Year, DateTime.Today.Month, 1);
    private static readonly string[] AgendaColors =
    [
        "#0878E8", "#13A86B", "#F59E0B", "#EF476F", "#8B5CF6",
        "#06B6D4", "#F97316", "#D946EF", "#64748B", "#84CC16"
    ];

    public MainWindow(
        DashboardController controller,
        IConfiguracaoService configuracao)
    {
        InitializeComponent();
        _controller = controller;
        _configuracao = configuracao;

        Loaded += async (_, _) => await LoadDataAsync();
        SizeChanged += (_, _) => ApplyResponsiveLayout();
    }

    private async Task LoadDataAsync(string? search = null)
    {
        var dashboard = await _controller.CarregarAsync(search);

        TotalText.Text = dashboard.Total.ToString();
        ScheduledText.Text = dashboard.Programadas.ToString();
        DueText.Text = dashboard.ProximasVencimento.ToString();
        PendingText.Text = dashboard.Pendentes.ToString();
        _periodRows = dashboard.Periodos;
        var availableEmployeeIds = _periodRows.Select(row => row.ColaboradorId).ToHashSet();
        foreach (var employeeId in _selectedPeriodIds.Keys
                     .Where(id => !availableEmployeeIds.Contains(id)).ToList())
        {
            _selectedPeriodIds.Remove(employeeId);
        }

        _employeeRows = _periodRows
            .GroupBy(row => row.ColaboradorId)
            .Select(group =>
            {
                var selected = group.FirstOrDefault(row =>
                    _selectedPeriodIds.TryGetValue(group.Key, out var selectedId) &&
                    row.Id == selectedId) ?? group.MaxBy(row => row.Id)!;
                _selectedPeriodIds[group.Key] = selected.Id;
                return selected;
            })
            .OrderBy(row => row.Colaborador)
            .ToList();
        _currentPage = 1;
        ShowCurrentPage();

        _agenda = await _controller.ListarAgendaAsync();
        RenderAgenda();

        if (PeriodsGrid.SelectedItem is null && PeriodsGrid.Items.Count > 0)
        {
            PeriodsGrid.SelectedIndex = 0;
        }
    }

    private void ShowCurrentPage()
    {
        var pageCount = Math.Max(1, (int)Math.Ceiling(_employeeRows.Count / (double)PageSize));
        _currentPage = Math.Clamp(_currentPage, 1, pageCount);
        _visiblePeriodRows.Clear();
        foreach (var row in _employeeRows
                     .Skip((_currentPage - 1) * PageSize)
                     .Take(PageSize))
        {
            _visiblePeriodRows.Add(row);
        }
        PeriodsGrid.ItemsSource = _visiblePeriodRows;

        var first = _employeeRows.Count == 0 ? 0 : ((_currentPage - 1) * PageSize) + 1;
        var last = Math.Min(_currentPage * PageSize, _employeeRows.Count);
        PaginationSummaryText.Text = $"Mostrando {first} a {last} de {_employeeRows.Count} colaboradores";
        CurrentPageText.Text = _currentPage.ToString();
        PreviousPageButton.IsEnabled = _currentPage > 1;
        NextPageButton.IsEnabled = _currentPage < pageCount;
    }

    private void PreviousPageClick(object sender, RoutedEventArgs e)
    {
        _currentPage--;
        ShowCurrentPage();
        if (PeriodsGrid.Items.Count > 0) PeriodsGrid.SelectedIndex = 0;
    }

    private void NextPageClick(object sender, RoutedEventArgs e)
    {
        _currentPage++;
        ShowCurrentPage();
        if (PeriodsGrid.Items.Count > 0) PeriodsGrid.SelectedIndex = 0;
    }

    private void PeriodGridSelectorLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not ComboBox selector || selector.Tag is not int employeeId)
        {
            return;
        }

        selector.ItemsSource = _periodRows
            .Where(row => row.ColaboradorId == employeeId)
            .OrderByDescending(row => row.Id)
            .ToList();
    }

    private async void PeriodGridSelectorChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ComboBox selector ||
            !selector.IsDropDownOpen ||
            selector.SelectedValue is not int periodId ||
            selector.DataContext is not PeriodoRow currentRow)
        {
            return;
        }

        var selectedRow = _periodRows.FirstOrDefault(row => row.Id == periodId);
        var period = await _controller.SelecionarAsync(periodId);
        if (period is null || selectedRow is null)
        {
            return;
        }

        var visibleIndex = _visiblePeriodRows.IndexOf(currentRow);
        if (visibleIndex >= 0)
        {
            var employeeIndex = _employeeRows.FindIndex(row =>
                row.ColaboradorId == selectedRow.ColaboradorId);
            if (employeeIndex >= 0)
            {
                _employeeRows[employeeIndex] = selectedRow;
            }
            _selectedPeriodIds[selectedRow.ColaboradorId] = selectedRow.Id;
            _visiblePeriodRows[visibleIndex] = selectedRow;
            PeriodsGrid.SelectedItem = selectedRow;
        }

        _selectedPeriod = period;
        ShowPeriodDetails(period);
    }

    private void PreviousAgendaMonthClick(object sender, RoutedEventArgs e)
    {
        _agendaMonth = _agendaMonth.AddMonths(-1);
        RenderAgenda();
    }

    private void NextAgendaMonthClick(object sender, RoutedEventArgs e)
    {
        _agendaMonth = _agendaMonth.AddMonths(1);
        RenderAgenda();
    }

    private void RenderAgenda()
    {
        AgendaMonthText.Text = $"Agenda de férias — {_agendaMonth:MMMM yyyy}";
        AgendaDaysGrid.Children.Clear();
        AgendaLegend.Children.Clear();

        var monthEnd = _agendaMonth.AddMonths(1).AddDays(-1);
        var monthItems = _agenda
            .Where(item => item.Inicio.Date <= monthEnd && item.Fim.Date >= _agendaMonth)
            .ToList();
        var employeeIds = monthItems
            .Select(item => item.ColaboradorId)
            .Distinct()
            .OrderBy(id => id)
            .ToList();
        var colors = employeeIds
            .Select((id, index) => new { id, color = AgendaColors[index % AgendaColors.Length] })
            .ToDictionary(item => item.id, item => item.color);

        var firstVisibleDay = _agendaMonth.AddDays(-(int)_agendaMonth.DayOfWeek);
        for (var index = 0; index < 42; index++)
        {
            var day = firstVisibleDay.AddDays(index);
            AgendaDaysGrid.Children.Add(CreateAgendaDay(day, monthItems, colors));
        }

        foreach (var employee in monthItems
                     .GroupBy(item => new { item.ColaboradorId, item.Colaborador })
                     .OrderBy(group => group.Key.Colaborador))
        {
            var legendItem = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(5, 3, 5, 3),
                ToolTip = string.Join("\n", employee.Select(item =>
                    $"{item.Inicio:dd/MM} a {item.Fim:dd/MM}"))
            };
            legendItem.Children.Add(new Border
            {
                Width = 9,
                Height = 9,
                CornerRadius = new CornerRadius(5),
                Background = BrushFrom(colors[employee.Key.ColaboradorId]),
                Margin = new Thickness(0, 0, 5, 0),
                VerticalAlignment = VerticalAlignment.Center
            });
            legendItem.Children.Add(new TextBlock
            {
                Text = employee.Key.Colaborador.Split(' ')[0],
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center
            });
            AgendaLegend.Children.Add(legendItem);
        }

        EmptyAgendaText.Visibility = monthItems.Count == 0
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private UIElement CreateAgendaDay(
        DateTime day,
        IReadOnlyList<FeriasAgendaItem> monthItems,
        IReadOnlyDictionary<int, string> colors)
    {
        var activeItems = monthItems
            .Where(item => item.Inicio.Date <= day && item.Fim.Date >= day)
            .GroupBy(item => item.ColaboradorId)
            .Select(group => group.First())
            .ToList();
        var panel = new Grid();
        panel.RowDefinitions.Add(new RowDefinition());
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panel.Children.Add(new TextBlock
        {
            Text = day.Day.ToString(),
            FontSize = 11,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = day.Month == _agendaMonth.Month
                ? (day.Date == DateTime.Today ? BrushFrom("#0878E8") : BrushFrom("#10213E"))
                : BrushFrom("#B3BCC8"),
            FontWeight = day.Date == DateTime.Today ? FontWeights.Bold : FontWeights.Normal
        });

        var markers = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Height = 5
        };
        Grid.SetRow(markers, 1);
        foreach (var item in activeItems.Take(4))
        {
            markers.Children.Add(new Border
            {
                Width = 5,
                Height = 5,
                CornerRadius = new CornerRadius(3),
                Background = BrushFrom(colors[item.ColaboradorId]),
                Margin = new Thickness(1, 0, 1, 0)
            });
        }
        panel.Children.Add(markers);

        return new Border
        {
            Height = 32,
            Margin = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            BorderBrush = day.Date == DateTime.Today ? BrushFrom("#0878E8") : Brushes.Transparent,
            BorderThickness = new Thickness(1),
            Background = activeItems.Count > 0 && day.Month == _agendaMonth.Month
                ? BrushFrom("#F4F8FD")
                : Brushes.Transparent,
            Child = panel,
            ToolTip = activeItems.Count == 0
                ? null
                : string.Join("\n", activeItems.Select(item => item.Colaborador))
        };
    }

    private static SolidColorBrush BrushFrom(string color)
    {
        return new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
    }

    private async void PeriodSelected(object sender, SelectionChangedEventArgs e)
    {
        if (PeriodsGrid.SelectedItem is not PeriodoRow selectedRow)
        {
            return;
        }

        _selectedPeriod = await _controller.SelecionarAsync(selectedRow.Id);
        if (_selectedPeriod is null)
        {
            return;
        }

        ShowPeriodDetails(_selectedPeriod);
    }

    private void ShowPeriodDetails(PeriodoAquisitivo period)
    {
        InitialsText.Text = period.Colaborador.Iniciais;
        EmployeeText.Text = period.Colaborador.Nome;
        CodeText.Text = $"Cód. {period.Colaborador.Matricula}";
        _updatingPeriodSelector = true;
        PeriodSelector.ItemsSource = _periodRows
            .Where(row => row.ColaboradorId == period.ColaboradorId)
            .OrderByDescending(row => row.Id)
            .ToList();
        PeriodSelector.SelectedValue = period.Id;
        _updatingPeriodSelector = false;
        ExpiryText.Text = period.Vencimento.ToString("dd/MM/yyyy");
        RightText.Text = $"{period.DireitoDias} dias";
        UsedText.Text = $"{period.Agendados} dias";
        SoldText.Text = $"{period.Vendidos} dias";
        DaysOffText.Text = $"{period.Folgas} dias";
        BalanceText.Text = $"{period.Saldo} dias";
        RemainingText.Text = $"Saldo restante: {period.Saldo} dias";

        HistoryList.ItemsSource = period.Movimentacoes
            .OrderByDescending(movement => movement.DataHoraUtc)
            .Select(movement =>
                $"●  {movement.Tipo}     {movement.Inicio:dd/MM/yyyy} " +
                $"{(-movement.Dias):+#;-#;0} dias");
    }

    private async void PeriodSelectorChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingPeriodSelector || PeriodSelector.SelectedValue is not int periodId ||
            _selectedPeriod?.Id == periodId)
        {
            return;
        }

        _selectedPeriod = await _controller.SelecionarAsync(periodId);
        if (_selectedPeriod is not null)
        {
            ShowPeriodDetails(_selectedPeriod);
        }
    }

    private async void ScheduleClick(object sender, RoutedEventArgs e)
    {
        if (_selectedPeriod is null)
        {
            return;
        }

        if (_selectedPeriod.Vencimento.Date < DateTime.Today)
        {
            MessageBox.Show(
                $"Este período venceu em {_selectedPeriod.Vencimento:dd/MM/yyyy}. " +
                "Não é possível agendar férias após o vencimento.",
                "Período vencido",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        var feriados = await _controller.ListarFeriadosAsync();
        var dialog = new ScheduleVacationDialog(
            _selectedPeriod,
            _configuracao.BloquearAgendamentoMenos30Dias,
            _configuracao.BloquearInicioAntesRepousoSemanal,
            feriados)
        {
            Owner = this
        };

        if (dialog.ShowDialog() != true || dialog.Intervalos.Count == 0)
        {
            return;
        }

        var result = await _controller.AgendarAsync(
            _selectedPeriod.Id,
            dialog.Intervalos,
            dialog.DiasAbono);

        if (!result.Valido)
        {
            MessageBox.Show(
                string.Join(Environment.NewLine, result.Erros),
                "Agendamento não permitido",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        if (result.Avisos.Count > 0)
        {
            MessageBox.Show(
                string.Join(Environment.NewLine, result.Avisos),
                "Agendamento salvo com avisos");
        }

        await LoadDataAsync(SearchBox.Text);
    }

    private async void SearchKeyUp(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            await LoadDataAsync(SearchBox.Text);
        }
    }

    private void ImportClick(object sender, RoutedEventArgs e)
    {
        MessageBox.Show(
            _controller.EstadoImportacao(),
            "Importar PDF",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private async void ModuleClick(object sender, RoutedEventArgs e)
    {
        var moduleName = (sender as FrameworkElement)?.Tag?.ToString();
        if (moduleName == "Colaboradores")
        {
            var window = new EmployeesWindow(_controller)
            {
                Owner = this
            };

            window.ShowDialog();
            if (window.Changed)
            {
                await LoadDataAsync(SearchBox.Text);
            }

            return;
        }

        if (moduleName == "Configurações")
        {
            new SettingsWindow(_configuracao) { Owner = this }.ShowDialog();
            return;
        }

        if (moduleName == "Regras")
        {
            new RulesWindow { Owner = this }.ShowDialog();
            return;
        }

        MessageBox.Show(
            $"Módulo {moduleName} preparado no design system. " +
            "Os dados desta entrega são administrados pelo dashboard integrado.",
            "Controle de Férias");
    }

    private void DayOffClick(object sender, RoutedEventArgs e)
    {
        MessageBox.Show(
            "O registro de folga exige quantidade e justificativa; " +
            "a movimentação é imutável e auditável.",
            "Registrar folga");
    }

    private void ApplyResponsiveLayout()
    {
        var rootGrid = (Grid)Content;
        var navigationWidth = ActualWidth < 1280 ? 76 : 230;
        var detailsWidth = ActualWidth < 1280 ? 330 : 410;

        rootGrid.ColumnDefinitions[0].Width = new GridLength(navigationWidth);
        rootGrid.ColumnDefinitions[2].Width = new GridLength(detailsWidth);
    }
}
