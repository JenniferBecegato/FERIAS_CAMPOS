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
        modelBuilder.Entity<PeriodoAquisitivo>().HasQueryFilter(p => p.Status != StatusPeriodo.Excluido);

        modelBuilder.Entity<Colaborador>()
            .HasIndex(colaborador => colaborador.Cpf)
            .IsUnique().HasFilter("Cpf <> ''");



        modelBuilder.Entity<PeriodoAquisitivo>()
            .HasMany(periodo => periodo.Movimentacoes)
            .WithOne(movimento => movimento.Periodo)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
