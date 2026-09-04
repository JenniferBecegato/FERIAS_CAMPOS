using System.IO;
using System.Windows;
using FeriasCampos.Controllers;
using FeriasCampos.Data;
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
        services.AddSingleton<IRegraFeriasEngine, RegraFeriasEngine>();
        services.AddSingleton<IAgendamentoService, AgendamentoService>();
        services.AddSingleton<IColaboradorService, ColaboradorService>();
        services.AddSingleton<IPeriodoService, PeriodoService>();
        services.AddSingleton<IMovimentacaoService, MovimentacaoService>();
        services.AddSingleton<IRelatorioService, RelatorioService>();
        services.AddSingleton<IDocumentoService, DocumentoService>();
        services.AddSingleton<IImportacaoPdfService, ImportacaoPdfBloqueadaService>();
        services.AddTransient<DashboardController>();
        services.AddTransient<MainWindow>();
    }

    private static void InitializeDatabase(IServiceProvider services)
    {
        var factory = services.GetRequiredService<IDbContextFactory<FeriasDbContext>>();

        using var database = factory.CreateDbContext();
        database.Database.EnsureCreated();
        DbSeeder.Seed(database);
    }
}
