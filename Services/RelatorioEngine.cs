using System.Globalization;
using FeriasCampos.Properties;
using FeriasCampos.Models;

namespace FeriasCampos.Services;

public static class RelatorioEngine
{
    public static ResultadoRelatorio Gerar(IEnumerable<PeriodoAquisitivo> origem, FiltroRelatorio f, DateTime geradoEm)
    {
        if (f.De?.Date > f.Ate?.Date)
            throw new ArgumentException(ScreenTexts.ReportsWindow_ADataFinalDeveSerIgualOuPosterior);
        var source = origem.ToList();
        var periods = source.Where(p =>
            (!f.ColaboradorId.HasValue || p.ColaboradorId == f.ColaboradorId) &&
            (f.Unidade is null || p.Colaborador.Unidade == f.Unidade) &&
            (!f.De.HasValue || p.Fim.Date >= f.De.Value.Date) &&
            (!f.Ate.HasValue || p.Inicio.Date <= f.Ate.Value.Date))
            .OrderBy(p => p.Colaborador.Nome).ThenBy(p => p.Inicio).ThenBy(p => p.Id).ToList();
        var rows = periods.Select(p => new LinhaRelatorio([p.ColaboradorId], [p.Id],
            [p.Colaborador.Nome, p.Colaborador.Unidade, p.Inicio.Date, p.Fim.Date,
             p.Saldo, p.Agendados, p.Folgas, p.FaltasNaoJustificadas])).ToList();
        var employee = f.ColaboradorId is int id
            ? source.Where(p => p.ColaboradorId == id).Select(p => p.Colaborador).FirstOrDefault() : null;
        var who = f.ColaboradorId is null ? ScreenTexts.ReportsWindow_TodosColaboradores
            : $"{employee?.Nome ?? "Colaborador"} (código {f.ColaboradorId})";
        var unit = f.Unidade is null ? ScreenTexts.ReportsWindow_TodasUnidades
            : string.IsNullOrEmpty(f.Unidade) ? ScreenTexts.ReportsWindow_NaoInformado : f.Unidade;
        var filters = $"Colaborador: {who} | Unidade: {unit} | Vigência do período aquisitivo: {f.De?.ToString("dd/MM/yyyy") ?? "Sem início"} a {f.Ate?.ToString("dd/MM/yyyy") ?? "Sem fim"}";
        var totals = string.Format(CultureInfo.GetCultureInfo("pt-BR"),
            "Colaboradores: {0} | Períodos aquisitivos: {1} | Saldo disponível: {2:0.##} dias | Agendados: {3:0.##} dias | Folgas: {4:0.##} dias | Faltas não justificadas: {5}",
            periods.Select(p => p.ColaboradorId).Distinct().Count(), periods.Count,
            periods.Sum(p => p.Saldo), periods.Sum(p => p.Agendados), periods.Sum(p => p.Folgas), periods.Sum(p => p.FaltasNaoJustificadas));
        return new(ScreenTexts.ReportsWindow_RelatorioUnico, filters, geradoEm,
            [ScreenTexts.ReportsWindow_Colaborador, ScreenTexts.ReportsWindow_Unidade,
             ScreenTexts.ReportsWindow_InicioAquisitivo, ScreenTexts.ReportsWindow_FimAquisitivo,
             ScreenTexts.ReportsWindow_SaldoDisponivelDias, ScreenTexts.ReportsWindow_AgendadosDias,
             ScreenTexts.ReportsWindow_FolgasDias, ScreenTexts.ReportsWindow_FaltasNaoJustificadas], rows, totals);
    }
}
