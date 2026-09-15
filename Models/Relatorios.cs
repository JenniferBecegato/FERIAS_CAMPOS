namespace FeriasCampos.Models;

public sealed class FiltroRelatorio
{
    public int? ColaboradorId { get; set; }
    // null inclui todas; string vazia seleciona unidade não informada.
    public string? Unidade { get; set; }
    public DateTime? De { get; set; }
    public DateTime? Ate { get; set; }
}

public sealed record LinhaRelatorio(int[] Colaboradores, int[] Periodos, object[] Valores);
public sealed record ResultadoRelatorio(string Titulo, string Filtros, DateTime GeradoEm,
    string[] Colunas, IReadOnlyList<LinhaRelatorio> Linhas, string Totais);
