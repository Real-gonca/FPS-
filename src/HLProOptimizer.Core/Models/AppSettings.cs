using HLProOptimizer.Core.Enums;

namespace HLProOptimizer.Core.Models;

/// <summary>
/// Configurações persistidas do usuário (tela Configurações).
/// É um POCO serializável em JSON e espelhado na tabela <c>Settings</c> do SQLite;
/// a camada de Presentation o envolve em um ViewModel observável.
/// </summary>
public sealed class AppSettings
{
    // ------------------------- Geral --------------------------------------

    /// <summary>Idioma da interface.</summary>
    public AppLanguage Language { get; set; } = AppLanguage.PtBr;

    /// <summary>Tema visual.</summary>
    public ThemeMode Theme { get; set; } = ThemeMode.Dark;

    /// <summary>Iniciar o aplicativo junto com o Windows.</summary>
    public bool StartWithWindows { get; set; }

    /// <summary>Minimizar para a bandeja ao fechar a janela.</summary>
    public bool MinimizeToTray { get; set; } = true;

    /// <summary>Exibir notificações toast/ícone de bandeja.</summary>
    public bool NotificationsEnabled { get; set; } = true;

    /// <summary>Iniciar minimizado.</summary>
    public bool StartMinimized { get; set; }

    // ------------------------- Otimização ---------------------------------

    /// <summary>Criar backup automático antes de alterações destrutivas.</summary>
    public bool AutoBackupEnabled { get; set; } = true;

    /// <summary>Criar ponto de restauração antes de otimizar.</summary>
    public bool CreateRestorePointBeforeChanges { get; set; } = true;

    /// <summary>Exigir confirmação antes de deletar arquivos.</summary>
    public bool ConfirmBeforeDelete { get; set; } = true;

    /// <summary>Nível de agressividade das otimizações.</summary>
    public OptimizationLevel OptimizationLevel { get; set; } = OptimizationLevel.Balanced;

    /// <summary>Modo de otimização selecionado por padrão.</summary>
    public OptimizationMode DefaultOptimizationMode { get; set; } = OptimizationMode.Full;

    // ------------------------- Modo Gamer ---------------------------------

    /// <summary>Manter o Modo Gamer ativo entre sessões.</summary>
    public bool GameModeAutoStart { get; set; }

    /// <summary>Desativar o Modo Gamer automaticamente quando o jogo fechar.</summary>
    public bool GameModeAutoDisableOnGameExit { get; set; }

    /// <summary>Processos considerados "jogo" (detecção automática + lista do usuário).</summary>
    public List<string> GameProcessNames { get; set; } = [];

    /// <summary>Limpar a lista de processos em segundo plano ao ativar o Modo Gamer.</summary>
    public bool GameModeKillBackgroundProcesses { get; set; } = true;

    // ------------------------- Monitoramento ------------------------------

    /// <summary>Intervalo de atualização das métricas (1, 2 ou 5 segundos).</summary>
    public int MonitoringIntervalSeconds { get; set; } = 1;

    /// <summary>Janela de histórico dos gráficos, em segundos.</summary>
    public int MonitoringHistorySeconds { get; set; } = 60;

    /// <summary>Exibir ícone na bandeja com uso de CPU.</summary>
    public bool ShowTrayIcon { get; set; } = true;

    /// <summary>Overlay em jogos (recurso futuro, apenas persistido).</summary>
    public bool GameOverlayEnabled { get; set; }

    /// <summary>Medir latência de rede via ping durante o monitoramento.</summary>
    public bool MeasureNetworkLatency { get; set; }

    /// <summary>Host usado na medição de latência.</summary>
    public string LatencyProbeHost { get; set; } = "8.8.8.8";

    // ------------------------- Atualizações -------------------------------

    /// <summary>Verificar atualizações automaticamente.</summary>
    public bool AutoCheckUpdates { get; set; } = true;

