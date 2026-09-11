using FeriasCampos.Models;

namespace FeriasCampos.Services;

public static class PeriodoAquisitivoMaintenance
{
    public static int Atualizar(Colaborador colaborador, DateTime dataReferencia)
    {
        if (colaborador.Periodos.Count == 0)
        {
            return 0;
        }

        dataReferencia = dataReferencia.Date;
        var alteracoes = 0;
        var ultimoPeriodo = colaborador.Periodos.MaxBy(periodo => periodo.Inicio)!;

        while (ultimoPeriodo.Fim.Date < dataReferencia)
        {
            var inicio = ultimoPeriodo.Fim.Date.AddDays(1);
            if (colaborador.Periodos.Any(periodo => periodo.Inicio.Date == inicio))
            {
                ultimoPeriodo = colaborador.Periodos
                    .First(periodo => periodo.Inicio.Date == inicio);
                continue;
            }

            var novoPeriodo = new PeriodoAquisitivo
            {
                Inicio = inicio,
                Fim = inicio.AddYears(1).AddDays(-1),
                Vencimento = inicio.AddYears(2).AddDays(-1),
                DireitoDias = ultimoPeriodo.DireitoDias,
                Status = inicio.AddYears(1) <= dataReferencia
                    ? StatusPeriodo.Disponivel
                    : StatusPeriodo.EmAquisicao
            };

            AdicionarAquisicao(novoPeriodo);

            colaborador.Periodos.Add(novoPeriodo);
            ultimoPeriodo = novoPeriodo;
            alteracoes++;
        }

        foreach (var periodo in colaborador.Periodos.Where(periodo =>
                     periodo.Movimentacoes.All(movimento =>
                         movimento.Tipo != TipoMovimentacao.Aquisicao)))
        {
            AdicionarAquisicao(periodo);
            if (periodo.Fim.Date < dataReferencia &&
                periodo.Status == StatusPeriodo.EmAquisicao)
            {
                periodo.Status = StatusPeriodo.Disponivel;
            }
            alteracoes++;
        }

        return alteracoes;
    }

    private static void AdicionarAquisicao(PeriodoAquisitivo periodo)
    {
        periodo.Movimentacoes.Add(new MovimentacaoSaldo
        {
            Tipo = TipoMovimentacao.Aquisicao,
            Dias = periodo.DireitoDias,
            Motivo = "Aquisição anual automática"
        });
    }
}
