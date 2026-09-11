namespace FeriasCampos.Models;

public enum TipoRelatorio { Vencimentos, Saldos, Programacao, Pendencias, Sobreposicoes, Extrato, VendasFolgas, Movimentacoes }

public sealed class FiltroRelatorio
{
    public TipoRelatorio Tipo { get; set; }
    public string Nome { get; set; } = "";
    public string[] Unidades { get; set; } = [];
    public string[] Periodos { get; set; } = [];
    public string[] Status { get; set; } = [];
    public DateTime? De { get; set; }
    public DateTime? Ate { get; set; }
    public int? SaldoMinimo { get; set; }
    public int? SaldoMaximo { get; set; }
    public bool ApenasSaldoPendente { get; set; }
    public int Prazo { get; set; } // -1: vencidos; 0: todos; positivo: próximos N dias
    public int Programacao { get; set; } // 0: todas; 1: sem programação; 2: parcial
    public int MinimoAusentes { get; set; } = 2;
    public TipoMovimentacao[] TiposMovimentacao { get; set; } = [];
    public string Identificacao { get; set; } = "";
    public string Motivo { get; set; } = "";
}

public sealed record LinhaRelatorio(int[] Colaboradores, int[] Periodos, object[] Valores);
public sealed record ResultadoRelatorio(string Titulo, string Filtros, DateTime GeradoEm,
    string[] Colunas, IReadOnlyList<LinhaRelatorio> Linhas, string Totais);