    /// <summary>Frequência da verificação.</summary>
    public UpdateFrequency UpdateFrequency { get; set; } = UpdateFrequency.Weekly;

    /// <summary>Instalar atualizações automaticamente.</summary>
    public bool AutoInstallUpdates { get; set; }

    /// <summary>Canal de atualização.</summary>
    public UpdateChannel UpdateChannel { get; set; } = UpdateChannel.Stable;

    /// <summary>Data da última verificação.</summary>
    public DateTime? LastUpdateCheck { get; set; }

    // ------------------------- Limpeza ------------------------------------

    /// <summary>Categorias de limpeza marcadas por padrão (ids de CleanupTarget).</summary>
    public List<string> DefaultCleanupTargetIds { get; set; } = [];

    /// <summary>Excluir pastas da limpeza (caminhos absolutos).</summary>
    public List<string> CleanupExclusions { get; set; } = [];

    /// <summary>Apagar arquivos apenas com mais de N dias (0 = todos).</summary>
    public int CleanupMinimumFileAgeDays { get; set; }

    // ------------------------- Rede / DNS ---------------------------------

    /// <summary>Preset de DNS aplicado por padrão (nome do DnsPreset).</summary>
    public string PreferredDnsPreset { get; set; } = "Cloudflare";

    /// <summary>Bloquear domínios de telemetria no arquivo hosts.</summary>
    public bool BlockTelemetryDomains { get; set; } = true;

    /// <summary>Domínios extras bloqueados pelo usuário no hosts.</summary>
    public List<string> CustomBlockedDomains { get; set; } = [];

    /// <summary>Id do plugin ativo para telemetria, se houver.</summary>
    public string? ActiveTelemetryPluginId { get; set; }

    /// <summary>Configurações padrão de fábrica (usadas por "Restaurar Padrões").</summary>
    public static AppSettings CreateDefault() => new();

    /// <summary>Cria uma cópia independente (para edição transacional na UI).</summary>
    public AppSettings Clone() => new()
    {
        Language = Language,
        Theme = Theme,
        StartWithWindows = StartWithWindows,
        MinimizeToTray = MinimizeToTray,
        NotificationsEnabled = NotificationsEnabled,
        StartMinimized = StartMinimized,
        AutoBackupEnabled = AutoBackupEnabled,
        CreateRestorePointBeforeChanges = CreateRestorePointBeforeChanges,
        ConfirmBeforeDelete = ConfirmBeforeDelete,
        OptimizationLevel = OptimizationLevel,
        DefaultOptimizationMode = DefaultOptimizationMode,
        GameModeAutoStart = GameModeAutoStart,
        GameModeAutoDisableOnGameExit = GameModeAutoDisableOnGameExit,
        GameProcessNames = [.. GameProcessNames],
        GameModeKillBackgroundProcesses = GameModeKillBackgroundProcesses,
        MonitoringIntervalSeconds = MonitoringIntervalSeconds,
        MonitoringHistorySeconds = MonitoringHistorySeconds,
        ShowTrayIcon = ShowTrayIcon,
        GameOverlayEnabled = GameOverlayEnabled,
        MeasureNetworkLatency = MeasureNetworkLatency,
        LatencyProbeHost = LatencyProbeHost,
        AutoCheckUpdates = AutoCheckUpdates,
        UpdateFrequency = UpdateFrequency,
        AutoInstallUpdates = AutoInstallUpdates,
        UpdateChannel = UpdateChannel,
        LastUpdateCheck = LastUpdateCheck,
        DefaultCleanupTargetIds = [.. DefaultCleanupTargetIds],
        CleanupExclusions = [.. CleanupExclusions],
        CleanupMinimumFileAgeDays = CleanupMinimumFileAgeDays,
        PreferredDnsPreset = PreferredDnsPreset,
        BlockTelemetryDomains = BlockTelemetryDomains,
        CustomBlockedDomains = [.. CustomBlockedDomains],
        ActiveTelemetryPluginId = ActiveTelemetryPluginId
    };
}
