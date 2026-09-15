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
    public async Task Nome_encontrado_reutiliza_cadastro_manual_mesmo_com_unidade_e_admissao_diferentes()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        var factory = new Factory(new DbContextOptionsBuilder<FeriasDbContext>().UseSqlite(connection).Options);
        await using var db = factory.CreateDbContext();
        await db.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
        var pessoa = new Colaborador { Nome = "Emily Lucas Leão", Cpf = "12345678901",
            Unidade = "Washington Luiz", Admissao = new DateTime(2024, 3, 1) };
        db.Colaboradores.Add(pessoa);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var periodo = new PeriodoPdf(new(2026, 3, 1), new(2027, 2, 28), new(2028, 2, 29), 15);
        var resultado = await new ImportacaoPdfService(factory).ImportarLeituraAsync(new([
            new(" EMILY  LUCAS LEAO ", [periodo]),
            new("Emily Lucas Leão", [periodo])], []), "Gurgel");
        Assert.Equal(0, resultado.ColaboradoresCriados);
        Assert.Equal(1, resultado.PeriodosCriados);
        Assert.Equal(1, resultado.PeriodosExistentes);
        db.ChangeTracker.Clear();
        var salvo = Assert.Single(await db.Colaboradores.Include(c => c.Periodos).ToListAsync(TestContext.Current.CancellationToken));
        Assert.Equal(pessoa.Id, salvo.Id);
        Assert.Equal(pessoa.Nome, salvo.Nome);
        Assert.Equal(pessoa.Cpf, salvo.Cpf);
        Assert.Equal(pessoa.Admissao, salvo.Admissao);
        Assert.Equal(pessoa.Unidade, salvo.Unidade);
        Assert.Single(salvo.Periodos);
    }

    [Theory]
    [InlineData("Gabriela dos Reis Melo", "GABRIELA DOS REIS MELO SANTOS")]
    [InlineData("Adriana Regina Jesus", "ADRIANA REGINA JESUS SANTOS")]
    [InlineData("Taiane Teixeira da Silva", "TAINA TEIXEIRA DA SILVA")]
    [InlineData("FABIANA CAROLINE ZANELATTO", "FABIANA CAROLINE ZANELATO")]
    public async Task Nome_semelhante_gera_aviso_e_importa_demais_sem_alterar_lancamentos(string nome, string nomePdf)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        var factory = new Factory(new DbContextOptionsBuilder<FeriasDbContext>().UseSqlite(connection).Options);
        await using var db = factory.CreateDbContext();
        await db.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
        var inicio = new DateTime(2025, 3, 4);
        db.Colaboradores.Add(new Colaborador { Nome = nome, Admissao = inicio, Periodos = [
            new() { Inicio = inicio, Fim = inicio.AddYears(1).AddDays(-1), Movimentacoes = [
                new() { Tipo = TipoMovimentacao.Aquisicao, Dias = 30 },
                new() { Tipo = TipoMovimentacao.Folga, Dias = -5, Motivo = "Preservar" }] }] });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var p = new PeriodoPdf(inicio, inicio.AddYears(1).AddDays(-1), inicio.AddYears(2).AddDays(-1), 30);
        var resultado = await
            new ImportacaoPdfService(factory).ImportarLeituraAsync(new([
                new("NOVO COLABORADOR", [p]), new(nomePdf, [p]), new("OUTRA PESSOA", [p])], []), "Gurgel");
        Assert.Contains("Possível cadastro duplicado", Assert.Single(resultado.Avisos));
        Assert.Contains(nomePdf, resultado.Avisos[0]);
        Assert.Equal(2, resultado.ColaboradoresCriados);
        db.ChangeTracker.Clear();
        Assert.Equal(3, await db.Colaboradores.CountAsync(TestContext.Current.CancellationToken));
        var salvo = await db.Periodos.Include(p => p.Movimentacoes).SingleAsync(p => p.Colaborador.Nome == nome, TestContext.Current.CancellationToken);
        Assert.Equal(25, salvo.Saldo);
        Assert.Equal(2, salvo.Movimentacoes.Count);
    }

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
        var invalida = LeitorPrevisaoFeriasPdf.LerLinhas([
            ("1 MARIA SILVA", "01/11/2024 a 31/10/2025 saldo ilegível")]);
        Assert.Empty(invalida.Colaboradores);
        Assert.Contains("MARIA SILVA", Assert.Single(invalida.Avisos));
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
        var parcial = await service.ImportarLeituraAsync(new([
            new("NOVO COLABORADOR", [P(inicio, 30)]),
            new("ANA REGINA SANTOS", [P(inicio.AddYears(2), 30), P(inicio.AddMonths(1), 30)])], []), "Gurgel");
        Assert.Contains("ANA REGINA SANTOS", Assert.Single(parcial.Avisos));
        Assert.Equal(1, parcial.PeriodosCriados);
        Assert.Equal(3, await db.Colaboradores.CountAsync(TestContext.Current.CancellationToken));
        Assert.Equal(2, await db.Periodos.CountAsync(p => p.Colaborador.Nome == "ANA REGINA SANTOS", TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("31/02/2024 a 31/10/2025 31/10/2025 02/07/2027 02/06/2027 30,00")]
    [InlineData("01/11/2024 a 31/10/2025 31/10/2025 02/07/2027 02/06/2027 31,00")]
    [InlineData("período ilegível")]
    public void Leitura_ignora_pessoa_inteira_com_erro_e_continua(string linhaInvalida)
    {
        const string valida = "01/11/2024 a 31/10/2025 31/10/2025 02/07/2027 02/06/2027 30,00";
        var leitura = LeitorPrevisaoFeriasPdf.LerLinhas([
            ("1 ANA SILVA", valida), ("", linhaInvalida),
            ("2 MARIA SANTOS", valida), ("3 ANA SILVA", valida)]);
        Assert.Equal("MARIA SANTOS", Assert.Single(leitura.Colaboradores).Nome);
        Assert.Contains("ANA SILVA", Assert.Single(leitura.Avisos));
    }

    [Fact]
    public async Task Ignora_duplicidade_e_sobreposicao_interna_sem_criar_cadastro_parcial()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        var factory = new Factory(new DbContextOptionsBuilder<FeriasDbContext>().UseSqlite(connection).Options);
        await using var db = factory.CreateDbContext();
        await db.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
        db.Colaboradores.AddRange(new Colaborador { Nome = "ANA SILVA" }, new Colaborador { Nome = "ANA SILVA" });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var p = new PeriodoPdf(new(2025, 1, 1), new(2025, 12, 31), new(2026, 12, 31), 30);
        var resultado = await new ImportacaoPdfService(factory).ImportarLeituraAsync(new([
            new("ANA SILVA", [p]),
            new("MARIA SANTOS", [p, p with { Inicio = p.Inicio.AddMonths(1) }]),
            new("JOAO SOUZA", [p with { Saldo = -1 }]),
            new("CARLOS LIMA", [p])], []), "Gurgel");
        Assert.Equal(3, resultado.Avisos.Count);
        Assert.Equal(1, resultado.ColaboradoresCriados);
        Assert.Equal(1, resultado.PeriodosCriados);
        Assert.Equal(0, resultado.PeriodosExistentes);
        db.ChangeTracker.Clear();
        Assert.Equal(3, await db.Colaboradores.CountAsync(TestContext.Current.CancellationToken));
        Assert.Equal("CARLOS LIMA", (await db.Periodos.Include(p => p.Colaborador).SingleAsync(TestContext.Current.CancellationToken)).Colaborador.Nome);
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
