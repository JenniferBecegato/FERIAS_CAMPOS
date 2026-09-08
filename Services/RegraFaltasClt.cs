using FeriasCampos.Models;

namespace FeriasCampos.Services;

public static class RegraFaltasClt
{
    public const string MotivoAjuste =
        "Redução do direito por faltas não justificadas (art. 130 da CLT)";

    public static int CalcularDireito(int direitoOriginal, int faltasNaoJustificadas)
    {
        var direitoClt = faltasNaoJustificadas switch
        {
            <= 5 => 30,
            <= 14 => 24,
            <= 23 => 18,
            <= 32 => 12,
            _ => 0
        };

        return Math.Min(Math.Max(0, direitoOriginal), direitoClt);
    }

    public static int ObterAjusteAtual(PeriodoAquisitivo periodo) =>
        periodo.Movimentacoes
            .Where(item => item.Tipo == TipoMovimentacao.Ajuste &&
                           item.Motivo == MotivoAjuste)
            .Sum(item => item.Dias);
}
