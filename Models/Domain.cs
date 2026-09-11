using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FeriasCampos.Models;

public enum StatusPeriodo
{
    EmAquisicao = 0,
    Disponivel = 1,
    Parcial = 3,
    Completo = 4,
    Vencido = 7
}

public enum TipoMovimentacao
{
    Aquisicao,
    Agendamento,
    Gozo,
    Venda,
    Folga,
    Ajuste,
    Cancelamento,
    Remarcacao,
    Pagamento,
    Indenizacao
}

public sealed class Colaborador
{
    public int Id { get; set; }

    [MaxLength(160)]
    public string Nome { get; set; } = string.Empty;

    [MaxLength(14)]
    public string Cpf { get; set; } = string.Empty;


    public DateTime Admissao { get; set; }
    public string Unidade { get; set; } = string.Empty;
    public List<PeriodoAquisitivo> Periodos { get; set; } = [];

    [NotMapped]
    public string Iniciais => string.Join(
        string.Empty,
        Nome.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Take(2)
            .Select(parte => parte[0]))
        .ToUpperInvariant();
}

public sealed class PeriodoAquisitivo
{
    public int Id { get; set; }
    public int ColaboradorId { get; set; }
    public Colaborador Colaborador { get; set; } = null!;
    public DateTime Inicio { get; set; }
    public DateTime Fim { get; set; }
    public DateTime Vencimento { get; set; }
    public int DireitoDias { get; set; } = 30;
    public int FaltasNaoJustificadas { get; set; }
    public StatusPeriodo Status { get; set; }
    public List<MovimentacaoSaldo> Movimentacoes { get; set; } = [];

    [NotMapped]
    public int Agendados => -Movimentacoes
        .Where(movimento => movimento.Tipo == TipoMovimentacao.Agendamento)
        .Sum(movimento => movimento.Dias);

    [NotMapped]
    public int Vendidos => -Movimentacoes
        .Where(movimento => movimento.Tipo == TipoMovimentacao.Venda)
        .Sum(movimento => movimento.Dias);

    [NotMapped]
    public int Folgas => -Movimentacoes
        .Where(movimento => movimento.Tipo == TipoMovimentacao.Folga)
        .Sum(movimento => movimento.Dias);

    [NotMapped]
    public int Saldo => Movimentacoes.Sum(movimento => movimento.Dias);
}

public sealed class MovimentacaoSaldo
{
    public long Id { get; set; }
    public int PeriodoAquisitivoId { get; set; }
    public PeriodoAquisitivo Periodo { get; set; } = null!;
    public TipoMovimentacao Tipo { get; set; }
    public int Dias { get; set; }
    public DateTime DataHoraUtc { get; set; } = DateTime.UtcNow;
    public string Motivo { get; set; } = string.Empty;
    public string Identificacao { get; set; } = Environment.MachineName;
    public DateTime? Inicio { get; set; }
    public DateTime? Fim { get; set; }
}

public sealed class Feriado
{
    public int Id { get; set; }
    public string Nome { get; set; } = string.Empty;
    public int Dia { get; set; }
    public int Mes { get; set; }

    [NotMapped]
    public string NomeMes
    {
        get
        {
            var culture = System.Globalization.CultureInfo.GetCultureInfo("pt-BR");
            return Mes is >= 1 and <= 12
                ? culture.TextInfo.ToTitleCase(culture.DateTimeFormat.GetMonthName(Mes))
                : string.Empty;
        }
    }
}

public sealed record ResultadoValidacao(
    bool Valido,
    IReadOnlyList<string> Erros,
    IReadOnlyList<string> Avisos);

public sealed record IntervaloFerias(DateTime Inicio, DateTime Fim)
{
    public int Dias => (Fim.Date - Inicio.Date).Days + 1;
}

public sealed record PeriodoRow(
    int Id,
    int ColaboradorId,
    string Iniciais,
    string Colaborador,
    string Periodo,
    string Vencimento,
    int SaldoInicial,
    int Agendados,
    int Vendidos,
    int Folgas,
    int Saldo,
    string Status)
{
    public string PeriodoResumido => Periodo.Replace('\n', ' ');
}

public sealed record DashboardDto(
    int Total,
    int Programadas,
    int ProximasVencimento,
    int Pendentes,
    IReadOnlyList<PeriodoRow> Periodos);

public sealed record FeriasAgendaItem(
    int ColaboradorId,
    string Colaborador,
    DateTime Inicio,
    DateTime Fim);

public sealed record NovoColaboradorDto(
    string Nome,
    string Cpf,
    DateTime Admissao,
    string Unidade,
    int DireitoDias);
