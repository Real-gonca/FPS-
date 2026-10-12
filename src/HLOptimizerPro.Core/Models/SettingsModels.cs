using System.Text.Json.Serialization;

namespace HLOptimizerPro.Core.Models;

/// <summary>
/// Configurações persistidas em <c>%AppData%\HLOptimizerPro\settings.json</c>.
/// Todas as opções têm valores por omissão conservadores (nada é aplicado sem o utilizador pedir).
/// </summary>
public sealed class AppSettings
{
    // ---- Interface ------------------------------------------------------------
    /// <summary>Código de idioma: "pt-PT" (padrão), "pt-AO", "en-US".</summary>
    public string Language { get; set; } = "pt-PT";

    /// <summary>Modo escuro: o design system é escuro por omissão (requisito).</summary>
    public bool DarkMode { get; set; } = true;

    /// <summary>Escala da interface (0.8 – 1.5) aplicada sobre o DPI do sistema.</summary>
    public double UiScale { get; set; } = 1.0;

    /// <summary>Resolver preferida para o gráfico (Auto = resolução real do ecrã).</summary>
    public string PreferredResolution { get; set; } = "Auto";

    /// <summary>Animações de transição entre páginas e gauges.</summary>
    public bool AnimationsEnabled { get; set; } = true;

    // ---- Comportamento --------------------------------------------------------
    public bool StartWithWindows { get; set; }
    public bool StartMinimized { get; set; }
    public bool MinimizeToTray { get; set; } = true;
    public bool NotificationsEnabled { get; set; } = true;
    public bool ShowTemperatureWarnings { get; set; } = true;

    /// <summary>Intervalo de atualização das métricas em tempo real (1–5 s).</summary>
    public int RealtimeIntervalMs { get; set; } = HLOptimizerPro.Core.Helpers.AppConstants.RealtimeIntervalMs;

    // ---- Segurança ------------------------------------------------------------
    /// <summary>Modo Simular global: mostra o que seria alterado sem escrever nada.</summary>
    public bool SimulationMode { get; set; }

    /// <summary>Criar ponto de restauração automaticamente antes de lotes de otimizações.</summary>
    public bool CreateRestorePointBeforeChanges { get; set; } = true;

    /// <summary>Exportar o registo (.reg) dos valores afetados antes de aplicar.</summary>
    public bool BackupRegistryBeforeChanges { get; set; } = true;

    /// <summary>Confirmar sempre operações de risco médio/alto.</summary>
    public bool ConfirmRiskyActions { get; set; } = true;

    // ---- Privacidade ----------------------------------------------------------
    /// <summary>Telemetria de utilização — DESLIGADA por omissão e sujeita a consentimento.</summary>
    public bool TelemetryEnabled { get; set; }

    /// <summary>Data do consentimento de telemetria (prova de consentimento explícito).</summary>
    public DateTime? TelemetryConsentDate { get; set; }

    // ---- Limpeza automática ---------------------------------------------------
    public bool AutoCleanupEnabled { get; set; }

    /// <summary>Intervalo em dias (1–30). Valor predefinido: 7 dias.</summary>
    public int AutoCleanupIntervalDays { get; set; } = 7;

    public DateTime? LastCleanupUtc { get; set; }

    /// <summary>Chaves das limpezas marcadas para a limpeza automática.</summary>
    public List<string> AutoCleanupTargets { get; set; } = new() { "temp-user", "temp-windows", "dns" };

    // ---- Módulos --------------------------------------------------------------
    public bool GameModeEnabled { get; set; }
    public bool GameModeAutoActivate { get; set; } = true;
    public bool GameModeRestoreOnExit { get; set; } = true;
    public TweakProfile LastTweakProfile { get; set; } = TweakProfile.Balanceado;

    /// <summary>Perfis de aplicação ativos (App Tuner / Game Mode).</summary>
    public List<string> EnabledAppProfiles { get; set; } = new();

    /// <summary>Jogos adicionados manualmente à biblioteca (nome → caminho do executável).</summary>
    public Dictionary<string, string> CustomGames { get; set; } = new();

    /// <summary>DNS preferido pelo utilizador (chave do fornecedor) ou vazio para automático.</summary>
    public string PreferredDnsProvider { get; set; } = string.Empty;

    /// <summary>Aplica "flush DNS" automaticamente após trocar de DNS.</summary>
    public bool FlushDnsAfterApply { get; set; } = true;

    /// <summary>Renova o IP (ipconfig /renew) após trocar de DNS.</summary>
    public bool RenewIpAfterApply { get; set; }

