using FeriasCampos.Data;
using FeriasCampos.Models;
using FeriasCampos.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FeriasCampos.Tests;

public sealed class MovimentacaoServiceTests
{
    [Theory]
    [InlineData(10, 3, "Consulta", true, 7)]
    [InlineData(3, 3, "Consulta", true, 0)]
    [InlineData(3, 5, "Consulta", false, 3)]
    [InlineData(0, 1, "Consulta", false, 0)]
    [InlineData(-1, 1, "Consulta", false, -1)]
    [InlineData(10, 0, "Consulta", false, 10)]
    [InlineData(10, -1, "Consulta", false, 10)]
    [InlineData(10, int.MinValue, "Consulta", false, 10)]
    [InlineData(10, int.MaxValue, "Consulta", false, 10)]
    [InlineData(10, 1, "   ", false, 10)]
    public async Task Valida_e_persiste_desconto(int saldo, int dias, string motivo, bool valido, int esperado)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        var factory = new Factory(new DbContextOptionsBuilder<FeriasDbContext>().UseSqlite(connection).Options);
        var id = await SeedAsync(factory, saldo);
        var result = await new MovimentacaoService(factory).RegistrarFolgaAsync(id, dias, motivo);
        Assert.Equal(valido, result.Valido);
        await using var db = factory.CreateDbContext();
        var period = await db.Periodos.Include(p => p.Movimentacoes).SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(esperado, period.Saldo);
        Assert.Equal(valido ? dias : 0, period.Folgas);
        Assert.Equal(valido ? 2 : 1, period.Movimentacoes.Count);
        Assert.Equal(valido ? (esperado == 0 ? StatusPeriodo.Completo : StatusPeriodo.Parcial) : StatusPeriodo.Disponivel, period.Status);
        if (valido)
        {
            var movement = Assert.Single(period.Movimentacoes, m => m.Tipo == TipoMovimentacao.Folga);
            Assert.Equal(motivo.Trim(), movement.Motivo);
            Assert.Equal(-dias, movement.Dias);
            Assert.NotEqual(default, movement.DataHoraUtc);
            Assert.False(string.IsNullOrWhiteSpace(movement.Identificacao));
        }
    }

    [Fact]
    public async Task Reconsulta_saldo_e_preserva_outros_periodos()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        var factory = new Factory(new DbContextOptionsBuilder<FeriasDbContext>().UseSqlite(connection).Options);
        var id = await SeedAsync(factory, 10);
        int otherId;
        await using (var db = factory.CreateDbContext())
        {
            var other = new PeriodoAquisitivo { ColaboradorId = (await db.Periodos.SingleAsync(TestContext.Current.CancellationToken)).ColaboradorId };
            other.Movimentacoes.Add(new() { Tipo = TipoMovimentacao.Aquisicao, Dias = 30 });
            db.Periodos.Add(other);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
            otherId = other.Id;
        }
        var service = new MovimentacaoService(factory);
        Assert.True((await service.RegistrarFolgaAsync(id, 3, " Primeira ")).Valido);
        Assert.False((await service.RegistrarFolgaAsync(id, 8, "Excede saldo atualizado")).Valido);
        Assert.True((await service.RegistrarFolgaAsync(id, 7, "Segunda")).Valido);
        Assert.False((await service.RegistrarFolgaAsync(id, 1, "Sem saldo")).Valido);
        Assert.False((await service.RegistrarFolgaAsync(int.MaxValue, 1, "Inexistente")).Valido);
        await using var verify = factory.CreateDbContext();
        var periods = await verify.Periodos.Include(p => p.Movimentacoes).ToListAsync(TestContext.Current.CancellationToken);
        Assert.Equal(0, periods.Single(p => p.Id == id).Saldo);
        Assert.Equal(10, periods.Single(p => p.Id == id).Folgas);
        Assert.Equal(30, periods.Single(p => p.Id == otherId).Saldo);
        Assert.Equal(0, periods.Single(p => p.Id == otherId).Folgas);
        Assert.Equal(2, await verify.Movimentacoes.CountAsync(m => m.Tipo == TipoMovimentacao.Folga, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(-1, false)]
    [InlineData(0, true)]
    [InlineData(1, true)]
    public async Task Valida_vencimento_antes_de_registrar_folga(int diasAteVencimento, bool valido)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        var factory = new Factory(new DbContextOptionsBuilder<FeriasDbContext>().UseSqlite(connection).Options);
        var vencimento = DateTime.Today.AddDays(diasAteVencimento);
        var id = await SeedAsync(factory, 10, vencimento);

        var result = await new MovimentacaoService(factory).RegistrarFolgaAsync(id, 1, "Consulta");

        Assert.Equal(valido, result.Valido);
        if (!valido)
            Assert.Equal($"Este período venceu em {vencimento:dd/MM/yyyy}. Não é possível registrar folga após o vencimento.", Assert.Single(result.Erros));
        await using var db = factory.CreateDbContext();
        var period = await db.Periodos.Include(p => p.Movimentacoes).SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(valido ? 9 : 10, period.Saldo);
        Assert.Equal(valido ? 1 : 0, period.Folgas);
        Assert.Equal(valido ? 2 : 1, period.Movimentacoes.Count);
        Assert.Equal(valido ? StatusPeriodo.Parcial : StatusPeriodo.Disponivel, period.Status);
    }

    private static async Task<int> SeedAsync(Factory factory, int saldo, DateTime? vencimento = null)
    {
        await using var db = factory.CreateDbContext();
        await db.Database.EnsureCreatedAsync();
        var period = new PeriodoAquisitivo
        {
            Colaborador = new Colaborador { Nome = "Teste", Cpf = "123" },
            Vencimento = vencimento ?? DateTime.Today.AddYears(1),
            Status = StatusPeriodo.Disponivel
        };
        period.Movimentacoes.Add(new() { Tipo = TipoMovimentacao.Aquisicao, Dias = saldo });
        db.Periodos.Add(period);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return period.Id;
    }

    private sealed class Factory(DbContextOptions<FeriasDbContext> options) : IDbContextFactory<FeriasDbContext>
    {
        public FeriasDbContext CreateDbContext() => new(options);
    }
}
