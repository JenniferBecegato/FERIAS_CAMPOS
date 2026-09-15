using FeriasCampos.Data;
using FeriasCampos.Models;
using FeriasCampos.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FeriasCampos.Tests;

public sealed class ImportacaoPdfTests
{
    [Fact]
    public void Le_nomes_quebrados_saldos_fracionados_e_prazo_prorrogado()
    {
        var leitura = LeitorPrevisaoFeriasPdf.LerLinhas([
            ("132 MARIA DA SILVA", "00:00:00 a 02/10/2027 - Período perdido por afastamento."),
            ("", "01/11/2024 a 31/10/2025 31/10/2025 02/07/2027 02/06/2027 30,00"),
            ("42 ANA REGINA", "06/12/2024 a 05/12/2025 05/12/2025 06/11/2026 07/10/2026 15,00"),
            ("SANTOS", "06/12/2025 a 05/12/2026 05/12/2026 06/11/2027 07/10/2027 17,50")]);
        Assert.Equal(2, leitura.Colaboradores.Count);
        Assert.Equal("ANA REGINA SANTOS", leitura.Colaboradores[1].Nome);
        Assert.Equal(17.5m, leitura.Colaboradores[1].Periodos[1].Saldo);
        Assert.Equal(new DateTime(2027, 7, 31), leitura.Colaboradores[0].Periodos[0].Vencimento);
        Assert.Single(leitura.Avisos);
        Assert.Throws<System.IO.InvalidDataException>(() => LeitorPrevisaoFeriasPdf.LerLinhas([
            ("1 MARIA SILVA", "01/11/2024 a 31/10/2025 saldo ilegível")]));
    }

    [Fact]
    public async Task Importacao_arredonda_fracoes_para_baixo_cria_periodos_e_nao_duplica_nem_restaura_saldo_consumido()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        var factory = new Factory(new DbContextOptionsBuilder<FeriasDbContext>().UseSqlite(connection).Options);
        await using var db = factory.CreateDbContext();
        await db.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
        // Simula o índice instalado em versões anteriores.
        db.Database.ExecuteSqlRaw("DROP INDEX IX_Colaboradores_Cpf; CREATE UNIQUE INDEX IX_Colaboradores_Cpf ON Colaboradores(Cpf)");
        ColaboradorSchemaMaintenance.Atualizar(db);
        ColaboradorSchemaMaintenance.Atualizar(db);
        var service = new ImportacaoPdfService(factory);
        var inicio = new DateTime(2025, 12, 6);
        PeriodoPdf P(DateTime data, decimal saldo) => new(data, data.AddYears(1).AddDays(-1), data.AddYears(2).AddDays(-1), saldo);
        var leitura = new LeituraPdf([
            new("ANA REGINA SANTOS", [P(inicio, 17.5m)]),
            new("MARIA SILVA", [P(inicio, 0)])], []);
        var resultado = await service.ImportarLeituraAsync(leitura, "Gurgel");
        Assert.Equal(2, resultado.ColaboradoresCriados);
        Assert.Equal(2, resultado.PeriodosCriados);
        var ana = await db.Colaboradores.Include(c => c.Periodos).ThenInclude(p => p.Movimentacoes).SingleAsync(c => c.Nome.StartsWith("ANA"), TestContext.Current.CancellationToken);
        Assert.Equal("", ana.Cpf);
        Assert.Equal(inicio, ana.Admissao);
        Assert.Equal(17m, ana.Periodos.Single().Saldo);
        ana.Cpf = "12345678901";
        ana.Admissao = inicio.AddYears(-5);
        ana.Periodos.Single().Movimentacoes.Add(new() { Tipo = TipoMovimentacao.Folga, Dias = -2 });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var repeticao = await service.ImportarLeituraAsync(new([
            new("  Ana  Régina Santos ", [P(inicio, 17.5m), P(inicio.AddYears(1), 2.5m)])], []), "Washington Luiz");
        Assert.Equal(0, repeticao.ColaboradoresCriados);
        Assert.Equal(1, repeticao.PeriodosCriados);
        Assert.Equal(1, repeticao.PeriodosExistentes);
        Assert.Single(repeticao.Avisos);
        db.ChangeTracker.Clear();
        ana = await db.Colaboradores.Include(c => c.Periodos).ThenInclude(p => p.Movimentacoes).SingleAsync(c => c.Id == ana.Id, TestContext.Current.CancellationToken);
        Assert.Equal("12345678901", ana.Cpf);
        Assert.Equal(inicio.AddYears(-5), ana.Admissao);
        Assert.Equal("Gurgel", ana.Unidade);
        Assert.Equal(15m, ana.Periodos.Single(p => p.Inicio == inicio).Saldo);
        Assert.Equal(2m, ana.Periodos.Single(p => p.Inicio == inicio.AddYears(1)).Saldo);
        PeriodoAquisitivoMaintenance.Atualizar(ana, inicio.AddMonths(1));
        Assert.Equal(17m, ana.Periodos.Sum(p => p.Saldo));
        Assert.Equal(0m, (await db.Periodos.Include(p => p.Movimentacoes).SingleAsync(p => p.Colaborador.Nome == "MARIA SILVA", TestContext.Current.CancellationToken)).Saldo);
        await Assert.ThrowsAsync<System.IO.InvalidDataException>(() => service.ImportarLeituraAsync(new([
            new("NOVO COLABORADOR", [P(inicio, 30)]),
            new("ANA REGINA SANTOS", [P(inicio.AddMonths(1), 30)])], []), "Gurgel"));
        Assert.Equal(2, await db.Colaboradores.CountAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Cadastro_e_edicao_aceitam_multiplos_cpfs_em_branco()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        var factory = new Factory(new DbContextOptionsBuilder<FeriasDbContext>().UseSqlite(connection).Options);
        await using var db = factory.CreateDbContext();
        await db.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
        var service = new ColaboradorService(factory);
        var dados = new NovoColaboradorDto("Maria Silva", "", DateTime.Today.AddYears(-1), "Gurgel", 30);
        Assert.True((await service.CadastrarAsync(dados)).Valido);
        Assert.True((await service.CadastrarAsync(dados with { Nome = "Ana Silva" })).Valido);
        var ana = (await service.ListarAsync()).First();
        Assert.True((await service.AlterarAsync(ana.Id, dados with { Nome = ana.Nome })).Valido);
        Assert.False((await service.CadastrarAsync(dados with { Cpf = "123" })).Valido);
    }

