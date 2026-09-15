using HLProOptimizer.Core.Abstractions;
using Newtonsoft.Json;

namespace HLProOptimizer.Application.Elevation.Operations;

/// <summary>Grava o arquivo hosts em processo elevado (a pasta etc é protegida).</summary>
public sealed class HostsElevatedOperation : IElevatedOperation
{
    private readonly IHostsEditorService _hostsEditor;

    /// <summary>Cria a operação.</summary>
    /// <param name="hostsEditor">Editor do arquivo hosts.</param>
    public HostsElevatedOperation(IHostsEditorService hostsEditor)
    {
        _hostsEditor = hostsEditor;
    }

    /// <inheritdoc />
    public string Id => ElevatedOperationIds.Hosts;

    /// <inheritdoc />
    public string Description => "Modificar o arquivo hosts como administrador";

    /// <inheritdoc />
    public async Task<string> ExecuteAsync(string payloadJson, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        var request = JsonConvert.DeserializeObject<HostsRequest>(payloadJson ?? string.Empty) ?? new HostsRequest();

        int affected;

        if (request.Block)
        {
            progress?.Report($"Bloqueando {request.Domains.Count} domínio(s) no hosts...");
            affected = await _hostsEditor.BlockDomainsAsync(request.Domains, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            progress?.Report($"Liberando {request.Domains.Count} domínio(s) no hosts...");
            affected = await _hostsEditor.UnblockDomainsAsync(request.Domains, cancellationToken).ConfigureAwait(false);
        }

        return JsonConvert.SerializeObject(new { Affected = affected, request.Block });
    }

    /// <summary>Requisição da operação.</summary>
    public sealed class HostsRequest
    {
        /// <summary>Domínios afetados.</summary>
        public List<string> Domains { get; set; } = [];

        /// <summary>True para bloquear, false para liberar.</summary>
        public bool Block { get; set; } = true;
    }
}
