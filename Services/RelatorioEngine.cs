using FeriasCampos.Properties;
using FeriasCampos.Models;

namespace FeriasCampos.Services;

public static class RelatorioEngine
{
    public static readonly string[] Titulos = [ScreenTexts.ReportsWindow_FeriasVencidasEProximasDoVencimento, ScreenTexts.ReportsWindow_SaldoDeFeriasPorColaborador,
        ScreenTexts.ReportsWindow_ProgramacaoDeFerias, ScreenTexts.ReportsWindow_PendenciasDeProgramacao, ScreenTexts.ReportsWindow_AusenciasSimultaneasPorUnidade,
        ScreenTexts.ReportsWindow_ExtratoIndividualDeFerias, ScreenTexts.ReportsWindow_DiasVendidosEFolgasRegistradas, ScreenTexts.ReportsWindow_ConferenciaDeAjustesEMovimentacoes];

    public static string Periodo(PeriodoAquisitivo p) => string.Format(ScreenTexts.ReportsWindow_PeriodoAquisitivoFormato, p.Inicio, p.Fim);
    public static string Status(PeriodoAquisitivo p, DateTime hoje)
    {

        if (p.Saldo > 0 && p.Vencimento.Date < hoje.Date) return ScreenTexts.ReportsWindow_Vencido;
        if (p.Fim.Date >= hoje.Date) return ScreenTexts.ReportsWindow_EmAquisicao;
        return p.Status == StatusPeriodo.EmAquisicao ? ScreenTexts.ReportsWindow_Disponivel : p.Status switch
        {
            StatusPeriodo.Disponivel => ScreenTexts.ReportsWindow_Disponivel, _ => p.Status.ToString()
        };
    }
    public static bool Movimentos(TipoRelatorio tipo) => tipo is TipoRelatorio.Extrato or TipoRelatorio.VendasFolgas or TipoRelatorio.Movimentacoes;
    public static bool Agenda(TipoRelatorio tipo) => tipo is TipoRelatorio.Programacao or TipoRelatorio.Sobreposicoes;
    public static string DataLabel(TipoRelatorio tipo) => Agenda(tipo) ? ScreenTexts.ReportsWindow_DatasDeFeriasQualquerSobreposicao : Movimentos(tipo) ? ScreenTexts.ReportsWindow_DataDoRegistroDaMovimentacaoLocal : ScreenTexts.ReportsWindow_DataDeVencimento;

