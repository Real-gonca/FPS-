using HLProOptimizer.Core.Enums;
using HLProOptimizer.Core.Models;

namespace HLProOptimizer.Application.GameMode;

/// <summary>
/// Contexto compartilhado pelos tweaks do Modo Gamer.
/// </summary>
/// <remarks>
/// Encapsula um <see cref="OptimizationContext"/> (modo Gamer) para que tweaks
/// possam delegar trabalho a passos de otimização já existentes, além de um
/// dicionário de estado para permitir reversão fiel.
/// </remarks>
public sealed class GameModeContext
{
    /// <summary>Cria o contexto do Modo Gamer.</summary>
    /// <param name="settings">Configurações do usuário.</param>
    /// <param name="profile">Perfil do sistema.</param>
    /// <param name="isElevated">Se o processo está elevado.</param>
    public GameModeContext(AppSettings settings, SystemProfile profile, bool isElevated)
    {
        Settings = settings;
        Profile = profile;
        IsElevated = isElevated;

        Optimization = new OptimizationContext(
            new OptimizationOptions
            {
                Mode = OptimizationMode.Gamer,
                Level = OptimizationLevel.Aggressive,
                EnableMaximumPerformance = true,
                OptimizeNetworkLatency = true,
                CreateRestorePoint = false
            },
            settings,
            profile);
    }

    /// <summary>Contexto de otimização subjacente (modo Gamer).</summary>
    public OptimizationContext Optimization { get; }

    /// <summary>Configurações do usuário.</summary>
    public AppSettings Settings { get; }

    /// <summary>Perfil do sistema.</summary>
    public SystemProfile Profile { get; }

    /// <summary>Indica se o processo atual está elevado.</summary>
    public bool IsElevated { get; }

    /// <summary>Estado livre para reversão (ex.: plano de energia anterior).</summary>
    public Dictionary<string, object?> State { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Bytes de RAM liberados acumulados durante a ativação.</summary>
    public long FreedMemoryBytes { get; set; }

    /// <summary>Processos finalizados durante a ativação.</summary>
    public List<string> TerminatedProcesses { get; } = [];

    /// <summary>Registra uma linha no log do Modo Gamer.</summary>
    /// <param name="message">Mensagem.</param>
    public void Log(string message) => Optimization.Log(message);
}
