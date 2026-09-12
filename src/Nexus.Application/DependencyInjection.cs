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

        // Tarefas reais — Patch 1 regista a primeira; cada patch seguinte
        // acrescenta as suas tarefas aqui (single point de registo).
        services.AddSingleton<IOptimizationTask, DisableTelemetryTask>();

        // Casos de uso.
        services.AddSingleton<RunOptimizationUseCase>();
        services.AddSingleton<RollbackUseCase>();
        services.AddSingleton<GetDashboardSnapshotUseCase>();

        // Serviços de fundo.
        services.AddHostedService<TelemetrySamplerService>();

        return services;
    }
}
