using FeriasCampos.Data;
using FeriasCampos.Models;
using FeriasCampos.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FeriasCampos.Tests;

public sealed class FeriadoTests
{
    [Fact]
    public async Task Crud_persiste_valida_e_ordena()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        var factory = new Factory(new DbContextOptionsBuilder<FeriasDbContext>().UseSqlite(connection).Options);
        using var db = factory.CreateDbContext();
        db.Database.EnsureCreated();
        var service = new FeriadoService(factory);
        Assert.False((await service.CadastrarAsync(" ", 1, 1)).Valido);
        foreach (var (dia, mes) in new[] { (31, 4), (30, 2), (0, 1), (1, 13), (1, 0) })
            Assert.False((await service.CadastrarAsync("Inválido", dia, mes)).Valido);
        Assert.True((await service.CadastrarAsync("  São João  ", 29, 2)).Valido);
        Assert.False((await service.CadastrarAsync("SÃO JOÃO", 29, 2)).Valido);
        Assert.True((await service.CadastrarAsync("Outro", 29, 2)).Valido);
        Assert.True((await service.CadastrarAsync("Janeiro", 1, 1)).Valido);
        var rows = await service.ListarAsync();
        Assert.Equal("Janeiro", rows[0].Nome);
        var item = rows.Single(f => f.Nome == "São João");
        Assert.False((await service.AlterarAsync(item.Id, "outro", 29, 2)).Valido);
        Assert.True((await service.AlterarAsync(item.Id, " Editado ", 10, 3)).Valido);
        Assert.Equal("Editado", (await service.ListarAsync()).Single(f => f.Id == item.Id).Nome);
        Assert.True((await service.ExcluirAsync(item.Id)).Valido);
        Assert.False((await service.ExcluirAsync(item.Id)).Valido);
        Assert.False((await service.AlterarAsync(item.Id, "Ausente", 1, 1)).Valido);
        Assert.Equal(2, (await service.ListarAsync()).Count);
    }

    [Fact]
    public void Migra_legado_preserva_id_e_nao_duplica_carga()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        using var db = new FeriasDbContext(new DbContextOptionsBuilder<FeriasDbContext>().UseSqlite(connection).Options);
        db.Database.ExecuteSqlRaw("""
            CREATE TABLE Feriados(Id INTEGER PRIMARY KEY, Data TEXT NOT NULL, Descricao TEXT NOT NULL);
            INSERT INTO Feriados VALUES(42, '2025-01-20 00:00:00', ' SÃO SEBASTIÃO ');
            """);
        FeriadoSchemaMaintenance.Atualizar(db);
        Assert.Equal(12, db.Feriados.Count());
        var original = db.Feriados.AsNoTracking().Single(f => f.Id == 42);
        Assert.Equal(" SÃO SEBASTIÃO ", original.Nome);
        Assert.Equal(20, original.Dia);
        Assert.Equal(1, original.Mes);
        db.Database.ExecuteSqlRaw("UPDATE Feriados SET Nome = 'Alterado' WHERE Id = 42; DELETE FROM Feriados WHERE Dia = 25 AND Mes = 12;");
        FeriadoSchemaMaintenance.Atualizar(db);
        Assert.Equal(11, db.Feriados.Count());
        Assert.Equal("Alterado", db.Feriados.AsNoTracking().Single(f => f.Id == 42).Nome);
        db.Database.ExecuteSqlRaw("DELETE FROM Feriados");
        FeriadoSchemaMaintenance.Atualizar(db);
        Assert.Empty(db.Feriados);
    }

    [Fact]
    public void Banco_novo_carga_atomica_e_nova_tentativa_apos_falha()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        using var db = new FeriasDbContext(new DbContextOptionsBuilder<FeriasDbContext>().UseSqlite(connection).Options);
        db.Database.EnsureCreated();
        db.Database.ExecuteSqlRaw("""
            CREATE TRIGGER FalharCarga BEFORE INSERT ON Feriados WHEN NEW.Mes = 7
            BEGIN SELECT RAISE(ABORT, 'Falha de teste'); END;
            """);
        Assert.Throws<SqliteException>(() => FeriadoSchemaMaintenance.Atualizar(db));
        Assert.Empty(db.Feriados);
        db.Database.ExecuteSqlRaw("DROP TRIGGER FalharCarga");
        FeriadoSchemaMaintenance.Atualizar(db);
        FeriadoSchemaMaintenance.Atualizar(db);
        Assert.Equal(12, db.Feriados.Count());
        Assert.False(db.Feriados.Any(f => f.Dia == 1 && f.Mes == 1));
        Assert.Equal(1, db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM Inicializacoes").Single());
    }

    [Theory]
    [InlineData(2026, 12, 30, 1, 1, true)]
    [InlineData(2027, 12, 31, 1, 1, true)]
    [InlineData(2027, 12, 29, 1, 1, false)]
    [InlineData(2028, 2, 27, 29, 2, true)]
    [InlineData(2027, 2, 27, 29, 2, false)]
    public void Calendario_e_validacao_compartilham_recorrencia(int ano, int mes, int dia,
        int feriadoDia, int feriadoMes, bool esperado)
    {
        var inicio = new DateTime(ano, mes, dia);
        Feriado[] feriados = [new() { Nome = "Teste", Dia = feriadoDia, Mes = feriadoMes }];
        Assert.Equal(esperado, CalendarioFeriados.InicioProibido(inicio, feriados));
        Assert.Equal(esperado, CalendarioFeriados.IniciosProibidos(inicio, inicio, feriados).Contains(inicio));
    }

    private sealed class Factory(DbContextOptions<FeriasDbContext> options) : IDbContextFactory<FeriasDbContext>
    {
        public FeriasDbContext CreateDbContext() => new(options);
    }
}
