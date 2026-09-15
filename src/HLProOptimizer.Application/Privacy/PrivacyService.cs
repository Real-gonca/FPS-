using HLProOptimizer.Core.Abstractions;
using HLProOptimizer.Core.Enums;
using HLProOptimizer.Core.Models;
using Microsoft.Extensions.Logging;

namespace HLProOptimizer.Application.Privacy;

/// <summary>
/// Implementação de <see cref="IPrivacyService"/> baseada no
/// <see cref="PrivacyCatalog"/>: lê o estado atual de cada item no registro/serviços,
/// aplica proteções e sabe restaurar exatamente os valores padrão do Windows.
/// </summary>
public sealed class PrivacyService : IPrivacyService
{
    private readonly IRegistryService _registry;
    private readonly IServiceManager _services;
    private readonly ISystemInformationService _systemInformation;
    private readonly IActionHistoryService _history;
    private readonly ILogger<PrivacyService> _logger;

    /// <summary>Cria o serviço de privacidade.</summary>
    public PrivacyService(
        IRegistryService registry,
        IServiceManager services,
        ISystemInformationService systemInformation,
        IActionHistoryService history,
        ILogger<PrivacyService> logger)
    {
        _registry = registry;
        _services = services;
        _systemInformation = systemInformation;
        _history = history;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PrivacyItem>> GetItemsAsync(CancellationToken cancellationToken = default)
    {
        var items = new List<PrivacyItem>();
        var services = await LoadServicesAsync(cancellationToken).ConfigureAwait(false);

        foreach (var definition in PrivacyCatalog.All)
        {
            var isProtected = IsProtected(definition, services);

            items.Add(new PrivacyItem
            {
                Id = definition.Id,
                DisplayName = definition.DisplayName,
                Description = definition.Description,
                Category = definition.Category,
                IsProtected = isProtected,
                IsRecommended = definition.Recommended,
                RequiresAdmin = definition.RequiresAdmin,
                RiskNote = definition.RiskNote,
                IsReversible = true,
                PerformanceGainScore = definition.PerformanceGainScore
            });
        }

        _logger.LogDebug(
            "Estado de privacidade lido: {Protected}/{Total} itens protegidos.",
            items.Count(i => i.IsProtected), items.Count);

        return items;
    }

    /// <inheritdoc />
    public IReadOnlyDictionary<PrivacyCategory, IReadOnlyList<PrivacyItem>> GroupByCategory(IReadOnlyList<PrivacyItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        return Enum.GetValues<PrivacyCategory>()
            .ToDictionary(
                category => category,
                category => (IReadOnlyList<PrivacyItem>)items
                    .Where(i => i.Category == category)
                    .OrderByDescending(i => i.IsRecommended)
                    .ThenBy(i => i.DisplayName, StringComparer.CurrentCulture)
                    .ToList());
    }

    /// <inheritdoc />
    public async Task<PrivacyApplyResult> ApplyAsync(IReadOnlyList<PrivacyItem> items, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(items);

        var isElevated = _systemInformation.IsAdministrator;
        var services = await LoadServicesAsync(cancellationToken).ConfigureAwait(false);

        var applied = 0;
        var failed = 0;
        var skipped = 0;
        var requiresElevation = false;
        var errors = new List<string>();

        foreach (var item in items)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var definition = PrivacyCatalog.All.FirstOrDefault(d =>
                string.Equals(d.Id, item.Id, StringComparison.OrdinalIgnoreCase));

            if (definition is null)
            {
                _logger.LogWarning("Item de privacidade desconhecido: {ItemId}.", item.Id);
                failed++;
                errors.Add($"Item desconhecido: {item.Id}.");
                continue;
            }

            if (definition.RequiresAdmin && !isElevated)
            {
                skipped++;
                requiresElevation = true;
                errors.Add($"'{definition.DisplayName}' requer privilégios de administrador.");
                continue;
            }

            try
            {
                var success = await ApplyDefinitionAsync(definition, item.IsProtected, services, cancellationToken).ConfigureAwait(false);

                if (success)
                {
                    applied++;
                }
                else
                {
                    failed++;
                    errors.Add($"Falha ao aplicar '{definition.DisplayName}'.");
                }
            }
            catch (UnauthorizedAccessException)
            {
                skipped++;
                requiresElevation = true;
                errors.Add($"Acesso negado em '{definition.DisplayName}' (requer administrador).");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                failed++;
                errors.Add($"Erro em '{definition.DisplayName}': {ex.Message}");
                _logger.LogError(ex, "Falha ao aplicar o item de privacidade {ItemId}.", item.Id);
            }
        }

        var result = new PrivacyApplyResult(applied, failed, skipped, requiresElevation, errors);

        _logger.LogInformation("Aplicação de privacidade concluída: {Summary}.", result.Summary);

        await _history.RecordAsync(
            ActionKind.Privacy,
            "Ajustes de privacidade",
            result.Summary + (requiresElevation ? " (alguns itens exigem administrador)" : string.Empty),
            success: result.IsSuccess,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return result;
    }

    /// <inheritdoc />
    public async Task<PrivacyApplyResult> ApplyRecommendationsAsync(CancellationToken cancellationToken = default)
    {
        var items = await GetItemsAsync(cancellationToken).ConfigureAwait(false);

        var recommended = items
            .Where(i => i.IsRecommended)
            .Select(i => new PrivacyItem
            {
                Id = i.Id,
                DisplayName = i.DisplayName,
                Description = i.Description,
                Category = i.Category,
                IsProtected = true,
                IsRecommended = i.IsRecommended,
                RequiresAdmin = i.RequiresAdmin,
                RiskNote = i.RiskNote,
                PerformanceGainScore = i.PerformanceGainScore
            })
            .ToList();

        _logger.LogInformation("Aplicando {Count} recomendação(ões) de privacidade.", recommended.Count);

        return await ApplyAsync(recommended, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<PrivacyApplyResult> RestoreDefaultsAsync(CancellationToken cancellationToken = default)
    {
        var items = await GetItemsAsync(cancellationToken).ConfigureAwait(false);

        var toRestore = items
            .Where(i => i.IsProtected)
            .Select(i => new PrivacyItem
            {
                Id = i.Id,
                DisplayName = i.DisplayName,
                Description = i.Description,
                Category = i.Category,
                IsProtected = false,
                IsRecommended = i.IsRecommended,
                RequiresAdmin = i.RequiresAdmin,
                RiskNote = i.RiskNote,
                PerformanceGainScore = i.PerformanceGainScore
            })
            .ToList();

        _logger.LogInformation("Restaurando padrões de {Count} item(ns) de privacidade.", toRestore.Count);

        return await ApplyAsync(toRestore, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public double CalculateProtectionScore(IReadOnlyList<PrivacyItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        if (items.Count == 0)
        {
            return 100d;
        }

        // Itens recomendados têm peso dobrado: proteger o essencial vale mais.
        var totalWeight = items.Sum(i => i.IsRecommended ? 2d : 1d);
        var protectedWeight = items.Where(i => i.IsProtected).Sum(i => i.IsRecommended ? 2d : 1d);

        return totalWeight <= 0 ? 100d : Math.Clamp((protectedWeight / totalWeight) * 100d, 0d, 100d);
    }

    /// <summary>Aplica (ou reverte) todos os tweaks de uma definição.</summary>
    private async Task<bool> ApplyDefinitionAsync(
        PrivacyDefinition definition,
        bool protect,
        IReadOnlyDictionary<string, WindowsServiceInfo> services,
        CancellationToken cancellationToken)
    {
        var success = true;

        foreach (var tweak in definition.Tweaks)
        {
            try
            {
                if (protect)
                {
                    _registry.SetDword(tweak.Hive, tweak.KeyPath, tweak.ValueName, tweak.ProtectedValue);
                }
                else if (tweak.DefaultValue is { } defaultValue)
                {
                    _registry.SetDword(tweak.Hive, tweak.KeyPath, tweak.ValueName, defaultValue);
                }
                else
                {
                    // Valor inexistente no Windows padrão: remove para restaurar o comportamento original.
                    _registry.DeleteValue(tweak.Hive, tweak.KeyPath, tweak.ValueName);
                }
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException)
            {
                _logger.LogWarning(ex, "Sem permissão para alterar {Hive}\\{Key}\\{Value}.", tweak.Hive, tweak.KeyPath, tweak.ValueName);
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Falha ao gravar {Hive}\\{Key}\\{Value}.", tweak.Hive, tweak.KeyPath, tweak.ValueName);
                success = false;
            }
        }

        if (definition.ServiceName is { Length: > 0 } serviceName && services.ContainsKey(serviceName))
        {
            var target = protect ? definition.ProtectedServiceStartup : definition.DefaultServiceStartup;

            var changed = await _services.SetStartupKindAsync(serviceName, target, cancellationToken).ConfigureAwait(false);
            success &= changed;

            if (protect && definition.ProtectedServiceStartup == ServiceStartupKind.Disabled)
            {
                await _services.StopAsync(serviceName, cancellationToken).ConfigureAwait(false);
            }
        }

        return success;
    }

    /// <summary>Verifica se um item está atualmente protegido.</summary>
    private bool IsProtected(
        PrivacyDefinition definition,
        IReadOnlyDictionary<string, WindowsServiceInfo> services)
    {
        foreach (var tweak in definition.Tweaks)
        {
            int? current;

            try
            {
                current = _registry.GetDword(tweak.Hive, tweak.KeyPath, tweak.ValueName);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Não foi possível ler {Hive}\\{Key}\\{Value}.", tweak.Hive, tweak.KeyPath, tweak.ValueName);
                return false;
            }

            if (current != tweak.ProtectedValue)
            {
                return false;
            }
        }

        if (definition.ServiceName is { Length: > 0 } serviceName)
        {
            if (!services.TryGetValue(serviceName, out var service))
            {
                // Serviço inexistente = nada a proteger (considera protegido).
                return definition.Tweaks.Count > 0;
            }

            var expected = definition.ProtectedServiceStartup;
            var matches = service.StartupKind == expected ||
                          (expected == ServiceStartupKind.Disabled && service.StartupKind == ServiceStartupKind.Disabled);

            if (!matches)
            {
                return false;
            }

            // Serviço desabilitado ainda em execução conta como não protegido.
            if (expected == ServiceStartupKind.Disabled && service.IsRunning)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Carrega os serviços uma única vez, indexados por nome.</summary>
    private async Task<IReadOnlyDictionary<string, WindowsServiceInfo>> LoadServicesAsync(CancellationToken cancellationToken)
    {
        try
        {
            var services = await _services.GetServicesAsync(cancellationToken).ConfigureAwait(false);

            return services.ToDictionary(s => s.ServiceName, StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Não foi possível listar os serviços do Windows.");
            return new Dictionary<string, WindowsServiceInfo>(StringComparer.OrdinalIgnoreCase);
        }
    }
}
