namespace HLProOptimizer.Core.Models;

/// <summary>
/// Ajuste aplicado pelo Modo Gamer (FPS Boost). Cada tweak sabe como se aplicar
/// e como se reverter, permitindo "desativar modo gamer" sem deixar resíduos.
/// </summary>
public sealed class GameTweak
{
    /// <summary>Identificador estável (ex.: "gamer.powerplan").</summary>
    public required string Id { get; init; }

    /// <summary>Nome exibido.</summary>
    public required string DisplayName { get; init; }

    /// <summary>Descrição do efeito.</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>Se está habilitado nas opções do Modo Gamer.</summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>Se exige administrador.</summary>
    public bool RequiresAdmin { get; init; }

    /// <summary>Se foi efetivamente aplicado na última ativação.</summary>
    public bool WasApplied { get; set; }

    /// <summary>Mensagem de resultado da aplicação.</summary>
    public string LastResult { get; set; } = string.Empty;

    /// <summary>Ganho estimado de FPS (0-10) usado no resumo.</summary>
    public int EstimatedFpsGain { get; init; }

    /// <summary>Peso para priorização em máquinas fracas.</summary>
    public int Order { get; init; }
}
