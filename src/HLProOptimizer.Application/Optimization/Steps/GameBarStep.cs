using HLProOptimizer.Core.Abstractions;
using HLProOptimizer.Core.Enums;
using HLProOptimizer.Core.Models;
using Microsoft.Extensions.Logging;

namespace HLProOptimizer.Application.Optimization.Steps;

/// <summary>
/// Desativa a Xbox Game Bar e o Game DVR (gravação em segundo plano), que consomem
/// GPU/CPU e causam stuttering em jogos. Mantém o "Game Mode" do Windows ativado.
/// </summary>
public sealed class GameBarStep : OptimizationStepBase
{
    private const string GameBarKey = @"Software\Microsoft\GameBar";
    private const string GameConfigStoreKey = @"System\GameConfigStore";
    private const string GameDvrPolicyKey = @"SOFTWARE\Policies\Microsoft\Windows\GameDVR";
    private const string GameDvrUserKey = @"Software\Microsoft\Windows\CurrentVersion\GameDVR";

    private readonly IRegistryService _registry;

    /// <summary>Cria o passo.</summary>
    /// <param name="registry">Acesso ao registro.</param>
    /// <param name="logger">Logger.</param>
    public GameBarStep(IRegistryService registry, ILogger<GameBarStep> logger)
        : base(logger)
    {
        _registry = registry;
    }

    /// <inheritdoc />
    public override string Id => OptimizationStepIds.GameBar;

    /// <inheritdoc />
    public override string Name => "Desativar Game Bar e DVR";

    /// <inheritdoc />
    public override string Description => "Remove a captura em segundo plano da Xbox Game Bar e do Game DVR (ganho de FPS e menos stuttering).";

    /// <inheritdoc />
    public override IReadOnlyCollection<OptimizationMode> SupportedModes { get; } =
    [
        OptimizationMode.Gamer
    ];

    /// <inheritdoc />
    public override int Order => 110;

    /// <inheritdoc />
    public override bool IsReversible => true;

    /// <inheritdoc />
    protected override Task<(bool Success, string Message, long BytesFreed)> ExecuteCoreAsync(
        OptimizationContext context,
        CancellationToken cancellationToken)
    {
        var before = SnapshotValues();

        // HKCU - não requer administrador.
        _registry.SetDword(RegistryHiveKind.CurrentUser, GameBarKey, "ShowStartupPanel", 0);
        _registry.SetDword(RegistryHiveKind.CurrentUser, GameBarKey, "UseNexusForGameBarEnabled", 0);
        _registry.SetDword(RegistryHiveKind.CurrentUser, GameBarKey, "GameBarPresenceWriter", 0);

        // Mantém o Game Mode do Windows ligado (ele ajuda em cenários de multitarefa).
        _registry.SetDword(RegistryHiveKind.CurrentUser, GameBarKey, "AllowAutoGameMode", 1);
        _registry.SetDword(RegistryHiveKind.CurrentUser, GameBarKey, "AutoGameModeEnabled", 1);

        _registry.SetDword(RegistryHiveKind.CurrentUser, GameConfigStoreKey, "GameDVR_Enabled", 0);
        _registry.SetDword(RegistryHiveKind.CurrentUser, GameConfigStoreKey, "GameDVR_FSEBehaviorMode", 2);
        _registry.SetDword(RegistryHiveKind.CurrentUser, GameConfigStoreKey, "GameDVR_HonorUserFSEBehaviorMode", 1);
        _registry.SetDword(RegistryHiveKind.CurrentUser, GameConfigStoreKey, "GameDVR_DXGIHonorFSEWindowsCompatible", 1);
        _registry.SetDword(RegistryHiveKind.CurrentUser, GameConfigStoreKey, "GameDVR_EFSEFeatureFlags", 0);

        _registry.SetDword(RegistryHiveKind.CurrentUser, GameDvrUserKey, "AppCaptureEnabled", 0);
        _registry.SetDword(RegistryHiveKind.CurrentUser, GameDvrUserKey, "HistoricalCaptureEnabled", 0);

        context.State[$"{Id}.before"] = before;

        // HKLM - melhor esforço: quando não há elevação, a parte do usuário já cobre o essencial.
        var policyApplied = TryApplyPolicy();

        return Task.FromResult((
            true,
            policyApplied
                ? "Game Bar e Game DVR desativados (usuário + política)."
                : "Game Bar e Game DVR desativados para o usuário atual (política de máquina exige administrador).",
            0L));
    }

    /// <inheritdoc />
    public override Task<OptimizationStepResult> RevertAsync(OptimizationContext context, CancellationToken cancellationToken = default)
    {
        _registry.SetDword(RegistryHiveKind.CurrentUser, GameBarKey, "ShowStartupPanel", 1);
        _registry.SetDword(RegistryHiveKind.CurrentUser, GameBarKey, "UseNexusForGameBarEnabled", 1);
        _registry.SetDword(RegistryHiveKind.CurrentUser, GameConfigStoreKey, "GameDVR_Enabled", 1);
        _registry.SetDword(RegistryHiveKind.CurrentUser, GameDvrUserKey, "AppCaptureEnabled", 1);
        _registry.SetDword(RegistryHiveKind.CurrentUser, GameDvrUserKey, "HistoricalCaptureEnabled", 1);

        try
        {
            _registry.DeleteValue(RegistryHiveKind.LocalMachine, GameDvrPolicyKey, "AllowGameDVR");
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Não foi possível remover a política de GameDVR em HKLM.");
        }

        return Task.FromResult(new OptimizationStepResult(Id, Name, true, "Game Bar e Game DVR restaurados."));
    }

    /// <summary>Aplica a política em HKLM (ignora falha por falta de elevação).</summary>
    private bool TryApplyPolicy()
    {
        try
        {
            _registry.SetDword(RegistryHiveKind.LocalMachine, GameDvrPolicyKey, "AllowGameDVR", 0);
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            Logger.LogWarning("Sem permissão para gravar a política de GameDVR em HKLM.");
            return false;
        }
        catch (System.Security.SecurityException ex)
        {
            Logger.LogWarning(ex, "Acesso negado à política de GameDVR em HKLM.");
            return false;
        }
    }

    /// <summary>Captura os valores atuais para permitir reversão fiel.</summary>
    private Dictionary<string, int?> SnapshotValues() => new(StringComparer.OrdinalIgnoreCase)
    {
        ["ShowStartupPanel"] = _registry.GetDword(RegistryHiveKind.CurrentUser, GameBarKey, "ShowStartupPanel"),
        ["UseNexusForGameBarEnabled"] = _registry.GetDword(RegistryHiveKind.CurrentUser, GameBarKey, "UseNexusForGameBarEnabled"),
        ["GameDVR_Enabled"] = _registry.GetDword(RegistryHiveKind.CurrentUser, GameConfigStoreKey, "GameDVR_Enabled"),
        ["AppCaptureEnabled"] = _registry.GetDword(RegistryHiveKind.CurrentUser, GameDvrUserKey, "AppCaptureEnabled")
    };
}
