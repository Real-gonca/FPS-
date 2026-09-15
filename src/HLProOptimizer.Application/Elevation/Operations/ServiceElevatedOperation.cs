using HLProOptimizer.Core.Abstractions;
using HLProOptimizer.Core.Enums;
using Newtonsoft.Json;

namespace HLProOptimizer.Application.Elevation.Operations;

/// <summary>Inicia/para/reconfigura um serviço Windows em processo elevado.</summary>
public sealed class ServiceElevatedOperation : IElevatedOperation
{
    private readonly IServiceManager _services;

    /// <summary>Cria a operação.</summary>
    /// <param name="services">Gerenciador de serviços.</param>
    public ServiceElevatedOperation(IServiceManager services)
    {
        _services = services;
    }

    /// <inheritdoc />
    public string Id => ElevatedOperationIds.Service;

    /// <inheritdoc />
    public string Description => "Gerenciar serviços do Windows como administrador";

    /// <inheritdoc />
    public async Task<string> ExecuteAsync(string payloadJson, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        var request = JsonConvert.DeserializeObject<ServiceRequest>(payloadJson ?? string.Empty) ?? new ServiceRequest();

        progress?.Report($"{request.Action} no serviço '{request.ServiceName}'...");

        var success = request.Action switch
        {
            "start" => await _services.StartAsync(request.ServiceName, cancellationToken).ConfigureAwait(false),
            "stop" => await _services.StopAsync(request.ServiceName, cancellationToken).ConfigureAwait(false),
            "restart" => await _services.RestartAsync(request.ServiceName, cancellationToken).ConfigureAwait(false),
            "setStartup" => await _services.SetStartupKindAsync(request.ServiceName, request.StartupKind, cancellationToken).ConfigureAwait(false),
            _ => false
        };

        return JsonConvert.SerializeObject(new { request.ServiceName, request.Action, Success = success });
    }

    /// <summary>Requisição da operação.</summary>
    public sealed class ServiceRequest
    {
        /// <summary>Nome do serviço.</summary>
        public string ServiceName { get; set; } = string.Empty;

        /// <summary>Ação: start | stop | restart | setStartup.</summary>
        public string Action { get; set; } = "restart";

        /// <summary>Novo tipo de inicialização (para a ação setStartup).</summary>
        public ServiceStartupKind StartupKind { get; set; } = ServiceStartupKind.Manual;
    }
}
