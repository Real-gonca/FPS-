using Microsoft.Extensions.DependencyInjection;
using Nexus.Application.Notifications;
using Nexus.Application.Optimization;
using Nexus.Application.Recommendations;
using Nexus.Application.Scoring;
using Nexus.Application.Tasks;
using Nexus.Application.Telemetry;
using Nexus.Application.UseCases;
using Nexus.Domain.Optimization;
using Nexus.Domain.Ports;

namespace Nexus.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddNexusApplication(this IServiceCollection services)
    {
        // Motores puros (regras de negócio).
        services.AddSingleton<ScoreEngine>();
        services.AddSingleton<RecommendationsEngine>();

        // Infraestrutura de aplicação.
        services.AddSingleton<INotificationHub, NotificationHub>();
        services.AddSingleton<TelemetryEvents>();

        // Pipeline de otimizações.
        services.AddSingleton<IOptimizationTaskCatalog, OptimizationTaskCatalog>();
        services.AddSingleton<OptimizationOrchestrator>();

        // Tarefas reais — cada patch acrescenta as suas aqui (single point).
        // Patch 1:
        services.AddSingleton<IOptimizationTask, DisableTelemetryTask>();
        // Patch 2 (serviços + privacidade):
        services.AddSingleton<IOptimizationTask, AdvertisingIdTask>();
        services.AddSingleton<IOptimizationTask>(sp => new ServiceTask(
            TaskKeys.ServiceDiagTrackDisable,
            "DiagTrack",
            "Connected User Experiences and Telemetry",
            ServiceOperation.ChangeStartMode,
            "disabled",
            "Coleta/envia dados de telemetria e diagnóstico (CEIP). A desativação é reversível e é a ação " +
            "com maior impacto na privacidade entre os serviços.",
            sp.GetRequiredService<ISystemCommandExecutor>()));
        services.AddSingleton<IOptimizationTask>(sp => new ServiceTask(
            TaskKeys.ServiceWmpNetworkDisable,
            "WMPNetworkSvc",
            "Windows Media Player Network Sharing Service",
            ServiceOperation.ChangeStartMode,
            "disabled",
            "Partilha a biblioteca do Media Player com outros dispositivos da rede. Sem utilidade se não " +
            "partilhar media; ciclos em segundo plano eliminados.",
            sp.GetRequiredService<ISystemCommandExecutor>()));

        // Casos de uso.
        services.AddSingleton<RunOptimizationUseCase>();
        services.AddSingleton<RollbackUseCase>();
        services.AddSingleton<GetDashboardSnapshotUseCase>();
        services.AddSingleton<ServiceOperationUseCase>();
        services.AddSingleton<BenchmarkUseCase>();

        // Serviços de fundo.
        services.AddHostedService<TelemetrySamplerService>();

        return services;
    }
}
