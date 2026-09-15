using FeriasCampos.Properties;
using System.Data;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using FeriasCampos.Models;
using FeriasCampos.Services;
using Microsoft.Win32;

namespace FeriasCampos.Views;

public partial class ReportsWindow : Window
{
    private readonly IPeriodoService _periodos;
    private ResultadoRelatorio? _result;
    private sealed record EmployeeOption(int? Id, string Label);
    private sealed record UnitOption(string? Value, string Label);
    public ReportsWindow(IPeriodoService periodos)
    {
        InitializeComponent();
        _periodos = periodos;
        GenerateButton.IsEnabled = FiltersPanel.IsEnabled = false;
        Loaded += async (_, _) =>
        {
            try
            {
                var data = await _periodos.ListarAsync();
                var employees = new List<EmployeeOption> { new(null, ScreenTexts.ReportsWindow_TodosColaboradores) };
                employees.AddRange(data.Select(p => p.Colaborador).DistinctBy(c => c.Id).OrderBy(c => c.Nome)
                    .Select(c => new EmployeeOption(c.Id, $"{c.Nome} — {(string.IsNullOrEmpty(c.Unidade) ? ScreenTexts.ReportsWindow_NaoInformado : c.Unidade)} (código {c.Id})")));
                EmployeeFilter.ItemsSource = employees;
                EmployeeFilter.SelectedIndex = 0;
                var units = new List<UnitOption> { new(null, ScreenTexts.ReportsWindow_TodasUnidades) };
                units.AddRange(data.Select(p => p.Colaborador.Unidade).Distinct().Order()
                    .Select(u => new UnitOption(u, string.IsNullOrEmpty(u) ? ScreenTexts.ReportsWindow_NaoInformado : u)));
                UnitFilter.ItemsSource = units;
                UnitFilter.SelectedIndex = 0;
                await GenerateAsync();
            }
            catch (Exception ex) { MessageText.Text = string.Format(ScreenTexts.ReportsWindow_NaoFoiPossivelCarregarOsRelatorios, ex.Message); }
        };
    }
    private FiltroRelatorio ReadFilter() => new()
    {
        ColaboradorId = (EmployeeFilter.SelectedItem as EmployeeOption)?.Id,
        Unidade = (UnitFilter.SelectedItem as UnitOption)?.Value,
        De = FromFilter.SelectedDate, Ate = ToFilter.SelectedDate
    };
    private async void GenerateClick(object sender, RoutedEventArgs e) => await GenerateAsync();
    private void ResultColumnGenerated(object sender, DataGridAutoGeneratingColumnEventArgs e)
    {
        if (e.Column is not DataGridTextColumn column) return;
        var index = Array.IndexOf(_result!.Colunas, e.PropertyName);
        int[] widths = [150, 80, 100, 100, 95, 90, 70, 95];
        column.MinWidth = 70;
        column.Width = new DataGridLength(widths[index]);
        column.MaxWidth = 360;
        column.Binding = new Binding(e.PropertyName)
        {
            StringFormat = e.PropertyType == typeof(DateTime) ? "dd/MM/yyyy" : e.PropertyType == typeof(decimal) ? "0.##" : null,
            ConverterCulture = System.Globalization.CultureInfo.GetCultureInfo("pt-BR")
        };
        var style = new Style(typeof(TextBlock));
        style.Setters.Add(new Setter(TextBlock.PaddingProperty, new Thickness(8, 0, 8, 0)));
        style.Setters.Add(new Setter(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center));
        style.Setters.Add(new Setter(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis));
        style.Setters.Add(new Setter(TextBlock.ToolTipProperty, new Binding(e.PropertyName)));
        column.ElementStyle = style;
    }
    private async Task GenerateAsync()
    {
        GenerateButton.IsEnabled = XlsxButton.IsEnabled = PdfButton.IsEnabled = false;
        FiltersPanel.IsEnabled = false;
        try
        {
            var filter = ReadFilter();
            var periods = await _periodos.ListarAsync();
            _result = RelatorioEngine.Gerar(periods, filter, DateTime.Now);
            var table = new DataTable();
            for (var i = 0; i < _result.Colunas.Length; i++)
            {
                table.Columns.Add(_result.Colunas[i], i is 2 or 3 ? typeof(DateTime) : i is >= 4 and <= 6 ? typeof(decimal) : i == 7 ? typeof(int) : typeof(string));
            }
            foreach (var row in _result.Linhas) table.Rows.Add(row.Valores);
            ResultsGrid.ItemsSource = table.DefaultView;
            ResultTitle.Text = _result.Titulo;
            AppliedFilters.Text = string.Format(ScreenTexts.ReportsWindow_GeradoEm, _result.GeradoEm, _result.Filtros);
            TotalsText.Text = _result.Totais;
            MessageText.Text = _result.Linhas.Count == 0 ? ScreenTexts.ReportsWindow_NenhumRegistroEncontradoAjusteOsFiltrosEGere : ScreenTexts.ReportsWindow_ExportacoesIncluemTodosOsRegistrosEOsFiltros;
        }
        catch (Exception ex)
        {
            _result = null; ResultsGrid.ItemsSource = null; TotalsText.Text = ""; AppliedFilters.Text = ""; ResultTitle.Text = "";
            MessageText.Text = string.Format(ScreenTexts.ReportsWindow_NaoFoiPossivelGerarORelatorio, ex.Message);
        }
        finally { GenerateButton.IsEnabled = FiltersPanel.IsEnabled = true; XlsxButton.IsEnabled = PdfButton.IsEnabled = _result is not null; }
    }
    private void ClearClick(object sender, RoutedEventArgs e)
    {
        EmployeeFilter.SelectedIndex = UnitFilter.SelectedIndex = 0;
        FromFilter.SelectedDate = ToFilter.SelectedDate = null;
        MessageText.Text = ScreenTexts.ReportsWindow_FiltrosLimposCliqueEmGerarRelatorioParaAtualizar;
    }
    private async void XlsxClick(object sender, RoutedEventArgs e) => await ExportAsync(false);
    private async void PdfClick(object sender, RoutedEventArgs e) => await ExportAsync(true);
    private async Task ExportAsync(bool pdf)
    {
        if (_result is not { } report) return;
        var dialog = new SaveFileDialog { Filter = pdf ? ScreenTexts.ReportsWindow_DocumentoPDFPdf : ScreenTexts.ReportsWindow_ArquivoXlsx, FileName = string.Format(ScreenTexts.ReportsWindow_RelatorioFerias, report.GeradoEm), AddExtension = true };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            if (pdf) await File.WriteAllBytesAsync(dialog.FileName, RelatorioExportacao.Pdf(report));
            else await File.WriteAllBytesAsync(dialog.FileName, RelatorioExportacao.Xlsx(report));
            MessageBox.Show(this, string.Format(ScreenTexts.ReportsWindow_RelatorioExportadoPara, dialog.FileName), ScreenTexts.ReportsWindow_ExportacaoConcluida, MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex) { MessageBox.Show(this, string.Format(ScreenTexts.ReportsWindow_NaoFoiPossivelExportar, ex.Message), ScreenTexts.ReportsWindow_Exportacao, MessageBoxButton.OK, MessageBoxImage.Error); }
    }
}
