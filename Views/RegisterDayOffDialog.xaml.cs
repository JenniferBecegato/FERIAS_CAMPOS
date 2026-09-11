using FeriasCampos.Properties;
using System.Windows;
using FeriasCampos.Controllers;
using FeriasCampos.Models;

namespace FeriasCampos.Views;

public partial class RegisterDayOffDialog : Window
{
    private readonly PeriodoAquisitivo _period;
    private readonly DashboardController _controller;
    private bool _saving;

    public RegisterDayOffDialog(PeriodoAquisitivo period, DashboardController controller)
    {
        InitializeComponent();
        _period = period;
        _controller = controller;
        EmployeeText.Text = period.Colaborador.Nome;
        PeriodText.Text = string.Format(ScreenTexts.RegisterDayOffDialog_PeriodoA, period.Inicio, period.Fim);
        BalanceText.Text = string.Format(ScreenTexts.RegisterDayOffDialog_SaldoDisponivelDias, period.Saldo);
        Loaded += (_, _) => DaysBox.Focus();
        Closing += (_, e) => e.Cancel = _saving;
    }

    private async void SaveClick(object sender, RoutedEventArgs e)
    {
        if (_saving) return;
        if (!int.TryParse(DaysBox.Text, out var days) || days <= 0)
        {
            ValidationText.Text = ScreenTexts.RegisterDayOffDialog_InformeUmaQuantidadeInteiraDeDiasMaiorQue;
            return;
        }
        if (string.IsNullOrWhiteSpace(ReasonBox.Text))
        {
            ValidationText.Text = ScreenTexts.RegisterDayOffDialog_InformeAJustificativaDaFolga;
            return;
        }

        _saving = true;
        SaveButton.IsEnabled = CancelButton.IsEnabled = false;
        DaysBox.IsEnabled = ReasonBox.IsEnabled = false;
        ValidationText.Text = "";
        try
        {
            var result = await _controller.RegistrarFolgaAsync(_period.Id, days, ReasonBox.Text);
            if (!result.Valido)
            {
                ValidationText.Text = string.Join(Environment.NewLine, result.Erros);
                return;
            }
            _saving = false;
            DialogResult = true;
        }
        catch (Exception)
        {
            ValidationText.Text = ScreenTexts.RegisterDayOffDialog_NaoFoiPossivelConcluirORegistroFecheEsta;
        }
        finally
        {
            _saving = false;
            SaveButton.IsEnabled = CancelButton.IsEnabled = true;
            DaysBox.IsEnabled = ReasonBox.IsEnabled = true;
        }
    }
}
