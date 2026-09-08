using FeriasCampos.Models;
using FeriasCampos.Services;
using Xunit;

namespace ControleFerias.Tests;

public sealed class RegraFeriasEngineTests
{
    [Theory]
    [InlineData(0, 30)]
    [InlineData(5, 30)]
    [InlineData(6, 24)]
    [InlineData(14, 24)]
    [InlineData(15, 18)]
    [InlineData(23, 18)]
    [InlineData(24, 12)]
    [InlineData(32, 12)]
    [InlineData(33, 0)]
    public void Calcula_direito_por_faixas_da_clt(int faltas, int esperado)
    {
        Assert.Equal(esperado, RegraFaltasClt.CalcularDireito(30, faltas));
    }

    [Fact]
    public void Limita_abono_a_um_terco_do_direito()
    {
        var period = CreatePeriodo();
        var valid = new RegraFeriasEngine().Validar(period,
            [new(new DateTime(2027, 1, 4), new DateTime(2027, 1, 23))],
            [], false, true, 10);
        var invalid = new RegraFeriasEngine().Validar(period,
            [new(new DateTime(2027, 1, 4), new DateTime(2027, 1, 22))],
            [], false, true, 11);

        Assert.True(valid.Valido);
        Assert.Contains(invalid.Erros, error => error.Contains("10 dias"));
    }

    [Fact]
    public void Considera_abono_ja_registrado_no_limite()
    {
        var period = CreatePeriodo();
        period.Movimentacoes.Add(new MovimentacaoSaldo
        {
            Tipo = TipoMovimentacao.Venda, Dias = -6
        });
        var result = new RegraFeriasEngine().Validar(period,
            [new(new DateTime(2027, 1, 4), new DateTime(2027, 1, 17))],
            [], false, true, 5);

        Assert.False(result.Valido);
        Assert.Contains(result.Erros, error => error.Contains("10 dias"));
    }

    [Fact]
    public void Aceita_tres_parcelas_14_8_8()
    {
        var period = CreatePeriodo();
        var result = new RegraFeriasEngine().Validar(period,
        [
            new(new DateTime(2027, 1, 4), new DateTime(2027, 1, 17)),
            new(new DateTime(2027, 2, 1), new DateTime(2027, 2, 8)),
            new(new DateTime(2027, 3, 1), new DateTime(2027, 3, 8))
        ], [], false, true);

        Assert.True(result.Valido);
    }

    [Fact]
    public void Aceita_parcela_parcial_quando_ainda_e_possivel_agendar_quatorze_dias()
    {
        var result = new RegraFeriasEngine().Validar(CreatePeriodo(),
            [new(new DateTime(2027, 1, 4), new DateTime(2027, 1, 8))],
            [], false, true);

        Assert.True(result.Valido);
    }

    [Fact]
    public void Rejeita_sobreposicao_e_conjunto_sem_parcela_de_quatorze_dias()
    {
        var period = CreatePeriodo();
        period.Movimentacoes.Add(new MovimentacaoSaldo
        {
            Tipo = TipoMovimentacao.Agendamento, Dias = -10,
            Inicio = new DateTime(2027, 1, 4), Fim = new DateTime(2027, 1, 13)
        });

        var overlap = new RegraFeriasEngine().Validar(period,
            [new(new DateTime(2027, 1, 10), new DateTime(2027, 1, 14))],
            [], false, true);
        var impossible = new RegraFeriasEngine().Validar(period,
            [new(new DateTime(2027, 2, 1), new DateTime(2027, 2, 10))],
            [], false, true);

        Assert.Contains(overlap.Erros, error => error.Contains("sobrepor"));
        Assert.Contains(impossible.Erros, error => error.Contains("14 dias"));
    }

    [Theory]
    [InlineData(2027, 1, 8)]
    [InlineData(2027, 1, 9)]
    public void Regra_de_repouso_bloqueia_ou_avisa(int year, int month, int day)
    {
        var start = new DateTime(year, month, day);
        var end = start.AddDays(13);
        var blocked = new RegraFeriasEngine().Validar(CreatePeriodo(),
            [new(start, end)], [], false, true);
        var warned = new RegraFeriasEngine().Validar(CreatePeriodo(),
            [new(start, end)], [], false, false);

        Assert.False(blocked.Valido);
        Assert.True(warned.Valido);
        Assert.Contains(warned.Avisos, warning => warning.Contains("repouso semanal"));
    }

    [Fact]
    public void Aceita_intervalo_valido_e_conta_extremos()
    {
        var result = new RegraFeriasEngine().Validar(
            CreatePeriodo(),
            new DateTime(2027, 1, 4),
            new DateTime(2027, 1, 17),
            []);

        Assert.True(result.Valido);
    }

