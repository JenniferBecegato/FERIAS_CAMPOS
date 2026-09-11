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
    private bool _ready;
    public ReportsWindow(IPeriodoService periodos)
    {
        InitializeComponent();
        _periodos = periodos;
        ReportType.ItemsSource = RelatorioEngine.Titulos;
        ReportType.SelectedIndex = 0;
        Loaded += async (_, _) =>
        {
            try
            {
                var data = await _periodos.ListarAsync();
                static string Option(string value) => string.IsNullOrEmpty(value) ? ScreenTexts.ReportsWindow_NaoInformado : value;
                UnitsFilter.ItemsSource = data.Select(p => Option(p.Colaborador.Unidade)).Distinct().Order().ToList();
                StatusFilter.ItemsSource = new[] { ScreenTexts.ReportsWindow_EmAquisicao, ScreenTexts.ReportsWindow_Disponivel, ScreenTexts.ReportsWindow_Parcial, ScreenTexts.ReportsWindow_Completo, ScreenTexts.ReportsWindow_Vencido };
                _ready = true;
                await GenerateAsync();
            }
            catch (Exception ex) { MessageText.Text = string.Format(ScreenTexts.ReportsWindow_NaoFoiPossivelCarregarOsRelatorios, ex.Message); }
        };
    }
    private TipoRelatorio Tipo => (TipoRelatorio)Math.Max(0, ReportType.SelectedIndex);
    private void ReportTypeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DateLabel is null) return;
        DateLabel.Text = RelatorioEngine.DataLabel(Tipo);
        ExpiryOptions.Visibility = Tipo == TipoRelatorio.Vencimentos ? Visibility.Visible : Visibility.Collapsed;
        PendingOptions.Visibility = Tipo == TipoRelatorio.Pendencias ? Visibility.Visible : Visibility.Collapsed;
        OverlapOptions.Visibility = Tipo == TipoRelatorio.Sobreposicoes ? Visibility.Visible : Visibility.Collapsed;
        MovementOptions.Visibility = RelatorioEngine.Movimentos(Tipo) ? Visibility.Visible : Visibility.Collapsed;
        MovementTypesFilter.ItemsSource = Tipo == TipoRelatorio.VendasFolgas ? new[] { TipoMovimentacao.Venda, TipoMovimentacao.Folga } : Enum.GetValues<TipoMovimentacao>();
        FromFilter.SelectedDate = ToFilter.SelectedDate = null;
        if (_ready) MessageText.Text = ScreenTexts.ReportsWindow_CliqueEmGerarRelatorioParaAplicarANova;
    }
    private static int? Number(TextBox box, string label)
    {
        if (string.IsNullOrWhiteSpace(box.Text)) return null;
        if (!int.TryParse(box.Text, out var n)) throw new ArgumentException(string.Format(ScreenTexts.ReportsWindow_InformeUmNumeroInteiro, label));
        return n;
    }
    private FiltroRelatorio ReadFilter()
    {
        static string[] Items(ListBox box) => box.SelectedItems.Cast<string>().Select(v => v == ScreenTexts.ReportsWindow_NaoInformado ? "" : v).ToArray();
        return new()
        {
            Tipo = Tipo, Nome = NameFilter.Text.Trim(),
            Unidades = Items(UnitsFilter), Status = Items(StatusFilter),
            De = FromFilter.SelectedDate, Ate = ToFilter.SelectedDate,
            SaldoMinimo = Number(MinBalanceFilter, ScreenTexts.ReportsWindow_SaldoMinimo), SaldoMaximo = Number(MaxBalanceFilter, ScreenTexts.ReportsWindow_SaldoMaximo),
            ApenasSaldoPendente = PositiveBalanceFilter.IsChecked == true, Prazo = new[] { 0, -1, 30, 60, 90 }[ExpiryFilter.SelectedIndex],
            Programacao = PendingFilter.SelectedIndex, MinimoAusentes = Tipo == TipoRelatorio.Sobreposicoes ? Number(MinPeopleFilter, ScreenTexts.ReportsWindow_MinimoDePessoas) ?? 2 : 2,
            TiposMovimentacao = MovementTypesFilter.SelectedItems.Cast<TipoMovimentacao>().ToArray(), Identificacao = IdentificationFilter.Text, Motivo = ReasonFilter.Text
        };
    }
    private async void GenerateClick(object sender, RoutedEventArgs e) => await GenerateAsync();
    private void ResultColumnGenerated(object sender, DataGridAutoGeneratingColumnEventArgs e)
    {
        if (e.Column is not DataGridTextColumn column) return;
        column.MinWidth = (e.PropertyName == ScreenTexts.ReportsWindow_Colaborador || e.PropertyName == ScreenTexts.ReportsWindow_PeriodoAquisitivo) ? 185 : 110;
        column.MaxWidth = 360;
        var style = new Style(typeof(TextBlock));
        style.Setters.Add(new Setter(TextBlock.PaddingProperty, new Thickness(8, 0, 8, 0)));
        style.Setters.Add(new Setter(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center));
        style.Setters.Add(new Setter(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis));
        style.Setters.Add(new Setter(TextBlock.ToolTipProperty, new Binding(e.PropertyName)));
        column.ElementStyle = style;
    }
    private async Task GenerateAsync()
    {
        GenerateButton.IsEnabled = CsvButton.IsEnabled = PdfButton.IsEnabled = false;
        FiltersPanel.IsEnabled = false;
        try
        {
            var filter = ReadFilter();
            var periods = await _periodos.ListarAsync();
            _result = RelatorioEngine.Gerar(periods, filter, DateTime.Today);
            var table = new DataTable();
            for (var i = 0; i < _result.Colunas.Length; i++)
            {
                var sample = _result.Linhas.Select(r => r.Valores[i]).FirstOrDefault(v => v is not string);
                table.Columns.Add(_result.Colunas[i], sample is int ? typeof(int) : typeof(string));
            }
            foreach (var row in _result.Linhas) table.Rows.Add(row.Valores.Select(v => v is DateTime ? RelatorioExportacao.Texto(v) : v).ToArray());
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
        finally { GenerateButton.IsEnabled = FiltersPanel.IsEnabled = true; CsvButton.IsEnabled = PdfButton.IsEnabled = _result is not null; }
    }
    private void ClearClick(object sender, RoutedEventArgs e)
    {
        NameFilter.Clear(); IdentificationFilter.Clear(); ReasonFilter.Clear(); MinBalanceFilter.Clear(); MaxBalanceFilter.Clear();
        foreach (var box in new[] { UnitsFilter, StatusFilter, MovementTypesFilter }) box.UnselectAll();
        ExpiryFilter.SelectedIndex = PendingFilter.SelectedIndex = 0;
        PositiveBalanceFilter.IsChecked = false; MinPeopleFilter.Text = "2"; FromFilter.SelectedDate = ToFilter.SelectedDate = null;
        MessageText.Text = ScreenTexts.ReportsWindow_FiltrosLimposCliqueEmGerarRelatorioParaAtualizar;
    }
    private async void CsvClick(object sender, RoutedEventArgs e) => await ExportAsync(false);
    private async void PdfClick(object sender, RoutedEventArgs e) => await ExportAsync(true);
    private async Task ExportAsync(bool pdf)
    {
        if (_result is not { } report) return;
        var dialog = new SaveFileDialog { Filter = pdf ? ScreenTexts.ReportsWindow_DocumentoPDFPdf : ScreenTexts.ReportsWindow_CSVCompativelComExcelCsv, FileName = string.Format(ScreenTexts.ReportsWindow_RelatorioFerias, report.GeradoEm), AddExtension = true };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            if (pdf) await File.WriteAllBytesAsync(dialog.FileName, RelatorioExportacao.Pdf(report));
            else await RelatorioExportacao.SalvarCsvAsync(report, dialog.FileName);
            MessageBox.Show(this, string.Format(ScreenTexts.ReportsWindow_RelatorioExportadoPara, dialog.FileName), ScreenTexts.ReportsWindow_ExportacaoConcluida, MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex) { MessageBox.Show(this, string.Format(ScreenTexts.ReportsWindow_NaoFoiPossivelExportar, ex.Message), ScreenTexts.ReportsWindow_Exportacao, MessageBoxButton.OK, MessageBoxImage.Error); }
    }
}

