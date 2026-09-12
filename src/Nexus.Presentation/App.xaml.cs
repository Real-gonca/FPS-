using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Nexus.Application;
using Nexus.Infrastructure;
using Nexus.Infrastructure.Logging;
using Nexus.Presentation.Views;

namespace Nexus.Presentation;

/// <summary>
/// Bootstrap com Generic Host (spec §2.2): toda a aplicação é construída por
/// injeção de dependência; o MainWindow é resolvido do container.
/// </summary>
public partial class App : Application
{
    private IHost? _host;
    private ILogger? _log;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Redes de segurança: uma exceção nunca deve "apagar" a app em silêncio.
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex)
                _log?.LogCritical(ex, "Exceção não tratada (AppDomain).");
        };

        DispatcherUnhandledException += (_, args) =>
        {
            _log?.LogError(args.Exception, "Exceção não tratada (Dispatcher) — a app continua.");
            args.Handled = true;
        };

        _host = Host.CreateDefaultBuilder()
            .UseContentRoot(AppContext.BaseDirectory)
            .UseNexusLogging()
            .ConfigureServices((context, services) =>
            {
                services.AddNexusApplication();
                services.AddNexusInfrastructure(context.Configuration);
                services.AddNexusPresentation();
            })
            .Build();

        try
        {
            await _host.StartAsync();
            _log = _host.Services.GetRequiredService<ILogger<App>>();
            _log.LogInformation("Nexus Optimizer a iniciar (host iniciado).");

            var window = _host.Services.GetRequiredService<MainWindow>();
            MainWindow = window;
            window.Show();
        }
        catch (Exception ex)
        {
            _log?.LogCritical(ex, "Falha fatal no arranque.");
            MessageBox.Show(
                "Falha ao iniciar o Nexus Optimizer:\n\n" + ex.Message +
                "\n\nConsulte os logs em %LOCALAPPDATA%\\NexusOptimizer\\logs",
                "Nexus Optimizer",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (_host is not null)
        {
            try
            {
                await _host.StopAsync(TimeSpan.FromSeconds(5));
            }
            catch
            {
                // encerramento em curso — nada a fazer
            }

            _host.Dispose();
        }

        base.OnExit(e);
    }
}
