using FeriasCampos.Properties;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using FeriasCampos.Controllers;
using FeriasCampos.Models;
using FeriasCampos.Services;

namespace FeriasCampos.Views;

public partial class MainWindow : Window
{
    private readonly DashboardController _controller;
    private readonly IConfiguracaoService _configuracao;
    private readonly IPeriodoService _periodos;
    private readonly IFeriadoService _feriados;
    private PeriodoAquisitivo? _selectedPeriod;
    private IReadOnlyList<PeriodoRow> _periodRows = [];
    private List<PeriodoRow> _employeeRows = [];
    private readonly Dictionary<int, int> _selectedPeriodIds = [];
    private readonly ObservableCollection<PeriodoRow> _visiblePeriodRows = [];
    private int _currentPage = 1;
    private int PageSize = int.MaxValue;
    private bool _updatingEmployeeFilter;
    private bool _updatingPeriodSelector;
    private bool _restoringPeriodSelection;
    private IReadOnlyList<FeriasAgendaItem> _agenda = [];
    private DateTime _agendaMonth = new(DateTime.Today.Year, DateTime.Today.Month, 1);
    private static readonly string[] AgendaColors =
    [
        "#0878E8", "#13A86B", "#F59E0B", "#EF476F", "#8B5CF6",
        "#06B6D4", "#F97316", "#D946EF", "#64748B", "#84CC16"
    ];

    public MainWindow(
        DashboardController controller,
        IConfiguracaoService configuracao,
        IPeriodoService periodos,
        IFeriadoService feriados)
    {
        InitializeComponent();
        _controller = controller;
        _configuracao = configuracao;
        _periodos = periodos;
        _feriados = feriados;

        Loaded += async (_, _) => await LoadDataAsync();
        SizeChanged += (_, _) => ApplyResponsiveLayout();
    }

    private async void SyncTableClick(object sender, RoutedEventArgs e)
    {
        SyncTableButton.IsEnabled = false;
        SyncTableButton.Content = ScreenTexts.MainWindow_Sincronizando;
        try
        {
            var pagina = _currentPage;
            await LoadDataAsync();
            _currentPage = pagina;
            ShowCurrentPage();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ScreenTexts.MainWindow_ErroSincronizacao + "\n" + ex.Message,
                ScreenTexts.MainWindow_SincronizarTabela, MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SyncTableButton.Content = ScreenTexts.MainWindow_SincronizarTabela;
            SyncTableButton.IsEnabled = true;
        }
    }

    private async Task LoadDataAsync()
    {
        var dashboard = await _controller.CarregarAsync();

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

        var selectedEmployeeId = EmployeeFilter.SelectedValue as int? ?? 0;
        var employees = await _controller.ListarColaboradoresAsync();
        _updatingEmployeeFilter = true;
        try
        {
            EmployeeFilter.ItemsSource = new[] { new Colaborador { Id = 0, Nome = "Todos os colaboradores" } }
                .Concat(employees).ToList();
            EmployeeFilter.SelectedValue = employees.Any(employee => employee.Id == selectedEmployeeId)
                ? selectedEmployeeId : 0;
        }
        finally { _updatingEmployeeFilter = false; }
        FilterEmployeeRows();
        _currentPage = 1;
        ShowCurrentPage();

        _agenda = await _controller.ListarAgendaAsync();
        RenderAgenda();

        if (PeriodsGrid.SelectedItem is null && PeriodsGrid.Items.Count > 0)
        {
            PeriodsGrid.SelectedIndex = 0;
        }
    }

    private void FilterEmployeeRows()
    {
        var employeeId = EmployeeFilter.SelectedValue as int? ?? 0;
        _employeeRows = _periodRows
            .Where(row => employeeId == 0 || row.ColaboradorId == employeeId)
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
    }

