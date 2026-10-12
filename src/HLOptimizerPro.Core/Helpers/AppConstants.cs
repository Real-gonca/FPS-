namespace HLOptimizerPro.Core.Helpers;

/// <summary>
/// Constantes globais da aplicação. Centralizadas para que instalador, tela "Sobre"
/// e serviços partilhem exatamente os mesmos valores.
/// </summary>
public static class AppConstants
{
    // ---- Identidade do produto -------------------------------------------------
    public const string ProductName = "HL Optimizer Pro";
    public const string Publisher = "HL Software";
    public const string Version = "2.0.0";
    public const string Slogan = "Mais Desempenho. Mais Estabilidade. Mais Você.";
    public const string SupportEmail = "suporte@hloptimizerpro.com";
    public const string SupportUrl = "https://hloptimizerpro.com/suporte";
    public const string DocsUrl = "https://hloptimizerpro.com/docs";
    public const string VideosUrl = "https://hloptimizerpro.com/videos";

    // ---- Pastas e ficheiros em %AppData%\HLOptimizerPro ------------------------
    public const string AppFolderName = "HLOptimizerPro";
    public const string SettingsFileName = "settings.json";
    public const string AccountFileName = "account.json";
    public const string LicenseFileName = "license.json";
    public const string JournalFileName = "journal.json";          // alterações aplicadas (reversão)
    public const string ActivityFileName = "activity.jsonl";       // histórico de atividades
    public const string MetricsHistoryFileName = "metrics-history.jsonl";
    public const string LogFolderName = "logs";
    public const string BackupFolderName = "backups";
    public const string ReportFolderName = "relatorios";

    // ---- Temporização ---------------------------------------------------------
    /// <summary>Intervalo de atualização em tempo real (1 s — dentro do requisito de 1–2 s).</summary>
    public const int RealtimeIntervalMs = 1000;

    /// <summary>Intervalo entre amostras persistidas do gráfico de 24 h (60 s → 1440 pontos/dia).</summary>
    public const int HistorySampleSeconds = 60;

    /// <summary>Janela do gráfico "Desempenho do Sistema".</summary>
    public const int HistoryWindowHours = 24;

    /// <summary>Limite de linhas do histórico de métricas antes de compactar o ficheiro.</summary>
    public const int MetricsHistoryMaxLines = 24 * 60;

    /// <summary>Timeout padrão para comandos externos (netsh, powercfg, reg...).</summary>
    public const int CommandTimeoutMs = 30_000;

    // ---- Regras de segurança --------------------------------------------------
    /// <summary>Memória livre mínima recomendada (tira a dica contextual do módulo de memória).</summary>
    public const double MinFreeRamPercent = 20.0;

    /// <summary>Espaço livre mínimo no disco do sistema antes de avisar (%).</summary>
    public const double MinFreeDiskPercent = 10.0;

    /// <summary>Percentagem de fragmentação a partir da qual a desfragmentação é recomendada.</summary>
    public const double FragmentationWarnPercent = 10.0;

    /// <summary>Nunca encerrar/afinidade em menos de 2 núcleos (o sistema fica inutilizável).</summary>
    public const int MinimumCpuCoresForAffinity = 2;

    /// <summary>Prefixo dos pontos de restauro criados pela aplicação.</summary>
    public const string RestorePointDescription = "HL Optimizer Pro — antes das otimizações";
}