    public static ResultadoRelatorio Gerar(IEnumerable<PeriodoAquisitivo> origem, FiltroRelatorio f, DateTime hoje)
    {
        if (f.De > f.Ate) throw new ArgumentException(ScreenTexts.ReportsWindow_ADataFinalDeveSerIgualOuPosterior);
        if (f.SaldoMinimo > f.SaldoMaximo) throw new ArgumentException(ScreenTexts.ReportsWindow_OSaldoMaximoDeveSerIgualOuMaior);
        if (f.MinimoAusentes < 2) throw new ArgumentException(ScreenTexts.ReportsWindow_InformePeloMenos2PessoasParaAsAusencias);
        bool Selected(string[] values, string value) => values.Length == 0 || values.Contains(value);
        bool Contains(string value, string search) => value.Contains(search.Trim(), StringComparison.CurrentCultureIgnoreCase);
        bool Between(DateTime data) => (!f.De.HasValue || data.Date >= f.De.Value.Date) && (!f.Ate.HasValue || data.Date <= f.Ate.Value.Date);
        var periods = origem.Where(p => Contains(p.Colaborador.Nome, f.Nome) &&
            Selected(f.Unidades, p.Colaborador.Unidade) &&
            Selected(f.Periodos, Periodo(p)) && Selected(f.Status, Status(p, hoje)) &&
            (!f.SaldoMinimo.HasValue || p.Saldo >= f.SaldoMinimo) && (!f.SaldoMaximo.HasValue || p.Saldo <= f.SaldoMaximo) &&
            (!f.ApenasSaldoPendente || p.Saldo > 0)).OrderBy(p => p.Colaborador.Nome).ThenBy(p => p.Inicio).ToList();
        var rows = new List<LinhaRelatorio>();
        string[] columns;
        string totals;
        object[] Base(PeriodoAquisitivo p) => [p.Colaborador.Nome, p.Colaborador.Unidade, Periodo(p)];
        void Add(PeriodoAquisitivo p, params object[] values) => rows.Add(new([p.ColaboradorId], [p.Id], Base(p).Concat(values).ToArray()));
        string[] baseColumns = [ScreenTexts.ReportsWindow_Colaborador, ScreenTexts.ReportsWindow_Unidade, ScreenTexts.ReportsWindow_PeriodoAquisitivo];

        if (Agenda(f.Tipo))
        {
            var agenda = periods.SelectMany(p => p.Movimentacoes.Where(m => m.Tipo == TipoMovimentacao.Agendamento && m.Dias < 0 && m.Inicio.HasValue && m.Fim.HasValue)
                .Select(m => (P: p, Inicio: m.Inicio!.Value.Date, Fim: m.Fim!.Value.Date)))
                .Where(a => a.Fim >= a.Inicio && (!f.De.HasValue || a.Fim >= f.De.Value.Date) && (!f.Ate.HasValue || a.Inicio <= f.Ate.Value.Date))
                .OrderBy(a => a.Inicio).ToList();
            if (f.Tipo == TipoRelatorio.Programacao)
            {
                columns = [.. baseColumns, ScreenTexts.ReportsWindow_Inicio, ScreenTexts.ReportsWindow_Fim, ScreenTexts.ReportsWindow_RetornoPrevisto, ScreenTexts.ReportsWindow_DiasAgendados];
                foreach (var a in agenda) Add(a.P, a.Inicio, a.Fim, a.Fim.AddDays(1), (a.Fim - a.Inicio).Days + 1);
                totals = string.Format(ScreenTexts.ReportsWindow_DiasAgendadosNasParcelasExibidasRetornoPrevistoDia, agenda.Sum(a => (a.Fim - a.Inicio).Days + 1));
            }
            else
            {
                columns = [ScreenTexts.ReportsWindow_Unidade, ScreenTexts.ReportsWindow_InicioSimultaneo, ScreenTexts.ReportsWindow_FimSimultaneo, ScreenTexts.ReportsWindow_PessoasAusentes, ScreenTexts.ReportsWindow_Colaboradores];
                foreach (var group in agenda.GroupBy(a => a.P.Colaborador.Unidade))
                {
                    var clipped = group.Select(a => (a.P, Inicio: f.De.HasValue && a.Inicio < f.De.Value.Date ? f.De.Value.Date : a.Inicio,
                        Fim: f.Ate.HasValue && a.Fim > f.Ate.Value.Date ? f.Ate.Value.Date : a.Fim)).ToList();
                    var points = clipped.SelectMany(a => new[] { a.Inicio, a.Fim.AddDays(1) }).Distinct().Order().ToList();
                    for (var i = 0; i < points.Count - 1; i++)
                    {
                        var active = clipped.Where(a => a.Inicio <= points[i] && a.Fim >= points[i]).ToList();
                        var people = active.Select(a => a.P.Colaborador).DistinctBy(c => c.Id).ToList();
                        if (people.Count < f.MinimoAusentes) continue;
                        rows.Add(new(people.Select(c => c.Id).ToArray(), active.Select(a => a.P.Id).Distinct().ToArray(),
                            [group.Key, points[i], points[i + 1].AddDays(-1), people.Count, string.Join(", ", people.Select(c => c.Nome).Order())]));
                    }
                }
                totals = string.Format(ScreenTexts.ReportsWindow_PicoDeAusenciasSimultaneasPorUnidadeCalculadoDentro, (rows.Count == 0 ? 0 : rows.Max(r => (int)r.Valores[3])));
            }
        }
        else if (Movimentos(f.Tipo))
        {
            columns = [.. baseColumns, ScreenTexts.ReportsWindow_RegistroLocal, ScreenTexts.ReportsWindow_Tipo, ScreenTexts.ReportsWindow_DiasCreditoDebito, ScreenTexts.ReportsWindow_Inicio, ScreenTexts.ReportsWindow_Fim, ScreenTexts.ReportsWindow_Motivo, ScreenTexts.ReportsWindow_Identificacao];
            var movements = periods.SelectMany(p => p.Movimentacoes.Select(m => (P: p, M: m)))
                .Where(a => Between(a.M.DataHoraUtc.ToLocalTime()) && (f.TiposMovimentacao.Length == 0 || f.TiposMovimentacao.Contains(a.M.Tipo)) &&
                    Contains(a.M.Identificacao, f.Identificacao) && Contains(a.M.Motivo, f.Motivo) &&
                    (f.Tipo != TipoRelatorio.VendasFolgas || a.M.Tipo is TipoMovimentacao.Venda or TipoMovimentacao.Folga))
                .OrderBy(a => a.P.Colaborador.Nome).ThenBy(a => a.M.DataHoraUtc).ToList();
            foreach (var a in movements) Add(a.P, a.M.DataHoraUtc.ToLocalTime().ToString("dd/MM/yyyy HH:mm"), a.M.Tipo.ToString(), a.M.Dias,
                a.M.Inicio is DateTime inicio ? inicio : "", a.M.Fim is DateTime fim ? fim : "", a.M.Motivo, a.M.Identificacao);
            totals = string.Format(ScreenTexts.ReportsWindow_CreditosDiasDebitosDiasVariacaoLiquidaDiasO, movements.Sum(a => Math.Max(0, a.M.Dias)), movements.Sum(a => Math.Max(0, -a.M.Dias)), movements.Sum(a => a.M.Dias));
        }
        else
        {
            periods = periods.Where(p => Between(p.Vencimento)).ToList();
            if (f.Tipo == TipoRelatorio.Vencimentos)
                periods = periods.Where(p => p.Saldo > 0 && (f.Prazo == -1 ? p.Vencimento.Date < hoje.Date : f.Prazo > 0 ? p.Vencimento.Date >= hoje.Date && p.Vencimento.Date <= hoje.Date.AddDays(f.Prazo) : true)).OrderBy(p => p.Vencimento).ToList();
            if (f.Tipo == TipoRelatorio.Pendencias)
                periods = periods.Where(p => p.Saldo > 0 && p.Fim.Date < hoje.Date &&
                    (f.Programacao == 1 ? p.Agendados == 0 : f.Programacao != 2 || p.Agendados > 0)).OrderBy(p => p.Vencimento).ToList();
            columns = [.. baseColumns, ScreenTexts.ReportsWindow_Vencimento, ScreenTexts.ReportsWindow_DiasAteVencer, ScreenTexts.ReportsWindow_DireitoDias, ScreenTexts.ReportsWindow_AgendadosDias, ScreenTexts.ReportsWindow_GozoRegistradoDias, ScreenTexts.ReportsWindow_VendidosDias, ScreenTexts.ReportsWindow_FolgasDias, ScreenTexts.ReportsWindow_SaldoDisponivelDias, ScreenTexts.ReportsWindow_Status];
            foreach (var p in periods) Add(p, p.Vencimento, (p.Vencimento.Date - hoje.Date).Days, p.DireitoDias, p.Agendados,
                -p.Movimentacoes.Where(m => m.Tipo == TipoMovimentacao.Gozo).Sum(m => m.Dias), p.Vendidos, p.Folgas, p.Saldo, Status(p, hoje));
            totals = string.Format(ScreenTexts.ReportsWindow_DireitoDiasAgendadosDiasGozoRegistradoDiasVendidos, periods.Sum(p => p.DireitoDias), periods.Sum(p => p.Agendados), -periods.SelectMany(p => p.Movimentacoes).Where(m => m.Tipo == TipoMovimentacao.Gozo).Sum(m => m.Dias), periods.Sum(p => p.Vendidos), periods.Sum(p => p.Folgas), periods.Sum(p => p.Saldo));
        }
        totals = string.Format(ScreenTexts.ReportsWindow_ColaboradoresPeriodosAquisitivosRegistros, rows.SelectMany(r => r.Colaboradores).Distinct().Count(), rows.SelectMany(r => r.Periodos).Distinct().Count(), rows.Count) + totals;
        return new(Titulos[(int)f.Tipo], Descrever(f), DateTime.Now, columns, rows, totals);
    }