    [Fact]
    public void Rejeita_fim_anterior_ao_inicio()
    {
        var result = new RegraFeriasEngine().Validar(
            CreatePeriodo(),
            new DateTime(2027, 1, 10),
            new DateTime(2027, 1, 5),
            []);

        Assert.False(result.Valido);
        Assert.Contains(result.Erros, error => error.Contains("anterior"));
    }

    [Fact]
    public void Rejeita_saldo_insuficiente()
    {
        var result = new RegraFeriasEngine().Validar(
            CreatePeriodo(10),
            new DateTime(2027, 1, 4),
            new DateTime(2027, 1, 18),
            []);

        Assert.False(result.Valido);
        Assert.Contains(result.Erros, error => error.Contains("10 dias"));
    }

    [Fact]
    public void Rejeita_parcela_menor_que_cinco_dias()
    {
        var result = new RegraFeriasEngine().Validar(
            CreatePeriodo(),
            new DateTime(2027, 1, 4),
            new DateTime(2027, 1, 7),
            []);

        Assert.False(result.Valido);
    }

    [Fact]
    public void Rejeita_inicio_dois_dias_antes_de_feriado()
    {
        var holidays = new[]
        {
            new Feriado
            {
                Data = new DateTime(2027, 1, 6),
                Descricao = "Feriado"
            }
        };

        var result = new RegraFeriasEngine().Validar(
            CreatePeriodo(),
            new DateTime(2027, 1, 4),
            new DateTime(2027, 1, 17),
            holidays);

        Assert.False(result.Valido);
        Assert.Contains(result.Erros, error => error.Contains("feriado"));
    }

    [Fact]
    public void Saldo_e_derivado_de_movimentacoes()
    {
        var period = CreatePeriodo();
        period.Movimentacoes.Add(new MovimentacaoSaldo
        {
            Tipo = TipoMovimentacao.Agendamento,
            Dias = -15
        });
        period.Movimentacoes.Add(new MovimentacaoSaldo
        {
            Tipo = TipoMovimentacao.Venda,
            Dias = -5
        });

        Assert.Equal(10, period.Saldo);
        Assert.Equal(15, period.Agendados);
        Assert.Equal(5, period.Vendidos);
    }

    [Fact]
    public void Bloqueia_antecedencia_inferior_a_30_dias_quando_configurado()
    {
        var start = DateTime.Today.AddDays(29);
        var result = new RegraFeriasEngine().Validar(
            CreatePeriodo(), start, start.AddDays(13), [], true);

        Assert.False(result.Valido);
        Assert.Contains(result.Erros, error => error.Contains("30 dias"));
    }

    [Fact]
    public void Mantem_aviso_de_antecedencia_quando_bloqueio_desmarcado()
    {
        var start = DateTime.Today.AddDays(29);
        var result = new RegraFeriasEngine().Validar(
            CreatePeriodo(), start, start.AddDays(13), [], false);

        Assert.True(result.Valido);
        Assert.Contains(result.Avisos, warning => warning.Contains("30 dias"));
    }

    [Fact]
    public void Rejeita_ferias_depois_do_vencimento_do_periodo()
    {
        var period = CreatePeriodo();
        period.Vencimento = new DateTime(2027, 1, 10);

        var result = new RegraFeriasEngine().Validar(
            period,
            new DateTime(2027, 1, 4),
            new DateTime(2027, 1, 17),
            []);

        Assert.False(result.Valido);
        Assert.Contains(result.Erros, error => error.Contains("vencimento"));
    }

    [Fact]
    public void Rejeita_movimentacao_em_periodo_ainda_em_aquisicao()
    {
        var period = CreatePeriodo();
        period.Status = StatusPeriodo.EmAquisicao;
        period.Fim = DateTime.Today.AddDays(10);

        var result = new RegraFeriasEngine().Validar(
            period,
            DateTime.Today.AddDays(5),
            DateTime.Today.AddDays(18),
            []);

        Assert.False(result.Valido);
        Assert.Contains(result.Erros, error => error.Contains("aquisição"));
    }

    [Fact]
    public void Permite_programar_periodo_em_aquisicao_para_depois_do_termino()
    {
        var period = CreatePeriodo();
        period.Status = StatusPeriodo.EmAquisicao;
        period.Fim = DateTime.Today.AddDays(10);

        var result = new RegraFeriasEngine().Validar(
            period,
            DateTime.Today.AddDays(11),
            DateTime.Today.AddDays(24),
            []);

        Assert.True(result.Valido);
    }

    private static PeriodoAquisitivo CreatePeriodo(int saldo = 30)
    {
        var period = new PeriodoAquisitivo
        {
            DireitoDias = saldo,
            Fim = new DateTime(2025, 12, 31),
            Status = StatusPeriodo.Disponivel,
            Vencimento = new DateTime(2030, 12, 31)
        };

        period.Movimentacoes.Add(new MovimentacaoSaldo
        {
            Tipo = TipoMovimentacao.Aquisicao,
            Dias = saldo
        });

        return period;
    }
}
