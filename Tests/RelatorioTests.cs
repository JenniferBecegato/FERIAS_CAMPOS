using System.Text;
using FeriasCampos.Models;
using FeriasCampos.Services;
using Xunit;

namespace FeriasCampos.Tests;

public sealed class RelatorioTests
{
    private static readonly DateTime Hoje = new(2026, 9, 8);
    private static PeriodoAquisitivo Period(int id, string nome = "Ana", string unidade = "Gurgel")
    {
        var c = new Colaborador { Id = id, Nome = nome, Unidade = unidade };
        return new() { Id = id, ColaboradorId = id, Colaborador = c, Inicio = new(2024, 10, 1), Fim = new(2025, 9, 30),
            Vencimento = new(2026, 9, 30), Status = StatusPeriodo.Disponivel,
            Movimentacoes = [new() { Tipo = TipoMovimentacao.Aquisicao, Dias = 30, DataHoraUtc = Hoje.ToUniversalTime() }] };
    }
    private static void Schedule(PeriodoAquisitivo p, DateTime inicio, DateTime fim) => p.Movimentacoes.Add(new()
    {
        Tipo = TipoMovimentacao.Agendamento, Inicio = inicio, Fim = fim, Dias = -((fim - inicio).Days + 1), DataHoraUtc = Hoje.ToUniversalTime()
    });
    private static ResultadoRelatorio Run(FiltroRelatorio f, params PeriodoAquisitivo[] p) => RelatorioEngine.Gerar(p, f, Hoje);

