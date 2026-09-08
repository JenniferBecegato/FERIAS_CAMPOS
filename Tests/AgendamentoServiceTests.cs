using FeriasCampos.Data;
using FeriasCampos.Models;
using FeriasCampos.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ControleFerias.Tests;

public sealed class AgendamentoServiceTests
{
    [Fact]
    public async Task Registra_faltas_e_reduz_saldo_quando_configurado()
    {
        await using var fixture = await Fixture.CreateAsync();
        var result = await fixture.Service.AgendarAsync(fixture.PeriodId,
            [new(new DateTime(2027, 1, 4), new DateTime(2027, 1, 17))],
            0, 6, "Teste com faltas");

        await using var database = fixture.Factory.CreateDbContext();
        var period = await database.Periodos.Include(item => item.Movimentacoes)
            .SingleAsync(item => item.Id == fixture.PeriodId,
                TestContext.Current.CancellationToken);
        Assert.True(result.Valido);
        Assert.Equal(6, period.FaltasNaoJustificadas);
        Assert.Equal(10, period.Saldo);
        Assert.Contains(period.Movimentacoes, item =>
            item.Tipo == TipoMovimentacao.Ajuste && item.Dias == -6);
    }

    [Fact]
    public async Task Registra_faltas_sem_reduzir_saldo_quando_desligado()
    {
        await using var fixture = await Fixture.CreateAsync(false);
        var result = await fixture.Service.AgendarAsync(fixture.PeriodId,
            [new(new DateTime(2027, 1, 4), new DateTime(2027, 1, 17))],
            0, 6, "Teste sem desconto");

        await using var database = fixture.Factory.CreateDbContext();
        var period = await database.Periodos.Include(item => item.Movimentacoes)
            .SingleAsync(item => item.Id == fixture.PeriodId,
                TestContext.Current.CancellationToken);
        Assert.True(result.Valido);
        Assert.Equal(6, period.FaltasNaoJustificadas);
        Assert.Equal(16, period.Saldo);
        Assert.DoesNotContain(period.Movimentacoes, item =>
            item.Tipo == TipoMovimentacao.Ajuste);
    }

    [Fact]
    public async Task Salva_abono_e_descanso_na_mesma_operacao()
    {
        await using var fixture = await Fixture.CreateAsync();
        var result = await fixture.Service.AgendarAsync(fixture.PeriodId,
            [new(new DateTime(2027, 1, 4), new DateTime(2027, 1, 23))],
            10, 0, "Teste com abono");

        await using var database = fixture.Factory.CreateDbContext();
        var period = await database.Periodos.Include(item => item.Movimentacoes)
            .SingleAsync(item => item.Id == fixture.PeriodId,
                TestContext.Current.CancellationToken);
        Assert.True(result.Valido);
        Assert.Equal(10, period.Vendidos);
        Assert.Equal(20, period.Agendados);
        Assert.Equal(0, period.Saldo);
        Assert.Equal(StatusPeriodo.Completo, period.Status);
    }

    [Fact]
    public async Task Salva_lote_valido_como_movimentacoes_independentes()
    {
        await using var fixture = await Fixture.CreateAsync();
        var result = await fixture.Service.AgendarAsync(fixture.PeriodId,
        [
            new(new DateTime(2027, 1, 4), new DateTime(2027, 1, 17)),
            new(new DateTime(2027, 2, 1), new DateTime(2027, 2, 8)),
            new(new DateTime(2027, 3, 1), new DateTime(2027, 3, 8))
        ], 0, 0, "Teste em lote");

        await using var database = fixture.Factory.CreateDbContext();
        var period = await database.Periodos.Include(item => item.Movimentacoes)
            .SingleAsync(item => item.Id == fixture.PeriodId,
                TestContext.Current.CancellationToken);
        Assert.True(result.Valido);
        Assert.Equal(3, period.Movimentacoes.Count(item =>
            item.Tipo == TipoMovimentacao.Agendamento));
        Assert.Equal(0, period.Saldo);
        Assert.Equal(StatusPeriodo.Completo, period.Status);
    }

    [Fact]
    public async Task Lote_invalido_nao_persiste_nenhuma_parcela()
    {
        await using var fixture = await Fixture.CreateAsync();
        var result = await fixture.Service.AgendarAsync(fixture.PeriodId,
        [
            new(new DateTime(2027, 1, 4), new DateTime(2027, 1, 17)),
            new(new DateTime(2027, 1, 10), new DateTime(2027, 1, 18))
        ], 0, 0, "Teste inválido");

        await using var database = fixture.Factory.CreateDbContext();
        Assert.False(result.Valido);
        Assert.Empty(await database.Movimentacoes
            .Where(item => item.Tipo == TipoMovimentacao.Agendamento)
            .ToListAsync(TestContext.Current.CancellationToken));
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        private Fixture(SqliteConnection connection, TestDbContextFactory factory,
            AgendamentoService service, int periodId)
        {
            _connection = connection;
            Factory = factory;
            Service = service;
            PeriodId = periodId;
        }

        public TestDbContextFactory Factory { get; }
        public AgendamentoService Service { get; }
        public int PeriodId { get; }

        public static async Task<Fixture> CreateAsync(bool descontarPorFaltas = true)
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<FeriasDbContext>()
                .UseSqlite(connection).Options;
            var factory = new TestDbContextFactory(options);
            await using var database = factory.CreateDbContext();
            await database.Database.EnsureCreatedAsync();
            var employee = new Colaborador
            {
                Nome = "Teste da Silva", Cpf = "12345678901", Matricula = "T-1",
                Admissao = new DateTime(2024, 1, 1)
            };
            var period = new PeriodoAquisitivo
            {
                Inicio = new DateTime(2025, 1, 1), Fim = new DateTime(2025, 12, 31),
                Vencimento = new DateTime(2027, 12, 31), DireitoDias = 30,
                Status = StatusPeriodo.Disponivel
            };
            period.Movimentacoes.Add(new MovimentacaoSaldo
            {
                Tipo = TipoMovimentacao.Aquisicao, Dias = 30, Motivo = "Teste"
            });
            employee.Periodos.Add(period);
            database.Colaboradores.Add(employee);
            await database.SaveChangesAsync();

            var service = new AgendamentoService(factory, new RegraFeriasEngine(),
                new TestConfiguration
                {
                    DescontarSaldoFeriasPorFaltasNaoJustificadas = descontarPorFaltas
                });
            return new Fixture(connection, factory, service, period.Id);
        }

        public async ValueTask DisposeAsync() => await _connection.DisposeAsync();
    }

    private sealed class TestDbContextFactory(DbContextOptions<FeriasDbContext> options)
        : IDbContextFactory<FeriasDbContext>
    {
        public FeriasDbContext CreateDbContext() => new(options);
    }

    private sealed class TestConfiguration : IConfiguracaoService
    {
        public bool BloquearAgendamentoMenos30Dias { get; set; }
        public bool BloquearInicioAntesRepousoSemanal { get; set; } = true;
        public bool DescontarSaldoFeriasPorFaltasNaoJustificadas { get; set; } = true;
        public void Salvar() { }
    }
}
