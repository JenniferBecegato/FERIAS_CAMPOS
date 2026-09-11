using System.Windows;
using System.Windows.Controls;
using System.Globalization;
using FeriasCampos.Models;
using FeriasCampos.Properties;
using FeriasCampos.Services;

namespace FeriasCampos.Views;

public partial class HolidaysWindow : Window
{
    private readonly IFeriadoService _service;
    private int? _editingId;
    private bool _busy;

    public HolidaysWindow(IFeriadoService service)
    {
        InitializeComponent();
        _service = service;
        var culture = CultureInfo.GetCultureInfo("pt-BR");
        MonthBox.ItemsSource = Enumerable.Range(1, 12).Select(month => new
        {
            Numero = month,
            Nome = culture.TextInfo.ToTitleCase(culture.DateTimeFormat.GetMonthName(month))
        }).ToList();
        Loaded += async (_, _) => await RunAsync(LoadAsync);
    }

    private async Task LoadAsync() => HolidaysGrid.ItemsSource = await _service.ListarAsync();

    private void NewClick(object sender, RoutedEventArgs e) => ShowForm(null);
    private void EditClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: Feriado item }) ShowForm(item);
    }

    private void ShowForm(Feriado? item)
    {
        _editingId = item?.Id;
        FormTitle.Text = item is null ? ScreenTexts.Holidays_Novo : ScreenTexts.Holidays_Editar;
        NameBox.Text = item?.Nome ?? string.Empty;
        MonthBox.SelectedValue = item?.Mes ?? 1;
        DayBox.SelectedItem = item?.Dia ?? 1;
        ErrorText.Text = string.Empty;
        FormColumn.Width = new GridLength(330);
        FormPanel.Visibility = Visibility.Visible;
        NameBox.Focus();
    }

    private void CancelClick(object sender, RoutedEventArgs e) => HideForm();
    private void MonthChanged(object sender, SelectionChangedEventArgs e)
    {
        if (MonthBox.SelectedValue is not int month || DayBox is null) return;
        var selectedDay = DayBox.SelectedItem is int day ? day : 1;
        // Sem ano no cadastro: fevereiro admite 29 dias para a recorrência bissexta.
        var daysInMonth = DateTime.DaysInMonth(2000, month);
        DayBox.ItemsSource = Enumerable.Range(1, daysInMonth);
        DayBox.SelectedItem = Math.Min(selectedDay, daysInMonth);
    }
    private void HideForm()
    {
        _editingId = null;
        FormPanel.Visibility = Visibility.Collapsed;
        FormColumn.Width = new GridLength(0);
        ErrorText.Text = string.Empty;
    }

    private async void SaveClick(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        var dia = DayBox.SelectedItem is int d ? d : 0;
        var mes = MonthBox.SelectedValue is int m ? m : 0;
        var result = _editingId is int id
            ? await _service.AlterarAsync(id, NameBox.Text, dia, mes)
            : await _service.CadastrarAsync(NameBox.Text, dia, mes);
        if (!result.Valido) { ErrorText.Text = string.Join(Environment.NewLine, result.Erros); return; }
        HideForm();
        await LoadAsync();
    });

    private async void DeleteClick(object sender, RoutedEventArgs e)
    {
        if (_busy || sender is not FrameworkElement { DataContext: Feriado item }) return;
        if (MessageBox.Show(this, string.Format(ScreenTexts.Holidays_ConfirmarExclusao, item.Nome),
            ScreenTexts.Holidays_Titulo, MessageBoxButton.YesNo, MessageBoxImage.Question,
            MessageBoxResult.No) != MessageBoxResult.Yes) return;
        await RunAsync(async () =>
        {
            var result = await _service.ExcluirAsync(item.Id);
            if (!result.Valido) { ErrorText.Text = string.Join(Environment.NewLine, result.Erros); return; }
            if (_editingId == item.Id) HideForm();
            await LoadAsync();
        });
    }

    private async Task RunAsync(Func<Task> action)
    {
        if (_busy) return;
        _busy = true;
        RootPanel.IsEnabled = false;
        ErrorText.Text = string.Empty;
        try { await action(); }
        catch (Exception) { ErrorText.Text = ScreenTexts.Holidays_Falha; }
        finally { _busy = false; RootPanel.IsEnabled = true; }
    }
}
