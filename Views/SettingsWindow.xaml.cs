using System.Windows;
using FeriasCampos.Services;

namespace FeriasCampos.Views;

public partial class SettingsWindow : Window
{
    private readonly IConfiguracaoService _configuracao;

    public SettingsWindow(IConfiguracaoService configuracao)
    {
        InitializeComponent();
        _configuracao = configuracao;
        BlockNoticeCheckBox.IsChecked =
            configuracao.BloquearAgendamentoMenos30Dias;
        BlockWeeklyRestCheckBox.IsChecked =
            configuracao.BloquearInicioAntesRepousoSemanal;
    }

    private void CancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void SaveClick(object sender, RoutedEventArgs e)
    {
        _configuracao.BloquearAgendamentoMenos30Dias =
            BlockNoticeCheckBox.IsChecked == true;
        _configuracao.BloquearInicioAntesRepousoSemanal =
            BlockWeeklyRestCheckBox.IsChecked == true;
        _configuracao.Salvar();
        DialogResult = true;
    }
}