    [Fact]
    public void Busca_por_nome_inclui_todos_os_colaboradores()
    {
        var primeiro = Period(1, "Ana Maria"); var segundo = Period(2, "Ana Paula");
        Assert.Equal(2, Run(new() { Tipo = TipoRelatorio.Saldos, Nome = "ana" }, primeiro, segundo).Linhas.Count);
        Assert.Empty(Run(new() { Tipo = TipoRelatorio.Saldos, Nome = "Nome inexistente" }, primeiro).Linhas);
        Assert.Equal(2, Run(new() { Tipo = TipoRelatorio.Saldos }, primeiro, segundo).Linhas.Count);
    }
    [Fact]
    public void Combina_multiplas_unidades_status_periodo_e_saldo()
    {
        var a = Period(1); var b = Period(2, unidade: "Washington Luiz"); var c = Period(3, unidade: "Outra unidade");
        var filter = new FiltroRelatorio { Tipo = TipoRelatorio.Saldos, Unidades = ["Gurgel", "Washington Luiz"],
            Status = ["Disponível"], Periodos = [RelatorioEngine.Periodo(a)], SaldoMinimo = 30, SaldoMaximo = 30 };
        Assert.Equal(2, Run(filter, a, b, c).Linhas.Count);
        filter.SaldoMinimo = 31; filter.SaldoMaximo = 40;
        Assert.Empty(Run(filter, a, b, c).Linhas);
    }
    [Fact]
    public void Agenda_inclui_parcela_iniciada_no_mes_anterior_e_limites_inclusivos()
    {
        var a = Period(1); Schedule(a, new(2026, 8, 25), new(2026, 9, 10));
        var result = Run(new() { Tipo = TipoRelatorio.Programacao, De = new(2026, 9, 10), Ate = new(2026, 9, 30) }, a);
        var row = Assert.Single(result.Linhas);
        Assert.Equal(new DateTime(2026, 9, 11), row.Valores[Array.IndexOf(result.Colunas, "Retorno previsto")]);
        Assert.Equal(17, row.Valores[Array.IndexOf(result.Colunas, "Dias agendados")]);
        Assert.Empty(Run(new() { Tipo = TipoRelatorio.Programacao, De = new(2026, 9, 11) }, a).Linhas);
    }
    [Theory]
    [InlineData(-1, 1)] [InlineData(30, 2)] [InlineData(60, 3)] [InlineData(90, 4)]
    public void Vencimentos_respeitam_faixas_e_excluem_saldo_zero(int prazo, int expected)
    {
        var periods = new[] { -1, 0, 30, 60, 90, 91 }.Select((days, i) => { var p = Period(i + 1); p.Vencimento = Hoje.AddDays(days); return p; }).ToList();
        var zero = Period(99); zero.Movimentacoes.Clear(); zero.Vencimento = Hoje; periods.Add(zero);
        Assert.Equal(expected, Run(new() { Tipo = TipoRelatorio.Vencimentos, Prazo = prazo }, periods.ToArray()).Linhas.Count);
    }
    [Fact]
    public void Pendencias_excluem_em_aquisicao_e_saldo_zero_e_distinguem_parcial()
    {
        var a = Period(1); var b = Period(2); Schedule(b, Hoje, Hoje.AddDays(4));
        var c = Period(3); c.Fim = Hoje.AddDays(30);
        var d = Period(4); d.Movimentacoes.Clear();
        Assert.Equal(2, Run(new() { Tipo = TipoRelatorio.Pendencias }, a, b, c, d).Linhas.Count);
        Assert.Equal(1, Assert.Single(Run(new() { Tipo = TipoRelatorio.Pendencias, Programacao = 1 }, a, b).Linhas).Colaboradores[0]);
        Assert.Equal(2, Assert.Single(Run(new() { Tipo = TipoRelatorio.Pendencias, Programacao = 2 }, a, b).Linhas).Colaboradores[0]);
    }
    [Fact]
    public void Sobreposicoes_contam_pessoas_distintas_dentro_da_unidade_e_recortam_datas()
    {
        var a = Period(1); var duplicate = Period(11); duplicate.Colaborador = a.Colaborador; duplicate.ColaboradorId = a.ColaboradorId;
        var b = Period(2, "Bruno"); var other = Period(3, "Caio", unidade: "Outra unidade");
        foreach (var p in new[] { a, duplicate, b, other }) Schedule(p, Hoje.AddDays(-3), Hoje.AddDays(3));
        var result = Run(new() { Tipo = TipoRelatorio.Sobreposicoes, De = Hoje, Ate = Hoje.AddDays(1) }, a, duplicate, b, other);
        var row = Assert.Single(result.Linhas);
        Assert.Equal(2, row.Valores[Array.IndexOf(result.Colunas, "Pessoas ausentes")]);
        Assert.Equal(Hoje, row.Valores[Array.IndexOf(result.Colunas, "Início simultâneo")]);
        Assert.Equal(Hoje.AddDays(1), row.Valores[Array.IndexOf(result.Colunas, "Fim simultâneo")]); Assert.Equal(3, row.Periodos.Length);
        Assert.Empty(Run(new() { Tipo = TipoRelatorio.Sobreposicoes, MinimoAusentes = 3 }, a, duplicate, b, other).Linhas);
    }
    [Fact]
    public void Totais_separam_pessoas_periodos_agendamento_e_gozo()
    {
        var a = Period(1); var b = Period(2); b.Colaborador = a.Colaborador; b.ColaboradorId = a.ColaboradorId;
        Schedule(a, Hoje, Hoje.AddDays(4));
        a.Movimentacoes.Add(new() { Tipo = TipoMovimentacao.Gozo, Dias = -3 });
        var result = Run(new() { Tipo = TipoRelatorio.Saldos }, a, b);
        Assert.Contains("Colaboradores: 1 | Períodos aquisitivos: 2", result.Totais);
        Assert.Contains("Agendados: 5 dias | Gozo registrado: 3 dias", result.Totais);
        Assert.Contains("Saldo disponível: 52 dias", result.Totais);
    }
    [Fact]
    public void Movimentacoes_filtram_data_local_tipo_motivo_e_identificacao()
    {
        var a = Period(1);
        a.Movimentacoes.Add(new() { Tipo = TipoMovimentacao.Folga, Dias = -2, DataHoraUtc = Hoje.AddHours(12).ToUniversalTime(), Motivo = "Consulta médica", Identificacao = "PC-RH" });
        a.Movimentacoes.Add(new() { Tipo = TipoMovimentacao.Venda, Dias = -10, DataHoraUtc = Hoje.AddDays(-1).ToUniversalTime() });
        var result = Run(new() { Tipo = TipoRelatorio.VendasFolgas, De = Hoje, Ate = Hoje, TiposMovimentacao = [TipoMovimentacao.Folga], Motivo = "MÉDICA", Identificacao = "pc-rh" }, a);
        Assert.Single(result.Linhas); Assert.Contains("Débitos: 2 dias", result.Totais);
        Assert.Equal(2, Run(new() { Tipo = TipoRelatorio.VendasFolgas }, a).Linhas.Count);
    }
    [Fact]
    public void Intervalos_invalidos_sao_rejeitados()
    {
        Assert.Throws<ArgumentException>(() => Run(new() { De = Hoje, Ate = Hoje.AddDays(-1) }));
        Assert.Throws<ArgumentException>(() => Run(new() { SaldoMinimo = 10, SaldoMaximo = 5 }));
        Assert.Throws<ArgumentException>(() => Run(new() { MinimoAusentes = 1 }));
    }
    [Fact]
    public void Csv_escapa_campos_e_formulas_preserva_numeros_e_filtros()
    {
        var a = Period(1, "=HYPERLINK(\"teste\");\nAna");
        var report = Run(new() { Tipo = TipoRelatorio.Saldos, Nome = "Ana" }, a);
        var csv = RelatorioExportacao.Csv(report);
        Assert.Contains("\"'=HYPERLINK(\"\"teste\"\");\nAna\"", csv);
        Assert.Contains("Nome: Ana", csv); Assert.Contains("\"30\"", csv);
    }
    [Fact]
    public void Pdf_paginas_e_xref_validos_com_acentos_e_texto_longo()
    {
        var a = Period(1, "João da Conceição");
        for (var i = 0; i < 12; i++) a.Movimentacoes.Add(new() { Tipo = TipoMovimentacao.Ajuste, Dias = -1,
            DataHoraUtc = Hoje.ToUniversalTime(), Motivo = string.Join(" ", Enumerable.Repeat("Conferência de férias (ajuste) \\ RH", 12)) });
        var report = Run(new() { Tipo = TipoRelatorio.Extrato }, a);
        var pdf = RelatorioExportacao.Pdf(report);
        var ascii = Encoding.Latin1.GetString(pdf);
        Assert.StartsWith("%PDF-1.4", ascii); Assert.EndsWith("%%EOF\n", ascii);
        var xref = int.Parse(ascii.Split("startxref\n")[1].Split('\n')[0]);
        Assert.Equal("xref", ascii.Substring(xref, 4));
        Assert.Contains("João da Conceição", ascii);
        Assert.Contains("REGISTRO 13", ascii);
        if (Environment.GetEnvironmentVariable("FERIAS_REPORT_QA_DIR") is { Length: > 0 } directory)
        {
            System.IO.Directory.CreateDirectory(directory);
            System.IO.File.WriteAllBytes(System.IO.Path.Combine(directory, "relatorio-qa.pdf"), pdf);
        }
    }
}
