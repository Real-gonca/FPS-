using HLProOptimizer.Core.Abstractions;
using Microsoft.Extensions.Logging;

namespace HLProOptimizer.Application.GameMode.Tweaks;

/// <summary>
/// Encerra processos em segundo plano dispensáveis, preservando o jogo, o shell
/// do Windows, drivers e o próprio HL PRO OPTIMIZER.
/// </summary>
public sealed class BackgroundProcessesTweak : IGameModeTweak
{
    /// <summary>Processos que nunca podem ser encerrados.</summary>
    private static readonly IReadOnlySet<string> ProtectedProcesses = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "explorer", "svchost", "csrss", "wininit", "winlogon", "services", "lsass", "smss",
        "dwm", "sihost", "taskhostw", "ctfmon", "fontdrvhost", "SearchHost", "ShellExperienceHost",
        "StartMenuExperienceHost", "RuntimeBroker", "SystemSettings", "ApplicationFrameHost",
        "WindowsTerminal", "cmd", "powershell", "pwsh", "conhost", "audiodg", "nvcontainer",
        "nvdisplay.container", "RtkAudUService64", "SecurityHealthService", "SecurityHealthSystray",
        "MsMpEng", "NisSrv", "HLProOptimizer", "GameBar", "GameBarFTServer", "TextInputHost",
        "dllhost", "Registry", "Memory Compression", "Idle", "System"
    };

    private readonly IProcessService _processService;
    private readonly ILogger<BackgroundProcessesTweak> _logger;

    /// <summary>Cria o tweak.</summary>
    /// <param name="processService">Serviço de processos.</param>
    /// <param name="logger">Logger.</param>
    public BackgroundProcessesTweak(IProcessService processService, ILogger<BackgroundProcessesTweak> logger)
    {
        _processService = processService;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Id => GameTweakCatalog.Ids.BackgroundProcesses;

    /// <inheritdoc />
    public async Task<bool> ApplyAsync(GameModeContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!context.Settings.GameModeKillBackgroundProcesses)
        {
            context.Log($"[{Id}] desativado nas configurações.");
            return false;
        }

        var keep = ProtectedProcesses
            .Concat(context.Settings.GameProcessNames)
            .ToList();

        var terminated = await _processService.TerminateBackgroundProcessesAsync(keep, cancellationToken).ConfigureAwait(false);

        context.TerminatedProcesses.AddRange(terminated);
        context.Log($"[{Id}] {terminated.Count} processo(s) em segundo plano encerrado(s).");

        _logger.LogInformation("Modo Gamer encerrou {Count} processo(s) em segundo plano.", terminated.Count);

        return terminated.Count > 0;
    }

    /// <inheritdoc />
    public Task<bool> RevertAsync(GameModeContext context, CancellationToken cancellationToken = default)
    {
        // Processos encerrados não podem ser "desencerrados"; o usuário os reabre se necessário.
        context?.Log($"[{Id}] processos encerrados não são restaurados automaticamente.");

        return Task.FromResult(true);
    }
}
