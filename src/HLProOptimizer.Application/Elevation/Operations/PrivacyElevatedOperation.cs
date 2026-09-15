using HLProOptimizer.Core.Abstractions;
using HLProOptimizer.Core.Models;
using Newtonsoft.Json;

namespace HLProOptimizer.Application.Elevation.Operations;

/// <summary>Aplica ajustes de privacidade em processo elevado (itens em HKLM).</summary>
public sealed class PrivacyElevatedOperation : IElevatedOperation
{
    private readonly IPrivacyService _privacyService;

    /// <summary>Cria a operação.</summary>
    /// <param name="privacyService">Serviço de privacidade.</param>
    public PrivacyElevatedOperation(IPrivacyService privacyService)
    {
        _privacyService = privacyService;
    }

    /// <inheritdoc />
    public string Id => ElevatedOperationIds.Privacy;

    /// <inheritdoc />
    public string Description => "Aplicar ajustes de privacidade como administrador";

    /// <inheritdoc />
    public async Task<string> ExecuteAsync(string payloadJson, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        var request = JsonConvert.DeserializeObject<PrivacyRequest>(payloadJson ?? string.Empty) ?? new PrivacyRequest();

        PrivacyApplyResult result;

        if (request.ApplyRecommendations)
        {
            progress?.Report("Aplicando recomendações de privacidade (elevado)...");
            result = await _privacyService.ApplyRecommendationsAsync(cancellationToken).ConfigureAwait(false);
        }
        else if (request.RestoreDefaults)
        {
            progress?.Report("Restaurando padrões de privacidade (elevado)...");
            result = await _privacyService.RestoreDefaultsAsync(cancellationToken).ConfigureAwait(false);
        }
        else
        {
            progress?.Report($"Aplicando {request.Items.Count} item(ns) de privacidade (elevado)...");
            result = await _privacyService.ApplyAsync(request.Items, cancellationToken).ConfigureAwait(false);
        }

        return JsonConvert.SerializeObject(result);
    }

    /// <summary>Requisição da operação.</summary>
    public sealed class PrivacyRequest
    {
        /// <summary>Aplicar apenas as recomendações.</summary>
        public bool ApplyRecommendations { get; set; }

        /// <summary>Restaurar os padrões do Windows.</summary>
        public bool RestoreDefaults { get; set; }

        /// <summary>Itens a aplicar.</summary>
        public List<PrivacyItem> Items { get; set; } = [];
    }
}