    [Fact]
    public async Task Pdf_de_referencia_importa_todos_os_colaboradores_e_periodos()
    {
        var arquivo = Environment.GetEnvironmentVariable("FERIAS_PDF_REFERENCIA");
        Assert.SkipWhen(string.IsNullOrEmpty(arquivo), "Defina FERIAS_PDF_REFERENCIA para validar o PDF externo.");
        var leitura = LeitorPrevisaoFeriasPdf.Ler(arquivo!);
        Assert.Equal(48, leitura.Colaboradores.Count);
        Assert.Equal(65, leitura.Colaboradores.Sum(c => c.Periodos.Count));
        Assert.Equal("ADRIANA REGINA JESUS SANTOS", leitura.Colaboradores[2].Nome);
        Assert.Equal("NATASHA VIDOTTO CHILE", leitura.Colaboradores[^1].Nome);
        Assert.Single(leitura.Avisos);
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        var factory = new Factory(new DbContextOptionsBuilder<FeriasDbContext>().UseSqlite(connection).Options);
        await using var db = factory.CreateDbContext();
        await db.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
        var service = new ImportacaoPdfService(factory);
        var resultado = await service.ImportarAsync(arquivo!, "Gurgel");
        Assert.Equal(48, resultado.ColaboradoresCriados);
        Assert.Equal(65, resultado.PeriodosCriados);
        var salvo = await db.Colaboradores.Include(c => c.Periodos).ThenInclude(p => p.Movimentacoes).ToListAsync(TestContext.Current.CancellationToken);
        foreach (var c in leitura.Colaboradores)
        foreach (var p in c.Periodos)
            Assert.Equal(decimal.Floor(p.Saldo), salvo.Single(s => s.Nome == c.Nome).Periodos.Single(s => s.Inicio == p.Inicio).Saldo);
        var repeticao = await service.ImportarAsync(arquivo!, "Gurgel");
        Assert.Equal(0, repeticao.PeriodosCriados);
        Assert.Equal(65, repeticao.PeriodosExistentes);
        Assert.Single(repeticao.Avisos);
    }

    private sealed class Factory(DbContextOptions<FeriasDbContext> options) : IDbContextFactory<FeriasDbContext>
    {
        public FeriasDbContext CreateDbContext() => new(options);
    }
}
