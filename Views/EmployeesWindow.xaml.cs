using System.Windows;
using FeriasCampos.Controllers;
using FeriasCampos.Models;

namespace FeriasCampos.Views;

public partial class EmployeesWindow : Window
{
    private readonly DashboardController _controller;

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
        FormColumn.Width = new GridLength(370);
        FormPanel.Visibility = Visibility.Visible;
        NameBox.Focus();
    }

    private void CancelClick(object sender, RoutedEventArgs e)
    {
        FormPanel.Visibility = Visibility.Collapsed;
        FormColumn.Width = new GridLength(0);
        ErrorText.Text = string.Empty;
    }

    private async void SaveClick(object sender, RoutedEventArgs e)
    {
        var cpfDigits = new string(CpfBox.Text.Where(char.IsDigit).ToArray());
        var newEmployee = new NovoColaboradorDto(
            NameBox.Text,
            CpfBox.Text,
            $"AUTO-{cpfDigits}",
            AdmissionPicker.SelectedDate ?? DateTime.MaxValue,
            string.Empty,
            string.Empty,
            string.Empty,
            30);

        var result = await _controller.CadastrarColaboradorAsync(newEmployee);
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
            "Colaborador cadastrado com sucesso.",
            "Colaboradores",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void ClearForm()
    {
        NameBox.Clear();
        CpfBox.Clear();
        AdmissionPicker.SelectedDate = null;
    }
}
