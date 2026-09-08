using FeriasCampos.Models;
using Microsoft.EntityFrameworkCore;

namespace FeriasCampos.Data;

public sealed class FeriasDbContext(DbContextOptions<FeriasDbContext> options)
    : DbContext(options)
{
    public DbSet<Colaborador> Colaboradores => Set<Colaborador>();
    public DbSet<PeriodoAquisitivo> Periodos => Set<PeriodoAquisitivo>();
    public DbSet<MovimentacaoSaldo> Movimentacoes => Set<MovimentacaoSaldo>();
    public DbSet<Feriado> Feriados => Set<Feriado>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Colaborador>()
            .HasIndex(colaborador => colaborador.Cpf)
            .IsUnique();

        modelBuilder.Entity<Colaborador>()
            .HasIndex(colaborador => colaborador.Matricula)
            .IsUnique();

        modelBuilder.Entity<PeriodoAquisitivo>()
            .HasMany(periodo => periodo.Movimentacoes)
            .WithOne(movimento => movimento.Periodo)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public static class DbSeeder
{
    private static readonly string[] Nomes =
    [
        "Emily Lucas Leão",
        "Gabriela dos Reis Melo",
        "Carla Cristina da Silva",
        "Táciana Regina Nemer",
        "Nicolly Vieira Bertasso",
        "Bárbara Pereira Gomes",
        "Adriana Regina Jesus",
        "Ana Maria Alexandre",
        "Luciene Regina Engel",
        "Taiane Teixeira da Silva"
    ];

    private static readonly DateTime[] Inicios =
    [
        new(2025, 3, 1),
        new(2025, 3, 4),
        new(2025, 7, 22),
        new(2025, 5, 11),
        new(2025, 9, 16),
        new(2025, 3, 1),
        new(2024, 12, 8),
        new(2025, 1, 24),
        new(2025, 1, 18),
        new(2025, 8, 7)
    ];

    private static readonly int[] DiasUsados = [15, 30, 0, 25, 0, 17, 0, 15, 0, 0];
    private static readonly int[] Direitos = [30, 30, 30, 30, 30, 30, 15, 15, 15, 30];

    public static void Seed(FeriasDbContext database)
    {
        if (database.Colaboradores.Any())
        {
            return;
        }

        for (var index = 0; index < Nomes.Length; index++)
        {
            var colaborador = CreateColaborador(index);
            database.Colaboradores.Add(colaborador);
        }

        database.SaveChanges();
    }

    private static Colaborador CreateColaborador(int index)
    {
        var inicio = Inicios[index];
        var colaborador = new Colaborador
        {
            Nome = Nomes[index],
            Cpf = $"000000000{index:D2}",
            Matricula = (20 + index).ToString(),
            Admissao = inicio.AddYears(-1),
            Cargo = "Colaborador",
            Setor = "Operações",
            Unidade = "Washington Luiz"
        };

        colaborador.Periodos.Add(CreatePeriodo(index, inicio));
        return colaborador;
    }

    private static PeriodoAquisitivo CreatePeriodo(int index, DateTime inicio)
    {
        var diasUsados = DiasUsados[index];
        var direito = Direitos[index];
        var periodo = new PeriodoAquisitivo
        {
            Inicio = inicio,
            Fim = inicio.AddYears(1).AddDays(-1),
            Vencimento = inicio.AddYears(2).AddDays(-1),
            DireitoDias = direito,
            Status = GetStatus(index, diasUsados, direito)
        };

        periodo.Movimentacoes.Add(new MovimentacaoSaldo
        {
            Tipo = TipoMovimentacao.Aquisicao,
            Dias = direito,
            Motivo = "Saldo inicial do período"
        });

        if (diasUsados > 0)
        {
            periodo.Movimentacoes.Add(new MovimentacaoSaldo
            {
                Tipo = TipoMovimentacao.Agendamento,
                Dias = -diasUsados,
                Inicio = new DateTime(2026, 8, 2),
                Fim = new DateTime(2026, 8, 1).AddDays(diasUsados),
                Motivo = "Férias programadas"
            });
        }

        return periodo;
    }

    private static StatusPeriodo GetStatus(int index, int diasUsados, int direito)
    {
        if (diasUsados == direito)
        {
            return StatusPeriodo.Completo;
        }

        if (diasUsados > 0)
        {
            return StatusPeriodo.Parcial;
        }

        return index == 3 ? StatusPeriodo.Urgente : StatusPeriodo.Atencao;
    }
}
