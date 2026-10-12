using System.Threading;
using System.Windows;
using System.Windows.Threading;
using HLOptimizerPro.App.Services;
using HLOptimizerPro.App.ViewModels;
using HLOptimizerPro.App.Views;
using HLOptimizerPro.Core.Helpers;
using HLOptimizerPro.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HLOptimizerPro.App;

/// <summary>
/// Ponto de entrada da aplicação.
///
/// Responsabilidades:
///   1. Garantir instância única (duas cópias a mexer no sistema ao mesmo tempo é pedir problemas).
///   2. Configurar o log (Serilog) e a injeção de dependências.
///   3. Criar pastas de dados, aplicar tema/idioma e revalidar a licença.
///   4. Decidir o arranque: janela de boas-vindas (login) ou diretamente o painel, conforme a conta.
///   5. Capturar QUALQUER exceção não tratada (UI, threads de fundo e tarefas) para que a aplicação
///      nunca feche abruptamente sem explicação — e registá-la no log.
/// </summary>
public partial class App : Application
{
    private const string SingleInstanceMutexName = @"Global\HLOptimizerPro-2.0-SingleInstance";

    private Mutex? _singleInstanceMutex;
    private ServiceProvider? _services;

    /// <summary>Contentor de serviços (usado por janelas que precisam de resolver dependências).</summary>
    public static IServiceProvider? Container { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // ---- 1. Instância única ------------------------------------------------
        _singleInstanceMutex = new Mutex(initiallyOwned: true, SingleInstanceMutexName, out var isNewInstance);
        if (!isNewInstance)
        {
            MessageBox.Show(
                "O HL Optimizer Pro já está em execução.\n\nProcure a janela existente na barra de tarefas.",
                AppConstants.ProductName, MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        // ---- 2. Log + injeção de dependências ----------------------------------
        var loggerFactory = LogService.CreateLoggerFactory();

        var services = new ServiceCollection();
        services.AddHLOptimizerServices(loggerFactory);
        _services = services.BuildServiceProvider();
        Container = _services;

        var logger = _services.GetRequiredService<ILogger<App>>();

        // ---- 3. Tratamento global de exceções ---------------------------------
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        try
        {
            AppPaths.EnsureCreated();

            var settings = _services.GetRequiredService<SettingsService>();
            var localization = _services.GetRequiredService<LocalizationService>();
            localization.Apply(settings.Current.Language);

            // O histórico de atividades é escrito por serviços que correm em threads de fundo
            // (limpeza automática, watchdog do Modo Jogo). Habilitar a sincronização da coleção
            // evita exceções de acesso cruzado quando a lista está ligada à interface.
            var activity = _services.GetRequiredService<ActivityLogService>();
            System.Windows.Data.BindingOperations.EnableCollectionSynchronization(activity.Entries, activity.SyncRoot);

            var license = _services.GetRequiredService<LicenseService>();
            var revalidation = license.Revalidate();
            logger.LogInformation("Licença: {Message}", revalidation.Message);

            _services.RegisterNavigationPages();

            // Aplica a preferência de animações ao gauge/transições.
            if (!settings.Current.AnimationsEnabled)
                logger.LogInformation("Animações desativadas pela preferência do utilizador.");

            // ---- 4. Arranque da interface -------------------------------------
            var account = _services.GetRequiredService<AccountService>();

            if (account.IsSignedIn && account.Current.RememberMe)
            {
                StartMainWindow(skipIntro: true);
            }
            else
            {
                var welcome = new WelcomeWindow();
                MainWindow = welcome;
                welcome.Show();
            }

            logger.LogInformation("Arranque concluído (elevado: {Elevated}).",
                _services.GetRequiredService<PrivilegeService>().IsElevated);
        }
        catch (Exception ex)
        {
            logger.LogCritical(ex, "Falha grave durante o arranque.");
            MessageBox.Show(
                $"Não foi possível iniciar o {AppConstants.ProductName}.\n\n{ex.Message}\n\n" +
                $"Detalhes técnicos no log: {AppPaths.Logs}",
                AppConstants.ProductName, MessageBoxButton.OK, MessageBoxImage.Error);

            Shutdown(1);
        }
    }

    /// <summary>
    /// Abre a janela principal. Chamado depois do login (ou diretamente, quando a conta é recordada).
    /// </summary>
    public void StartMainWindow(bool skipIntro)
    {
        if (_services is null) return;

        var mainWindow = new MainWindow
        {
            DataContext = _services.GetRequiredService<MainViewModel>(),
        };

        MainWindow = mainWindow;
        mainWindow.Show();

        // Fecha a janela de boas-vindas, se estiver aberta.
        foreach (var window in Windows.OfType<WelcomeWindow>().ToList())
            window.Close();

        if (!skipIntro)
            mainWindow.PlayIntroAnimation();
    }

    /// <summary>Encerra a aplicação de forma controlada (usado pelo botão "Sair" da Configuração).</summary>
    public void RequestShutdown()
    {
        try
        {
            SaveStateOnExit();
        }
        finally
        {
            Shutdown();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        SaveStateOnExit();
        base.OnExit(e);

        _services?.Dispose();
        LogService.Shutdown();

        try
        {
            _singleInstanceMutex?.ReleaseMutex();
            _singleInstanceMutex?.Dispose();
        }
        catch
        {
            // O mutex pode já ter sido libertado — não é um problema no encerramento.
        }
    }

    // ------------------------------------------------------------------ estado persistido

    private void SaveStateOnExit()
    {
        if (_services is null) return;

        try
        {
            var settings = _services.GetRequiredService<SettingsService>();
            settings.Save();

            var history = _services.GetRequiredService<MonitoringHistoryService>();
            var monitoring = _services.GetRequiredService<MonitoringService>();
            history.Flush(monitoring.Read());

            var gameMode = _services.GetRequiredService<GameModeService>();
            gameMode.SetEnabled(false, autoActivate: false);   // repõe prioridades/energia antes de sair

            _services.GetService<ILogger<App>>()?.LogInformation("Estado guardado no encerramento.");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Falha ao guardar o estado no encerramento: {ex.Message}");
        }
    }

    // ------------------------------------------------------------------ exceções não tratadas

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        e.Handled = true;   // mantém a aplicação viva e explica o problema

        var logger = _services?.GetService<ILogger<App>>();
        logger?.LogError(e.Exception, "Exceção não tratada na interface.");

        var notifications = _services?.GetService<Core.Services.NotificationService>();
        notifications?.Error("Erro inesperado",
            $"{e.Exception.Message} — a aplicação continuará em execução. Detalhes no log.");
    }

    private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        var logger = _services?.GetService<ILogger<App>>();
        logger?.LogCritical(e.ExceptionObject as Exception, "Exceção não tratada no domínio da aplicação.");
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        e.SetObserved();

        var logger = _services?.GetService<ILogger<App>>();
        logger?.LogError(e.Exception, "Tarefa falhou sem tratamento (exceção observada).");
    }
}
