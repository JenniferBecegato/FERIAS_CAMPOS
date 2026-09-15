using FeriasCampos.Data;
using FeriasCampos.Models;
using FeriasCampos.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FeriasCampos.Tests;

public sealed class ColaboradorServiceTests
{
    [Fact]
    public async Task Atualizacao_do_banco_preserva_historico_e_permite_novos_cadastros()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        var factory = new Factory(new DbContextOptionsBuilder<FeriasDbContext>().UseSqlite(connection).Options);
        await using var db = factory.CreateDbContext();
        await db.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
        var service = new ColaboradorService(factory);
        var dados = new NovoColaboradorDto("Maria Silva", "12345678901", DateTime.Today.AddYears(-2), "Gurgel", 30);
        Assert.True((await service.CadastrarAsync(dados)).Valido);
        db.Database.ExecuteSqlRaw("""
            ALTER TABLE Colaboradores ADD COLUMN Matricula TEXT NOT NULL DEFAULT 'antiga';
            ALTER TABLE Colaboradores ADD COLUMN Ativo INTEGER NOT NULL DEFAULT 1;
            ALTER TABLE Colaboradores ADD COLUMN Cargo TEXT NOT NULL DEFAULT '';
            ALTER TABLE Colaboradores ADD COLUMN Setor TEXT NOT NULL DEFAULT '';
            CREATE UNIQUE INDEX IX_Colaboradores_Matricula ON Colaboradores(Matricula);
            """);

        ColaboradorSchemaMaintenance.Atualizar(db);
        ColaboradorSchemaMaintenance.Atualizar(db);

        var columns = db.Database.SqlQueryRaw<string>("SELECT name AS Value FROM pragma_table_info('Colaboradores')").ToList();
        Assert.Equal(new[] { "Admissao", "Cpf", "Id", "Nome", "Unidade" }, columns.Order().ToArray());
        var original = Assert.Single(await service.ListarAsync());
        var period = await db.Periodos.SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(original.Id, period.ColaboradorId);
        Assert.Equal(30, (await service.PeriodoAsync(period.Id))!.Saldo);
        var dashboard = await service.DashboardAsync();
        Assert.Equal(1, dashboard.Total);
        Assert.True((await service.CadastrarAsync(dados with { Cpf = "12345678902" })).Valido);
        Assert.Equal(2, (await service.ListarAsync()).Count);
    }

    [Fact]
    public async Task Alteracao_valida_duplicidade_e_preserva_historico()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        var factory = new Factory(new DbContextOptionsBuilder<FeriasDbContext>().UseSqlite(connection).Options);
        await using var db = factory.CreateDbContext();
        await db.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
        var service = new ColaboradorService(factory);
        var dados = new NovoColaboradorDto("Maria Silva", "12345678901", DateTime.Today.AddYears(-2), "Gurgel", 30);
        Assert.True((await service.CadastrarAsync(dados)).Valido);
        Assert.True((await service.CadastrarAsync(dados with { Cpf = "12345678902" })).Valido);
        var employee = (await service.ListarAsync()).Single(c => c.Cpf == dados.Cpf);
        Assert.False((await service.AlterarAsync(employee.Id, dados with { Cpf = "12345678902" })).Valido);
        var period = await db.Periodos.SingleAsync(p => p.ColaboradorId == employee.Id, TestContext.Current.CancellationToken);
        db.Movimentacoes.Add(new() { PeriodoAquisitivoId = period.Id, Tipo = TipoMovimentacao.Folga, Dias = -3 });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        Assert.True((await service.AlterarAsync(employee.Id, dados with { Nome = "Maria Alterada", Unidade = "Washington Luiz" })).Valido);
        Assert.False((await service.AlterarAsync(employee.Id, dados with { Admissao = dados.Admissao.AddDays(1) })).Valido);
        var saved = (await service.ListarAsync()).Single(c => c.Id == employee.Id);
        Assert.Equal("Maria Alterada", saved.Nome);
        Assert.Equal(27, (await service.PeriodoAsync(period.Id))!.Saldo);
        Assert.True((await service.ExcluirAsync(employee.Id)).Valido);
        Assert.Null(await service.PeriodoAsync(period.Id));
        Assert.Single(await service.ListarAsync());
        Assert.False(await db.Movimentacoes.AsNoTracking().AnyAsync(m => m.PeriodoAquisitivoId == period.Id, TestContext.Current.CancellationToken));
        Assert.False((await service.ExcluirAsync(employee.Id)).Valido);
        Assert.False((await service.AlterarAsync(employee.Id, dados)).Valido);
    }

    [Fact]
    public async Task Alterar_admissao_sem_uso_recalcula_periodos()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        var factory = new Factory(new DbContextOptionsBuilder<FeriasDbContext>().UseSqlite(connection).Options);
        await using var db = factory.CreateDbContext();
        await db.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
        var service = new ColaboradorService(factory);
        var dados = new NovoColaboradorDto("Maria Silva", "12345678901", DateTime.Today.AddYears(-2), "Gurgel", 15);
        await service.CadastrarAsync(dados);
        var employee = Assert.Single(await service.ListarAsync());
        var admission = DateTime.Today.AddMonths(-3);
        Assert.True((await service.AlterarAsync(employee.Id, dados with { Admissao = admission })).Valido);
        var period = await db.Periodos.Include(p => p.Movimentacoes).SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(admission, period.Inicio);
        Assert.Equal(admission.AddYears(1).AddDays(-1), period.Fim);
        Assert.Equal(15, period.Saldo);
        Assert.Single(period.Movimentacoes);
    }

    private sealed class Factory(DbContextOptions<FeriasDbContext> options) : IDbContextFactory<FeriasDbContext>
    {
        public FeriasDbContext CreateDbContext() => new(options);
    }
}
