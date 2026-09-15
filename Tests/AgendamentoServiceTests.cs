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

    [Theory]
    [InlineData(1, true)]
    [InlineData(0, false)]
    [InlineData(-5, false)]
    [InlineData(-29, false)]
    [InlineData(-30, true)]
    [InlineData(-60, true)]
    public async Task Exclui_parcela_futura_ou_concluida_e_restaura_saldo(int inicio, bool permitido)
    {
        await using var fixture = await Fixture.CreateAsync();
        long id;
        await using (var database = fixture.Factory.CreateDbContext())
        {
            var period = await database.Periodos.Include(x => x.Movimentacoes).SingleAsync(TestContext.Current.CancellationToken);
            var item = new MovimentacaoSaldo
            {
                Tipo = TipoMovimentacao.Agendamento, Dias = -30,
                Inicio = DateTime.Today.AddDays(inicio), Fim = DateTime.Today.AddDays(inicio + 29)
            };
            period.Movimentacoes.Add(item);
            period.Status = StatusPeriodo.Completo;
            await database.SaveChangesAsync(TestContext.Current.CancellationToken);
            id = item.Id;
        }
        var result = await fixture.Service.AgendarAsync(fixture.PeriodId, [], 0, 0, "Teste", [id]);
        await using var check = fixture.Factory.CreateDbContext();
        var saved = await check.Periodos.Include(x => x.Movimentacoes).SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(permitido, result.Valido);
        Assert.Equal(permitido ? 30 : 0, saved.Saldo);
        Assert.Equal(permitido ? StatusPeriodo.Disponivel : StatusPeriodo.Completo, saved.Status);
        Assert.Equal(!permitido, saved.Movimentacoes.Any(x => x.Id == id));
    }

    [Fact]
    public async Task Exclusao_com_novo_agendamento_invalido_nao_altera_dados()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.Service.AgendarAsync(fixture.PeriodId,
            [new(new DateTime(2027, 1, 4), new DateTime(2027, 1, 23))], 10, 0, "Teste");
        await using var database = fixture.Factory.CreateDbContext();
        var id = await database.Movimentacoes.Where(x => x.Tipo == TipoMovimentacao.Agendamento)
            .Select(x => x.Id).SingleAsync(TestContext.Current.CancellationToken);
        var result = await fixture.Service.AgendarAsync(fixture.PeriodId,
            [new(new DateTime(2027, 2, 1), new DateTime(2027, 2, 28))], 0, 0, "Teste", [id]);
        Assert.False(result.Valido);
        var saved = await database.Periodos.Include(x => x.Movimentacoes).SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(0, saved.Saldo);
        Assert.Equal(10, saved.Vendidos);
        Assert.Contains(saved.Movimentacoes, x => x.Id == id);
        var deleted = await fixture.Service.AgendarAsync(fixture.PeriodId, [], 0, 0, "Teste", [id]);
        Assert.True(deleted.Valido);
        await using var check = fixture.Factory.CreateDbContext();
        var remaining = await check.Periodos.Include(x => x.Movimentacoes).SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(20, remaining.Saldo);
        Assert.Equal(10, remaining.Vendidos);
        Assert.Equal(StatusPeriodo.Parcial, remaining.Status);
        Assert.False((await fixture.Service.AgendarAsync(fixture.PeriodId, [], 0, 0, "Teste", [id])).Valido);
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
                Nome = "Teste da Silva", Cpf = "12345678901",
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
