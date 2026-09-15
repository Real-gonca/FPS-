using HLProOptimizer.Core.Models;

namespace HLProOptimizer.Core.Abstractions;

/// <summary>
/// Modo Gamer / FPS Boost. Ativa um conjunto de tweaks (energia, MMCSS, Game Bar,
/// rede, indexação, processos em background) e sabe revertê-los integralmente.
/// </summary>
public interface IGameModeService
{
    /// <summary>Disparado sempre que o estado do Modo Gamer muda.</summary>
    event EventHandler<GameModeState>? StateChanged;

    /// <summary>Estado atual.</summary>
    GameModeState CurrentState { get; }

    /// <summary>Indica se o Modo Gamer está ativo.</summary>
    bool IsActive { get; }

    /// <summary>Tweaks disponíveis (com estado habilitado/desabilitado).</summary>
    IReadOnlyList<GameTweak> AvailableTweaks { get; }

    /// <summary>Ativa o Modo Gamer.</summary>
    /// <param name="enabledTweakIds">Ids dos tweaks selecionados (null = todos os habilitados por padrão).</param>
    /// <param name="progress">Log em tempo real.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    Task<GameModeState> ActivateAsync(
        IReadOnlyCollection<string>? enabledTweakIds = null,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>Desativa o Modo Gamer, revertendo todos os tweaks aplicados.</summary>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    Task<GameModeState> DeactivateAsync(CancellationToken cancellationToken = default);

    /// <summary>Restaura o estado persistido na inicialização do app (se configurado).</summary>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    Task<GameModeState> RestoreStateAsync(CancellationToken cancellationToken = default);

    /// <summary>Aplica prioridade alta ao processo do jogo detectado.</summary>
    /// <param name="processName">Nome do processo do jogo (sem extensão).</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    Task<bool> BoostGameProcessAsync(string processName, CancellationToken cancellationToken = default);
}