    private static string Descrever(FiltroRelatorio f)
    {
        var parts = new List<string>();
        void Add(string label, string value) { if (!string.IsNullOrWhiteSpace(value)) parts.Add(string.Format(ScreenTexts.ReportsWindow_FiltroNomeValor, label, value)); }
        static string Options(string[] values) => string.Join(", ", values.Select(v => string.IsNullOrEmpty(v) ? ScreenTexts.ReportsWindow_NaoInformado : v));
        Add(ScreenTexts.ReportsWindow_Nome, f.Nome); Add(ScreenTexts.ReportsWindow_Unidades, Options(f.Unidades));
        Add(ScreenTexts.ReportsWindow_Periodos, Options(f.Periodos)); Add(ScreenTexts.ReportsWindow_Status, Options(f.Status));
        parts.Add(string.Format(ScreenTexts.ReportsWindow_Ate, DataLabel(f.Tipo), f.De?.ToString("dd/MM/yyyy") ?? ScreenTexts.ReportsWindow_SemInicio, f.Ate?.ToString("dd/MM/yyyy") ?? ScreenTexts.ReportsWindow_SemFim));
        if (f.SaldoMinimo.HasValue || f.SaldoMaximo.HasValue) parts.Add(string.Format(ScreenTexts.ReportsWindow_SaldoA, f.SaldoMinimo?.ToString() ?? ScreenTexts.ReportsWindow_SemMinimo, f.SaldoMaximo?.ToString() ?? ScreenTexts.ReportsWindow_SemMaximo));
        if (f.ApenasSaldoPendente) parts.Add(ScreenTexts.ReportsWindow_SomenteSaldoPositivo);
        if (f.Tipo == TipoRelatorio.Vencimentos) parts.Add(f.Prazo == -1 ? ScreenTexts.ReportsWindow_SomenteVencidos : f.Prazo == 0 ? ScreenTexts.ReportsWindow_TodosOsVencimentosComSaldo : string.Format(ScreenTexts.ReportsWindow_VencimentoNosProximosDias, f.Prazo));
        if (f.Tipo == TipoRelatorio.Pendencias) parts.Add(f.Programacao == 1 ? ScreenTexts.ReportsWindow_SemProgramacao : f.Programacao == 2 ? ScreenTexts.ReportsWindow_ProgramacaoParcial : ScreenTexts.ReportsWindow_SemProgramacaoOuParcial);
        if (f.Tipo == TipoRelatorio.Sobreposicoes) parts.Add(string.Format(ScreenTexts.ReportsWindow_MinimoDeAusentes, f.MinimoAusentes));
        if (Movimentos(f.Tipo)) { Add(ScreenTexts.ReportsWindow_Tipos, string.Join(", ", f.TiposMovimentacao)); Add(ScreenTexts.ReportsWindow_Identificacao, f.Identificacao); Add(ScreenTexts.ReportsWindow_Motivo, f.Motivo); }
        return string.Join(" | ", parts);
    }
}
