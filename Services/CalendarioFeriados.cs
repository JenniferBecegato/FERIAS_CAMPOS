using FeriasCampos.Models;

namespace FeriasCampos.Services;

public static class CalendarioFeriados
{
    public static bool InicioProibido(DateTime inicio, IEnumerable<Feriado> feriados) =>
        feriados.Any(f => Enumerable.Range(1, 2).Any(offset =>
            inicio.Date <= DateTime.MaxValue.Date.AddDays(-offset) &&
            inicio.AddDays(offset).Month == f.Mes && inicio.AddDays(offset).Day == f.Dia));

    public static HashSet<DateTime> IniciosProibidos(DateTime inicio, DateTime fim,
        IEnumerable<Feriado> feriados)
    {
        var lista = feriados.ToList();
        var result = new HashSet<DateTime>();
        for (var dia = inicio.Date; dia <= fim.Date;)
        {
            if (InicioProibido(dia, lista)) result.Add(dia);
            if (dia == DateTime.MaxValue.Date) break;
            dia = dia.AddDays(1);
        }
        return result;
    }
}
