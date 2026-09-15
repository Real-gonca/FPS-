using HLProOptimizer.Core.Enums;
using HLProOptimizer.Core.Models;

namespace HLProOptimizer.Core.Abstractions;

/// <summary>Gerenciador de serviços Windows (tela Ferramentas &gt; Serviços).</summary>
public interface IServiceManager
{
    /// <summary>Lista todos os serviços instalados.</summary>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    Task<IReadOnlyList<WindowsServiceInfo>> GetServicesAsync(CancellationToken cancellationToken = default);

    /// <summary>Obtém um serviço pelo nome interno.</summary>
    /// <param name="serviceName">Nome do serviço (ex.: "DiagTrack").</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    Task<WindowsServiceInfo?> GetServiceAsync(string serviceName, CancellationToken cancellationToken = default);

    /// <summary>Inicia um serviço.</summary>
    Task<bool> StartAsync(string serviceName, CancellationToken cancellationToken = default);

    /// <summary>Para um serviço.</summary>
    Task<bool> StopAsync(string serviceName, CancellationToken cancellationToken = default);

    /// <summary>Reinicia um serviço (para + inicia).</summary>
    Task<bool> RestartAsync(string serviceName, CancellationToken cancellationToken = default);

    /// <summary>Altera o tipo de inicialização de um serviço.</summary>
    /// <param name="serviceName">Nome do serviço.</param>
    /// <param name="startupKind">Novo tipo de inicialização.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    Task<bool> SetStartupKindAsync(string serviceName, ServiceStartupKind startupKind, CancellationToken cancellationToken = default);

    /// <summary>Verifica se um serviço está em execução.</summary>
    Task<bool> IsRunningAsync(string serviceName, CancellationToken cancellationToken = default);
}