    private void EmployeeFilterChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingEmployeeFilter || !IsLoaded) return;
        FilterEmployeeRows();
        _currentPage = 1;
        ShowCurrentPage();
        if (PeriodsGrid.Items.Count > 0) PeriodsGrid.SelectedIndex = 0;
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
        if (_visiblePeriodRows.Count == 0)
        {
            _selectedPeriod = null;
            InitialsText.Text = EmployeeText.Text = ExpiryText.Text = RightText.Text = UsedText.Text =
                SoldText.Text = DaysOffText.Text = AbsencesText.Text = BalanceText.Text = RemainingText.Text = string.Empty;
            _updatingPeriodSelector = true;
            try { PeriodSelector.ItemsSource = null; }
            finally { _updatingPeriodSelector = false; }
            HistoryList.ItemsSource = null;
        }

        var first = _employeeRows.Count == 0 ? 0 : ((_currentPage - 1) * PageSize) + 1;
        var last = Math.Min(_currentPage * PageSize, _employeeRows.Count);
        PaginationSummaryText.Text = string.Format(ScreenTexts.MainWindow_MostrandoADeColaboradores, first, last, _employeeRows.Count);
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

    private void PageSizeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ComboBox { SelectedItem: ComboBoxItem item }) return;
        var pageSize = item.Tag?.ToString() == "all" ? int.MaxValue
            : int.TryParse(item.Content?.ToString(), out var parsed) ? parsed : 0;
        if (pageSize <= 0 || pageSize == PageSize)
        {
            return;
        }

        PageSize = pageSize;
        _currentPage = 1;
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
        AgendaMonthText.Text = string.Format(ScreenTexts.MainWindow_AgendaDeFerias2, _agendaMonth);
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
                    string.Format(ScreenTexts.MainWindow_IntervaloAgenda, item.Inicio, item.Fim)))
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
        if (_restoringPeriodSelection || PeriodsGrid.SelectedItem is not PeriodoRow selectedRow)
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
        _updatingPeriodSelector = true;
        PeriodSelector.ItemsSource = _periodRows
            .Where(row => row.ColaboradorId == period.ColaboradorId)
            .OrderByDescending(row => row.Id)
            .ToList();
        PeriodSelector.SelectedValue = period.Id;
        _updatingPeriodSelector = false;
        ExpiryText.Text = period.Vencimento.ToString("dd/MM/yyyy");
        RightText.Text = string.Format(ScreenTexts.MainWindow_Dias, period.DireitoDias);
        UsedText.Text = string.Format(ScreenTexts.MainWindow_Dias, -period.Movimentacoes.Where(m => m.Tipo == TipoMovimentacao.Gozo).Sum(m => m.Dias));
        SoldText.Text = string.Format(ScreenTexts.MainWindow_Dias, period.Vendidos);
        AbsencesText.Text = $"Faltas não justificadas: {period.FaltasNaoJustificadas} dias";
        DaysOffText.Text = string.Format(ScreenTexts.MainWindow_Dias, period.Folgas);
        BalanceText.Text = string.Format(ScreenTexts.MainWindow_Dias, period.Saldo);
        RemainingText.Text = string.Format(ScreenTexts.MainWindow_SaldoRestanteDias2, period.Saldo);

        HistoryList.ItemsSource = period.Movimentacoes
            .OrderByDescending(movement => movement.DataHoraUtc)
            .Select(movement =>
                string.Format(ScreenTexts.MainWindow_MovimentacaoTipoData, movement.Tipo, movement.Inicio) +
                string.Format(ScreenTexts.MainWindow_Dias2, (-movement.Dias)));
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
                string.Format(ScreenTexts.MainWindow_EstePeriodoVenceuEm, _selectedPeriod.Vencimento) +
                ScreenTexts.MainWindow_NaoEPossivelAgendarFeriasAposOVencimento,
                ScreenTexts.MainWindow_PeriodoVencido,
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        var feriados = await _controller.ListarFeriadosAsync();
        var dialog = new ScheduleVacationDialog(
            _selectedPeriod,
            _configuracao.BloquearAgendamentoMenos30Dias,
            _configuracao.BloquearInicioAntesRepousoSemanal,
            _configuracao.DescontarSaldoFeriasPorFaltasNaoJustificadas,
            feriados)
        {
            Owner = this
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var result = await _controller.AgendarAsync(
            _selectedPeriod.Id,
            dialog.Intervalos,
            dialog.DiasAbono,
            dialog.FaltasNaoJustificadas, dialog.ExcluirAgendamentos);

        if (!result.Valido)
        {
            MessageBox.Show(
                string.Join(Environment.NewLine, result.Erros),
                ScreenTexts.MainWindow_AgendamentoNaoPermitido,
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        if (result.Avisos.Count > 0)
        {
            MessageBox.Show(
                string.Join(Environment.NewLine, result.Avisos),
                ScreenTexts.MainWindow_AgendamentoSalvoComAvisos);
        }

        await LoadDataAsync();
    }

    private async void ImportClick(object sender, RoutedEventArgs e)
    {
        var arquivo = new Microsoft.Win32.OpenFileDialog { Filter = "Previsão de férias (*.pdf)|*.pdf", Title = "Importar previsão de férias" };
        if (arquivo.ShowDialog(this) != true) return;
        var unidade = new ComboBox { ItemsSource = new[] { "Washington Luiz", "Gurgel" }, Margin = new Thickness(0, 8, 0, 16) };
        var importar = new Button { Content = "Importar", IsEnabled = false, HorizontalAlignment = HorizontalAlignment.Right };
        var painel = new StackPanel { Margin = new Thickness(24) };
        painel.Children.Add(new TextBlock { Text = "Unidade dos novos colaboradores", FontWeight = FontWeights.SemiBold });
        painel.Children.Add(unidade);
        painel.Children.Add(new TextBlock { Text = "Novos colaboradores terão CPF em branco e admissão estimada pelo início do período mais antigo do PDF. Períodos existentes serão preservados.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 16) });
        painel.Children.Add(importar);
        var dialog = new Window { Title = "Importar previsão de férias", Owner = this, Width = 500,
            SizeToContent = SizeToContent.Height, WindowStartupLocation = WindowStartupLocation.CenterOwner, Content = painel, ResizeMode = ResizeMode.NoResize };
        unidade.SelectionChanged += (_, _) => importar.IsEnabled = unidade.SelectedItem is string;
        importar.Click += (_, _) => dialog.DialogResult = true;
        if (dialog.ShowDialog() != true) return;
        IsEnabled = false;
        try
        {
            var resultado = await _controller.ImportarAsync(arquivo.FileName, (string)unidade.SelectedItem);
            await LoadDataAsync();
            var resumo = $"Importação concluída.\nColaboradores criados: {resultado.ColaboradoresCriados}\nPeríodos criados: {resultado.PeriodosCriados}\nPeríodos já existentes: {resultado.PeriodosExistentes}";
            if (resultado.Avisos.Count > 0) resumo += "\n\nAvisos da importação:\n" + string.Join("\n", resultado.Avisos);
            IsEnabled = true;
            ShowModuleNotice(resultado.Avisos.Count > 0 ? "Importação concluída com avisos" : "Importação concluída", "importIcon", resumo);
        }
        catch (Exception ex)
        {
            IsEnabled = true;
            MessageBox.Show(this, "Não foi possível concluir a importação.\n" + ex.Message, "Importar PDF", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally { IsEnabled = true; }
    }

    private void ShowModuleNotice(string title, string iconKey, string message)
    {
        var icon = (ImageSource)FindResource(iconKey);
        var panel = new StackPanel { Margin = new Thickness(24) };
        var header = new StackPanel { Orientation = Orientation.Horizontal };
        header.Children.Add(new Image { Source = icon, Width = 30, Height = 30, Margin = new Thickness(0, 0, 10, 0) });
        header.Children.Add(new TextBlock { Text = title, FontSize = 22, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, MaxWidth = 420 });
        panel.Children.Add(header);
        panel.Children.Add(new ScrollViewer
        {
            MaxHeight = Math.Max(120, SystemParameters.WorkArea.Height - 240),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Margin = new Thickness(0, 18, 0, 20),
            Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap }
        });
        var close = new Button { Content = ScreenTexts.MainWindow_Entendi, IsCancel = true, IsDefault = true, HorizontalAlignment = HorizontalAlignment.Right };
        panel.Children.Add(close);
        var dialog = new Window
        {
            Owner = this, Title = title, Icon = icon, Width = 520,
            SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = new SolidColorBrush(Color.FromRgb(248, 250, 252)), Content = panel
        };
        close.Click += (_, _) => dialog.Close();
        dialog.ShowDialog();
    }
    private async void ModuleClick(object sender, RoutedEventArgs e)
    {
        var moduleName = (sender as FrameworkElement)?.Tag?.ToString();
        if (moduleName == ScreenTexts.Holidays_Titulo)
        {
            new HolidaysWindow(_feriados) { Owner = this }.ShowDialog();
            return;
        }
        if (moduleName == ScreenTexts.MainWindow_Relatorios)
        {
            new ReportsWindow(_periodos) { Owner = this }.ShowDialog();
            return;
        }
        if (moduleName == ScreenTexts.MainWindow_AgendaDeFerias)
        {
            new VacationAgendaWindow(_controller) { Owner = this }.ShowDialog();
            return;
        }
        if (moduleName == ScreenTexts.MainWindow_Colaboradores)
        {
            var window = new EmployeesWindow(_controller)
            {
                Owner = this
            };

            window.ShowDialog();
            if (window.Changed)
            {
                await LoadDataAsync();
            }

            return;
        }

        if (moduleName == ScreenTexts.MainWindow_Configuracoes)
        {
            new SettingsWindow(_configuracao) { Owner = this }.ShowDialog();
            return;
        }

        if (moduleName == ScreenTexts.MainWindow_Regras)
        {
            new RulesWindow { Owner = this }.ShowDialog();
            return;
        }

        ShowModuleNotice(
            moduleName ?? ScreenTexts.MainWindow_ControleDeFerias,
            moduleName == ScreenTexts.MainWindow_AgendaDeFerias ? "travelIcon" : "dashboardIcon",
            string.Format(ScreenTexts.MainWindow_ModuloPreparadoNoDesignSystem, moduleName) +
            ScreenTexts.MainWindow_OsDadosDestaEntregaSaoAdministradosPeloDashboard);
    }

    private async void DayOffClick(object sender, RoutedEventArgs e)
    {
        if (_selectedPeriod is null) return;
        var period = _selectedPeriod;
        if (period.Vencimento.Date < DateTime.Today)
        {
            MessageBox.Show(
                string.Format(ScreenTexts.MainWindow_EstePeriodoVenceuEm, period.Vencimento) +
                ScreenTexts.MainWindow_NaoEPossivelRegistrarFolgaAposOVencimento,
                ScreenTexts.MainWindow_PeriodoVencido,
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }
        if (period.Saldo <= 0)
        {
            MessageBox.Show(ScreenTexts.MainWindow_OPeriodoNaoPossuiSaldoDisponivelParaRegistrar,
                ScreenTexts.MainWindow_RegistrarFolga2, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        var dialog = new RegisterDayOffDialog(period, _controller) { Owner = this };
        if (dialog.ShowDialog() != true) return;

        try
        {
            _restoringPeriodSelection = true;
            _selectedPeriodIds[period.ColaboradorId] = period.Id;
            await LoadDataAsync();
            var index = _employeeRows.FindIndex(row => row.Id == period.Id);
            if (index >= 0)
            {
                _currentPage = index / PageSize + 1;
                ShowCurrentPage();
                PeriodsGrid.SelectedItem = _visiblePeriodRows.First(row => row.Id == period.Id);
            }
            _selectedPeriod = await _controller.SelecionarAsync(period.Id);
            if (_selectedPeriod is not null) ShowPeriodDetails(_selectedPeriod);
        }
        catch (Exception)
        {
            MessageBox.Show(ScreenTexts.MainWindow_AFolgaFoiRegistradaMasNaoFoiPossivel,
                ScreenTexts.MainWindow_AtualizacaoDoPainel, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            _restoringPeriodSelection = false;
        }
    }

    private void ApplyResponsiveLayout()
    {
        var rootGrid = DashboardLayout;
        var navigationWidth = ActualWidth < 1280 ? 76 : 230;
        var detailsWidth = ActualWidth < 1280 ? 330 : 410;

        rootGrid.ColumnDefinitions[0].Width = new GridLength(navigationWidth);
        rootGrid.ColumnDefinitions[2].Width = new GridLength(detailsWidth);
    }
}
