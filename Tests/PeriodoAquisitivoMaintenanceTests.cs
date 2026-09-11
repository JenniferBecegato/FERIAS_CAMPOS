using FeriasCampos.Models;
using FeriasCampos.Services;
using Xunit;

namespace ControleFerias.Tests;

public sealed class PeriodoAquisitivoMaintenanceTests
{
    [Fact]
    public void Cria_periodos_anuais_ate_o_ciclo_atual_sem_duplicar()
    {
        var employee = CreateEmployee();
        var today = new DateTime(2026, 9, 3);

        var firstRun = PeriodoAquisitivoMaintenance.Atualizar(employee, today);
        var secondRun = PeriodoAquisitivoMaintenance.Atualizar(employee, today);

        Assert.Equal(2, firstRun);
        Assert.Equal(0, secondRun);
        Assert.Equal(3, employee.Periodos.Count);
        Assert.Contains(employee.Periodos, period =>
            period.Inicio == new DateTime(2025, 3, 1) &&
            period.Fim == new DateTime(2026, 2, 28) &&
            period.Saldo == 30);
        Assert.Contains(employee.Periodos, period =>
            period.Inicio == new DateTime(2026, 3, 1) &&
            period.Fim == new DateTime(2027, 2, 28) &&
            period.Status == StatusPeriodo.EmAquisicao &&
            period.Saldo == 30);
    }

    private static Colaborador CreateEmployee()
    {
        var employee = new Colaborador();
        var period = new PeriodoAquisitivo
        {
            Inicio = new DateTime(2024, 3, 1),
            Fim = new DateTime(2025, 2, 28),
            Vencimento = new DateTime(2026, 2, 28),
            DireitoDias = 30
        };
        period.Movimentacoes.Add(new MovimentacaoSaldo
        {
            Tipo = TipoMovimentacao.Aquisicao,
            Dias = 30
        });
        employee.Periodos.Add(period);
        return employee;
    }
}
