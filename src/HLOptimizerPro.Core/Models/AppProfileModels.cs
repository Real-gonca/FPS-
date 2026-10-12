namespace HLOptimizerPro.Core.Models;

/// <summary>Plano de energia do Windows usado pelos perfis.</summary>
public sealed class PowerPlanInfo
{
    public required string Guid { get; init; }
    public required string Name { get; init; }
    public bool IsActive { get; init; }

    public const string HighPerformance = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";
    public const string Balanced = "381b4222-f694-41f0-9685-ff5bb260df2e";
    public const string PowerSaver = "a1841308-3541-4fab-bc81-f71556f20b4a";
    public const string UltimatePerformance = "e9a42b02-d5df-448d-aa00-03f14749eb61";

    public string ShortName => Guid.ToLowerInvariant() switch
    {
        HighPerformance => "Alto Desempenho",
        Balanced => "Equilibrado",
        PowerSaver => "Economia de Energia",
        UltimatePerformance => "Desempenho Máximo",
        _ => Name,
    };
}

/// <summary>
/// Perfil de otimização de uma aplicação (módulo App Tuner e Game Mode).
/// Todas as ações são registadas no diário para reversão 100 %.
/// </summary>
public sealed class AppOptimizationProfile
{
    /// <summary>Chave do catálogo de apps (ex.: "discord", "chrome", "gta5").</summary>
    public required string AppKey { get; init; }

    public required string DisplayName { get; init; }

    /// <summary>Nomes de processo possíveis (deteção em runtime). Sem extensão, minúsculas.</summary>
    public IReadOnlyList<string> ProcessNames { get; init; } = Array.Empty<string>();

    /// <summary>Pasta/nome do executável (para deteção de instalação).</summary>
    public string? DetectionHint { get; init; }

    /// <summary>Chaves de registo Uninstall onde procurar a app (deteção fiável de instalação).</summary>
    public IReadOnlyList<string> RegistryNames { get; init; } = Array.Empty<string>();

    public string IconGlyph { get; init; } = "\uE7FC";

    /// <summary>Prioridade de processo a aplicar (ex.: "High", "AboveNormal").</summary>
    public string ProcessPriority { get; init; } = "Normal";

    /// <summary>Número máximo de núcleos a atribuir (0 = todos; nunca abaixo de 2).</summary>
    public int PreferredCores { get; init; }

    /// <summary>Plano de energia a ativar enquanto a app/jogo corre.</summary>
    public string? PowerPlanGuid { get; init; }

    /// <summary>Ativar Game Mode do Windows para este processo.</summary>
    public bool EnableWindowsGameMode { get; init; }

    /// <summary>É um jogo (tratado pelo Game Mode com deteção automática).</summary>
    public bool IsGame { get; init; }

    /// <summary>Notas apresentadas ao utilizador (o que será feito, em português).</summary>
    public string Notes { get; init; } = string.Empty;

    /// <summary>Ganho esperado descrito de forma honesta (sem promessas de números mágicos).</summary>
    public string ExpectedGain { get; init; } = "Redução de oscilações de CPU/RAM e menor latência de resposta.";

    /// <summary>Estado atual na máquina.</summary>
    public AppDetectionState State { get; set; } = AppDetectionState.Desconhecido;

    /// <summary>Toggles de UI (prioridade / afinidade / plano de energia).</summary>
    public bool PrioritizeProcess { get; set; } = true;
    public bool UseCpuAffinity { get; set; }
    public bool ApplyPowerPlan { get; set; }

    /// <summary>Pid detetado em execução (0 = não está a correr).</summary>
    public int RunningPid { get; set; }

    public bool IsInstalled => State is AppDetectionState.Instalado or AppDetectionState.Otimizado;
    public bool IsRunning => RunningPid > 0;

    public string StateText => State switch
    {
        AppDetectionState.Otimizado => "Otimizado",
        AppDetectionState.Instalado => "Pronto",
        AppDetectionState.NaoInstalado => "Não instalado",
        _ => "A detetar…",
    };
}

/// <summary>Estado do motor de Game Mode (3.4e).</summary>
public sealed class GameModeStatus
{
    public GameModeState State { get; init; } = GameModeState.Desativado;
    public string? ActiveGameName { get; init; }
    public int ActivePid { get; init; }
    public string? ActiveProcessName { get; init; }
    public DateTime? ActivatedAt { get; init; }
    public int BackgroundProcessesThrottled { get; init; }
    public string LastMessage { get; init; } = string.Empty;

    public string StateText => State switch
    {
        GameModeState.Ativo => $"Ativo — {ActiveGameName}",
        GameModeState.AguardandoProcesso => $"Aguarda processo — {ActiveGameName}",
        GameModeState.ARestaurar => "A restaurar estado anterior…",
        GameModeState.AguardandoJogo => "A monitorizar jogos da biblioteca",
        _ => "Desativado",
    };
}

/// <summary>Estado do módulo Latência (3.4f).</summary>
public sealed class LatencyStatus
{
    /// <summary>Latência de ida e volta medida (ICMP ao gateway/internet) em ms.</summary>
    public double CurrentLatencyMs { get; set; }

    /// <summary>Jitter medido (desvio entre amostras).</summary>
    public double JitterMs { get; set; }

    /// <summary>Nível de DPC/ISR por segundo (indica carga de kernel/drivers).</summary>
    public double DpcLatencyUs { get; set; }

    /// <summary>Temporizador do sistema em ms (0.5 ms padrão; valores menores reduzem latência de input).</summary>
    public double TimerResolutionMs { get; set; } = 15.625;

    public double NetworkLatencyMs { get; set; }

    public string QualityText => CurrentLatencyMs switch
    {
        <= 0 => "A medir…",
        < 20 => "Excelente",
        < 50 => "Bom",
        < 100 => "Regular",
        _ => "Elevada",
    };

    public HealthLevel Health => CurrentLatencyMs <= 0 ? HealthLevel.Desconhecido
        : CurrentLatencyMs < 25 ? HealthLevel.Otimo
        : CurrentLatencyMs < 60 ? HealthLevel.Atencao
        : HealthLevel.Critico;
}

/// <summary>Nível (0–100) de um dos componentes ajustáveis do módulo Latência.</summary>
public sealed class LatencyComponent
{
    public required string Key { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    public string IconKind { get; init; } = "TimerOutline";

    /// <summary>Valor atual do slider (0–100).</summary>
    public double Value { get; set; } = 50;

    public string LowLabel { get; init; } = "Padrão";
    public string HighLabel { get; init; } = "Máximo";

    /// <summary>Ação aplicada quando o slider está à direita do ponto neutro.</summary>
    public Action<LatencyComponent>? OnApply { get; init; }

    public string ValueText => Value switch
    {
        < 20 => "Desativado",
        < 45 => "Padrão",
        < 70 => "Otimizado",
        < 90 => "Agressivo",
        _ => "Máximo",
    };
}