    public AppSettings Clone() => new()
    {
        Language = Language,
        DarkMode = DarkMode,
        UiScale = UiScale,
        PreferredResolution = PreferredResolution,
        AnimationsEnabled = AnimationsEnabled,
        StartWithWindows = StartWithWindows,
        StartMinimized = StartMinimized,
        MinimizeToTray = MinimizeToTray,
        NotificationsEnabled = NotificationsEnabled,
        ShowTemperatureWarnings = ShowTemperatureWarnings,
        RealtimeIntervalMs = RealtimeIntervalMs,
        SimulationMode = SimulationMode,
        CreateRestorePointBeforeChanges = CreateRestorePointBeforeChanges,
        BackupRegistryBeforeChanges = BackupRegistryBeforeChanges,
        ConfirmRiskyActions = ConfirmRiskyActions,
        TelemetryEnabled = TelemetryEnabled,
        TelemetryConsentDate = TelemetryConsentDate,
        AutoCleanupEnabled = AutoCleanupEnabled,
        AutoCleanupIntervalDays = AutoCleanupIntervalDays,
        LastCleanupUtc = LastCleanupUtc,
        AutoCleanupTargets = new List<string>(AutoCleanupTargets),
        GameModeEnabled = GameModeEnabled,
        GameModeAutoActivate = GameModeAutoActivate,
        GameModeRestoreOnExit = GameModeRestoreOnExit,
        LastTweakProfile = LastTweakProfile,
        EnabledAppProfiles = new List<string>(EnabledAppProfiles),
        CustomGames = new Dictionary<string, string>(CustomGames),
        PreferredDnsProvider = PreferredDnsProvider,
        FlushDnsAfterApply = FlushDnsAfterApply,
        RenewIpAfterApply = RenewIpAfterApply,
    };
}

/// <summary>Conta local da aplicação (login opcional — "Agora não" cria um perfil local).</summary>
public sealed class UserAccount
{
    public string UserId { get; set; } = Guid.NewGuid().ToString("N");
    public string DisplayName { get; set; } = "Utilizador";
    public string Email { get; set; } = string.Empty;

    /// <summary>Hash PBKDF2 da senha (nunca a senha em claro).</summary>
    public string? PasswordHash { get; set; }
    public string? PasswordSalt { get; set; }
    public int PasswordIterations { get; set; } = 100_000;

    /// <summary>Login com Microsoft (MSAL): identificador da conta, sem tokens guardados.</summary>
    public bool IsMicrosoftAccount { get; set; }
    public string? MicrosoftAccountId { get; set; }

    /// <summary>Modo local: sem login, dados apenas nesta máquina.</summary>
    public bool IsLocalOnly { get; set; }

    public bool RememberMe { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime? LastLoginAt { get; set; }
    public string AvatarInitials => string.IsNullOrWhiteSpace(DisplayName)
        ? "HL"
        : string.Concat(DisplayName.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(p => char.ToUpperInvariant(p[0])));

    /// <summary>Perfil apresentado no cabeçalho do Dashboard.</summary>
    public string ProfileText => IsLocalOnly ? "Sessão local" : "Conta HL";
}

/// <summary>Licença (Free/Premium) com validação offline e cache.</summary>
public sealed class LicenseInfo
{
    public LicenseTier Tier { get; set; } = LicenseTier.Free;

    /// <summary>Chave mascarada para apresentação (ex.: HLPR-****-****-A91C).</summary>
    public string MaskedKey { get; set; } = string.Empty;

    /// <summary>Assinatura/HMAC da chave (não guardamos a chave em claro).</summary>
    public string? KeyFingerprint { get; set; }

    public string LicensedTo { get; set; } = string.Empty;
    public DateTime? ActivatedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public DateTime? LastValidationUtc { get; set; }

    /// <summary>Validação offline: a licença continua válida por 30 dias sem Internet.</summary>
    public int OfflineGraceDays { get; set; } = 30;

    public bool IsPremium => Tier == LicenseTier.Premium && !IsExpired;

    public bool IsExpired => ExpiresAt is { } exp && exp < DateTime.UtcNow;

    public string TierText => IsPremium ? "Premium ativada" : "Versão Free";

    public string ExpiryText => ExpiresAt is null
        ? "Licença vitalícia"
        : $"Válida até {ExpiresAt:dd/MM/yyyy}";

    /// <summary>Recursos do plano Premium mostrados no ecrã Sobre/Premium.</summary>
    public static IReadOnlyList<string> PremiumFeatures { get; } = new[]
    {
        "Otimização de Sistema (Windows, rede, disco, arranque)",
        "Game Mode com ativação automática por jogo",
        "App Tuner (prioridade, afinidade de CPU e plano de energia por app)",
        "Módulo de Latência (input, kernel, GPU, rede)",
        "Tweaks por perfil (Competitivo, Streaming, Balanceado, Extremo)",
        "Limpeza automática agendada",
        "Suporte Prioritário",
        "Atualizações Automáticas",
    };
}
