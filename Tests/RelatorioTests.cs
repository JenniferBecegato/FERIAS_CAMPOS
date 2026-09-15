using System.IO;
using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using FeriasCampos.Models;
using FeriasCampos.Services;
using Xunit;

namespace FeriasCampos.Tests;

public sealed class RelatorioTests
{
    private static readonly DateTime Hoje = new(2026, 9, 15);
    private static PeriodoAquisitivo Period(int id, int employee = 1, string unit = "Centro", string name = "Ana") => new()
    {
        Id = id, ColaboradorId = employee, Colaborador = new() { Id = employee, Nome = name, Unidade = unit },
        Inicio = new(2025, 1, 1), Fim = new(2025, 12, 31), Vencimento = new(2026, 12, 31),
        FaltasNaoJustificadas = 3,
        Movimentacoes = [new() { Tipo = TipoMovimentacao.Aquisicao, Dias = 30 },
            new() { Tipo = TipoMovimentacao.Agendamento, Dias = -5 },
            new() { Tipo = TipoMovimentacao.Folga, Dias = -0.5m }]
    };
    private static ResultadoRelatorio Run(FiltroRelatorio f, params PeriodoAquisitivo[] p) => RelatorioEngine.Gerar(p, f, Hoje);

    [Fact]
    public void Filtros_combinam_id_unidade_e_datas_sem_confundir_homonimos()
    {
        var a = Period(1); var b = Period(2, 2); var c = Period(3, 3, "");
        Assert.Equal(3, Run(new(), a, b, c).Linhas.Count);
        Assert.Equal(2, Run(new() { Unidade = "Centro" }, a, b, c).Linhas.Count);
        Assert.Equal(2, Assert.Single(Run(new() { ColaboradorId = 2, Unidade = "Centro", De = new(2025, 6, 1), Ate = new(2025, 7, 1) }, a, b, c).Linhas).Periodos[0]);
        Assert.Single(Run(new() { Unidade = "" }, a, b, c).Linhas);
        Assert.Empty(Run(new() { ColaboradorId = 2, Unidade = "" }, a, b, c).Linhas);
        Assert.Empty(Run(new() { ColaboradorId = 99 }, a, b, c).Linhas);
    }

    [Theory]
    [InlineData("2024-12-01", "2025-01-01", true)]
    [InlineData("2025-12-31", "2026-02-01", true)]
    [InlineData("2025-06-01", "2025-06-01", true)]
    [InlineData("2024-01-01", "2026-01-01", true)]
    [InlineData("2026-01-01", null, false)]
    [InlineData(null, "2024-12-31", false)]
    [InlineData("2025-12-31", null, true)]
    [InlineData(null, "2025-01-01", true)]
    public void Vigencia_usa_aquisicao_e_limites_inclusivos(string? de, string? ate, bool included)
    {
        var f = new FiltroRelatorio { De = de is null ? null : DateTime.Parse(de), Ate = ate is null ? null : DateTime.Parse(ate) };
        Assert.Equal(included ? 1 : 0, Run(f, Period(1)).Linhas.Count);
    }

    [Fact]
    public void Datas_invertidas_sao_rejeitadas() =>
        Assert.Throws<ArgumentException>(() => Run(new() { De = Hoje, Ate = Hoje.AddDays(-1) }));

    [Fact]
    public void Valores_atuais_preservam_fracoes_faltas_e_periodos_distintos()
    {
        var a = Period(1); var b = Period(2); b.Inicio = new(2024, 1, 1);
        var r = Run(new(), a, b);
        Assert.Equal(2, r.Linhas[0].Periodos[0]);
        Assert.Equal(new object[] { "Ana", "Centro", a.Inicio, a.Fim, 24.5m, 5m, 0.5m, 3 }, r.Linhas[1].Valores);
        Assert.Contains("Colaboradores: 1 | Períodos aquisitivos: 2", r.Totais);
        Assert.Contains("Saldo disponível: 49 dias", r.Totais);
        Assert.Contains("Folgas: 1 dias | Faltas não justificadas: 6", r.Totais);
        Assert.Equal(Hoje, r.GeradoEm);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Xlsx_tem_partes_validas_numeros_datas_filtros_e_texto_sem_formulas(bool empty)
    {
        var r = Run(new(), empty ? [] : [Period(1, name: "=Ana & João")]);
        using var input = new MemoryStream(RelatorioExportacao.Xlsx(r));
        using var zip = new ZipArchive(input);
        Assert.Equal(6, zip.Entries.Count);
        foreach (var entry in zip.Entries) { using var stream = entry.Open(); XDocument.Load(stream); }
        using var sheetStream = zip.GetEntry("xl/worksheets/sheet1.xml")!.Open();
        var sheet = XDocument.Load(sheetStream);
        XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        Assert.Empty(sheet.Descendants(ns + "f"));
        Assert.Equal(empty ? "A6:H6" : "A6:H7", (string?)sheet.Descendants(ns + "autoFilter").Single().Attribute("ref"));
        var cells = sheet.Descendants(ns + "c").ToDictionary(c => (string)c.Attribute("r")!);
        Assert.Equal(r.Filtros, cells["A3"].Value);
        Assert.Equal(r.Totais, cells["A4"].Value);
        for (var i = 0; i < r.Colunas.Length; i++) Assert.Equal(r.Colunas[i], cells[$"{(char)('A' + i)}6"].Value);
        if (!empty)
        {
            Assert.Equal("inlineStr", (string?)cells["A7"].Attribute("t"));
            Assert.Equal("=Ana & João", cells["A7"].Value);
            Assert.Equal("24.5", cells["E7"].Value);
            Assert.Equal("0.5", cells["G7"].Value);
            Assert.Equal("3", cells["H7"].Value);
            Assert.Equal(new DateTime(2025, 1, 1).ToOADate().ToString(System.Globalization.CultureInfo.InvariantCulture), cells["C7"].Value);
        }
    }

    [Fact]
    public void Pdf_exporta_valores_filtros_e_todos_os_registros()
    {
        var r = Run(new(), Enumerable.Range(1, 20).Select(i => Period(i, name: "João da Conceição")).ToArray());
        var pdf = Encoding.Latin1.GetString(RelatorioExportacao.Pdf(r));
        Assert.StartsWith("%PDF-1.4", pdf);
        Assert.Contains("REGISTRO 20", pdf);
        Assert.Contains("João da Conceição", pdf);
        Assert.Contains("24,5", pdf);
        var xref = int.Parse(pdf.Split("startxref\n")[1].Split('\n')[0]);
        Assert.Equal("xref", pdf.Substring(xref, 4));
        Assert.Contains("Nenhum registro encontrado", Encoding.Latin1.GetString(RelatorioExportacao.Pdf(Run(new()))));
    }
}
