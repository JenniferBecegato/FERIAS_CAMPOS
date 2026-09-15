using System.IO;
using System.Windows;
using FeriasCampos.Controllers;
using FeriasCampos.Data;
using FeriasCampos.Models;
using FeriasCampos.Services;
using FeriasCampos.Views;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FeriasCampos;

public partial class App : Application
{
    private ServiceProvider? _services;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DialogScreenBounds.Register();

        var dataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Campos",
            "ControleFerias");

        Directory.CreateDirectory(dataDirectory);

        var serviceCollection = new ServiceCollection();
        ConfigureServices(serviceCollection, dataDirectory);
        _services = serviceCollection.BuildServiceProvider();

        InitializeDatabase(_services);

        MainWindow = _services.GetRequiredService<MainWindow>();
        MainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _services?.Dispose();
        base.OnExit(e);
    }

    private static void ConfigureServices(
        IServiceCollection services,
        string dataDirectory)
    {
        var databasePath = Path.Combine(dataDirectory, "controle-ferias.db");

        services.AddDbContextFactory<FeriasDbContext>(options =>
            options.UseSqlite($"Data Source={databasePath}"));

        services.AddSingleton<IConfiguracaoService>(
            new ConfiguracaoService(dataDirectory));
        services.AddSingleton<IFeriadoService, FeriadoService>();
        services.AddSingleton<IRegraFeriasEngine, RegraFeriasEngine>();
        services.AddSingleton<IAgendamentoService, AgendamentoService>();
        services.AddSingleton<IColaboradorService, ColaboradorService>();
        services.AddSingleton<IPeriodoService, PeriodoService>();
        services.AddSingleton<IMovimentacaoService, MovimentacaoService>();
        services.AddSingleton<IRelatorioService, RelatorioService>();
        services.AddSingleton<IDocumentoService, DocumentoService>();
        services.AddSingleton<IImportacaoPdfService, ImportacaoPdfService>();
        services.AddTransient<DashboardController>();
        services.AddTransient<MainWindow>();
    }

    private static void InitializeDatabase(IServiceProvider services)
    {
        var factory = services.GetRequiredService<IDbContextFactory<FeriasDbContext>>();

        using var database = factory.CreateDbContext();
        database.Database.EnsureCreated();
        ColaboradorSchemaMaintenance.Atualizar(database);
        FeriadoSchemaMaintenance.Atualizar(database);
        var columns = database.Database.SqlQueryRaw<string>(
            "SELECT name AS Value FROM pragma_table_info('Periodos')").ToList();
        if (!columns.Contains("FaltasNaoJustificadas"))
        {
            database.Database.ExecuteSqlRaw(
                "ALTER TABLE Periodos ADD COLUMN FaltasNaoJustificadas INTEGER NOT NULL DEFAULT 0");
        }
        var periodosLegados = database.Periodos.Include(p => p.Movimentacoes)
            .Where(p => p.Status != StatusPeriodo.EmAquisicao && p.Status != StatusPeriodo.Disponivel &&
                        p.Status != StatusPeriodo.Parcial && p.Status != StatusPeriodo.Completo &&
                        p.Status != StatusPeriodo.Vencido).ToList();
        foreach (var periodo in periodosLegados)
        {
            periodo.Status = periodo.Saldo <= 0 ? StatusPeriodo.Completo
                : periodo.Vencimento.Date < DateTime.Today ? StatusPeriodo.Vencido
                : periodo.Fim.Date >= DateTime.Today ? StatusPeriodo.EmAquisicao
                : periodo.Saldo < periodo.DireitoDias ? StatusPeriodo.Parcial
                : StatusPeriodo.Disponivel;
        }
        if (periodosLegados.Count > 0)
            database.SaveChanges();
    }
}
