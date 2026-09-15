using FeriasCampos.Properties;
using System.Windows;
using FeriasCampos.Controllers;
using FeriasCampos.Models;

namespace FeriasCampos.Views;

public partial class EmployeesWindow : Window
{
    private readonly DashboardController _controller;
    private Colaborador? _editing;
    private bool _busy;

    public EmployeesWindow(DashboardController controller)
    {
        InitializeComponent();
        _controller = controller;
        Loaded += async (_, _) => await LoadEmployeesAsync();
    }

    public bool Changed { get; private set; }

    private async Task LoadEmployeesAsync()
    {
        EmployeesGrid.ItemsSource = await _controller.ListarColaboradoresAsync();
    }

    private void NewClick(object sender, RoutedEventArgs e)
    {
        ClearForm();
        ShowForm();
    }

    private async void EditClick(object sender, RoutedEventArgs e)
    {
        if (_busy || sender is not FrameworkElement { DataContext: Colaborador employee }) return;
        ClearForm();
        _editing = employee;
        FormTitle.Text = ScreenTexts.EmployeesWindow_AlterarColaborador;
        NameBox.Text = employee.Nome;
        CpfBox.Text = employee.Cpf;
        AdmissionPicker.SelectedDate = employee.Admissao;
        UnitBox.SelectedValue = employee.Unidade;
        ShowForm();
        _busy = true;
        RootPanel.IsEnabled = false;
        try { await LoadPeriodsAsync(employee.Id); }
        catch (Exception) { ErrorText.Text = ScreenTexts.EmployeesWindow_NaoFoiPossivelConcluirAOperacaoFecheE; }
        finally { _busy = false; RootPanel.IsEnabled = true; }
    }

    private async Task LoadPeriodsAsync(int employeeId)
    {
        var periods = await _controller.ListarPeriodosAsync(employeeId);
        EmployeePeriods.ItemsSource = periods.Select(p => new
        {
            p.Id,
            Datas = $"Aquisitivo: {p.Inicio:dd/MM/yyyy} a {p.Fim:dd/MM/yyyy}",
            Saldo = $"Saldo de férias: {p.Saldo:0.##} dias",
            Faltas = $"Faltas não justificadas: {p.FaltasNaoJustificadas} dias",
            Folgas = $"Folgas descontadas: {p.Folgas:0.##} dias"
        }).ToList();
        PeriodsPanel.Visibility = Visibility.Visible;
        EmptyPeriodsText.Visibility = periods.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void DeletePeriodClick(object sender, RoutedEventArgs e)
    {
        if (_busy || _editing is null || sender is not FrameworkElement { Tag: int periodId }) return;
        _busy = true;
        RootPanel.IsEnabled = false;
        try
        {
            var period = await _controller.SelecionarAsync(periodId);
            if (period is null) { await LoadPeriodsAsync(_editing.Id); return; }
            if (MessageBox.Show(this,
                $"Excluir o período aquisitivo de {period.Inicio:dd/MM/yyyy} a {period.Fim:dd/MM/yyyy}? Os agendamentos e todas as movimentações deste período serão removidos. Esta ação é imediata e não pode ser desfeita.",
                "Excluir período", MessageBoxButton.YesNo,
                MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes) return;
            var result = await _controller.ExcluirPeriodoAsync(_editing.Id, periodId);
            if (!result.Valido) { ErrorText.Text = string.Join(Environment.NewLine, result.Erros); return; }
            Changed = true;
            ErrorText.Text = string.Empty;
            await LoadPeriodsAsync(_editing.Id);
        }
        catch (Exception) { ErrorText.Text = ScreenTexts.EmployeesWindow_NaoFoiPossivelConcluirAOperacaoFecheE; }
        finally { _busy = false; RootPanel.IsEnabled = true; }
    }

    private void ShowForm()
    {
        FormColumn.Width = new GridLength(370);
        FormPanel.Visibility = Visibility.Visible;
        NameBox.Focus();
    }

    private void CancelClick(object sender, RoutedEventArgs e)
    {
        FormPanel.Visibility = Visibility.Collapsed;
        FormColumn.Width = new GridLength(0);
        ErrorText.Text = string.Empty;
        ClearForm();
    }

    private async void SaveClick(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        _busy = true;
        RootPanel.IsEnabled = false;
        try
        {
            var editing = _editing;
            var newEmployee = new NovoColaboradorDto(
                NameBox.Text,
                CpfBox.Text,
                AdmissionPicker.SelectedDate ?? DateTime.MaxValue,
                UnitBox.SelectedValue?.ToString() ?? string.Empty,
                30);

            var result = editing is null
                ? await _controller.CadastrarColaboradorAsync(newEmployee)
                : await _controller.AlterarColaboradorAsync(editing.Id, newEmployee);
            if (!result.Valido)
            {
                ErrorText.Text = string.Join(Environment.NewLine, result.Erros);
                return;
            }

            Changed = true;
            await LoadEmployeesAsync();
            ClearForm();
            CancelClick(sender, e);

            MessageBox.Show(
                editing is null ? ScreenTexts.EmployeesWindow_ColaboradorCadastradoComSucesso : ScreenTexts.EmployeesWindow_ColaboradorAlteradoComSucesso,
                ScreenTexts.EmployeesWindow_Colaboradores,
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception)
        {
            ErrorText.Text = ScreenTexts.EmployeesWindow_NaoFoiPossivelConcluirAOperacaoFecheE;
        }
        finally
        {
            _busy = false;
            RootPanel.IsEnabled = true;
        }
    }

    private async void DeleteClick(object sender, RoutedEventArgs e)
    {
        if (_busy || sender is not FrameworkElement { DataContext: Colaborador employee }) return;
        if (MessageBox.Show(this,
                string.Format(ScreenTexts.EmployeesWindow_ExcluirOColaboradorTodosOsPeriodosDeFerias, employee.Nome),
                ScreenTexts.EmployeesWindow_ExcluirColaborador, MessageBoxButton.YesNo, MessageBoxImage.Warning,
                MessageBoxResult.No) != MessageBoxResult.Yes) return;
        _busy = true;
        RootPanel.IsEnabled = false;
        try
        {
            var result = await _controller.ExcluirColaboradorAsync(employee.Id);
            if (!result.Valido)
            {
                MessageBox.Show(this, string.Join(Environment.NewLine, result.Erros), ScreenTexts.EmployeesWindow_Colaboradores,
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            Changed = true;
            if (_editing?.Id == employee.Id) CancelClick(sender, e);
            await LoadEmployeesAsync();
        }
        catch (Exception)
        {
            MessageBox.Show(this, ScreenTexts.EmployeesWindow_NaoFoiPossivelConcluirAOperacaoFecheE,
                ScreenTexts.EmployeesWindow_Colaboradores, MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _busy = false;
            RootPanel.IsEnabled = true;
        }
    }

    private void ClearForm()
    {
        _editing = null;
        EmployeePeriods.ItemsSource = null;
        PeriodsPanel.Visibility = Visibility.Collapsed;
        FormTitle.Text = ScreenTexts.EmployeesWindow_NovoColaborador2;
        ErrorText.Text = string.Empty;
        NameBox.Clear();
        CpfBox.Clear();
        AdmissionPicker.SelectedDate = null;
        UnitBox.SelectedIndex = 0;
    }
}
