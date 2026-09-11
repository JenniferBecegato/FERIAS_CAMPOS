using FeriasCampos.Data;
using FeriasCampos.Models;
using FeriasCampos.Properties;
using Microsoft.EntityFrameworkCore;

namespace FeriasCampos.Services;

public interface IFeriadoService
{
    Task<IReadOnlyList<Feriado>> ListarAsync();
    Task<ResultadoValidacao> CadastrarAsync(string nome, int dia, int mes);
    Task<ResultadoValidacao> AlterarAsync(int id, string nome, int dia, int mes);
    Task<ResultadoValidacao> ExcluirAsync(int id);
}

public sealed class FeriadoService(IDbContextFactory<FeriasDbContext> factory) : IFeriadoService
{
    public async Task<IReadOnlyList<Feriado>> ListarAsync()
    {
        await using var db = await factory.CreateDbContextAsync();
        return await db.Feriados.AsNoTracking().OrderBy(f => f.Mes).ThenBy(f => f.Dia)
            .ThenBy(f => f.Nome).ToListAsync();
    }

    public Task<ResultadoValidacao> CadastrarAsync(string nome, int dia, int mes) => SalvarAsync(null, nome, dia, mes);
    public Task<ResultadoValidacao> AlterarAsync(int id, string nome, int dia, int mes) => SalvarAsync(id, nome, dia, mes);

    private async Task<ResultadoValidacao> SalvarAsync(int? id, string nome, int dia, int mes)
    {
        nome = (nome ?? string.Empty).Trim();
        if (nome.Length == 0) return Erro(ScreenTexts.Holidays_NomeObrigatorio);
        if (mes < 1 || mes > 12 || dia < 1 || dia > DateTime.DaysInMonth(2000, mes))
            return Erro(ScreenTexts.Holidays_DataInvalida);
        await using var db = await factory.CreateDbContextAsync();
        using var transaction = await db.Database.BeginTransactionAsync();
        var sameDate = await db.Feriados.Where(f => f.Dia == dia && f.Mes == mes).ToListAsync();
        if (sameDate.Any(f => f.Id != id && string.Equals(f.Nome.Trim(), nome, StringComparison.OrdinalIgnoreCase)))
            return Erro(ScreenTexts.Holidays_Duplicado);
        var item = id.HasValue ? await db.Feriados.FindAsync(id.Value) : new Feriado();
        if (item is null) return Erro(ScreenTexts.Holidays_NaoEncontrado);
        item.Nome = nome;
        item.Dia = dia;
        item.Mes = mes;
        if (!id.HasValue) db.Feriados.Add(item);
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        return new(true, [], []);
    }

    public async Task<ResultadoValidacao> ExcluirAsync(int id)
    {
        await using var db = await factory.CreateDbContextAsync();
        var item = await db.Feriados.FindAsync(id);
        if (item is null) return Erro(ScreenTexts.Holidays_NaoEncontrado);
        db.Feriados.Remove(item);
        await db.SaveChangesAsync();
        return new(true, [], []);
    }

    private static ResultadoValidacao Erro(string message) => new(false, [message], []);
}
