namespace HLProOptimizer.Application.Optimization;

/// <summary>
/// Identificadores estáveis dos passos de otimização. Centralizados para evitar
/// strings mágicas e permitir que a UI/plugins referenciem passos com segurança.
/// </summary>
public static class OptimizationStepIds
{
    /// <summary>Criação do ponto de restauração (sempre o primeiro passo).</summary>
    public const string RestorePoint = "opt.backup.restorepoint";

    /// <summary>Limpeza de arquivos temporários.</summary>
    public const string TemporaryFiles = "opt.cleanup.temp";

    /// <summary>Limpeza de cache do sistema.</summary>
    public const string SystemCache = "opt.cleanup.cache";

    /// <summary>Limpeza de cache de navegadores.</summary>
    public const string BrowserCache = "opt.cleanup.browser";

    /// <summary>Esvaziar a lixeira.</summary>
    public const string RecycleBin = "opt.cleanup.recyclebin";

    /// <summary>Limpeza/otimização do registro.</summary>
    public const string Registry = "opt.registry.clean";

    /// <summary>Compactação de memória (working sets).</summary>
    public const string MemoryCompact = "opt.memory.compact";

    /// <summary>Flush do cache DNS.</summary>
    public const string DnsFlush = "opt.network.dnsflush";

    /// <summary>Ajustes de rede para baixa latência.</summary>
    public const string NetworkLatency = "opt.network.latency";

    /// <summary>Otimização de serviços do Windows.</summary>
    public const string Services = "opt.services";

    /// <summary>Desativação de telemetria.</summary>
    public const string Telemetry = "opt.privacy.telemetry";

    /// <summary>Desativação de itens de inicialização não essenciais.</summary>
    public const string Startup = "opt.startup";

    /// <summary>Desativação da Game Bar / DVR.</summary>
    public const string GameBar = "opt.gamer.gamebar";

    /// <summary>Ajustes do MMCSS (Multimedia Class Scheduler Service).</summary>
    public const string Mmcss = "opt.gamer.mmcss";

    /// <summary>Desativação da indexação de busca.</summary>
    public const string Indexing = "opt.gamer.indexing";

    /// <summary>Efeitos visuais em modo desempenho.</summary>
    public const string VisualEffects = "opt.performance.visualeffects";

    /// <summary>Pausa temporária do Windows Update.</summary>
    public const string WindowsUpdatePause = "opt.gamer.wupause";

    /// <summary>Ativação do plano Desempenho Máximo.</summary>
    public const string UltimatePerformance = "opt.power.ultimate";

    /// <summary>Reinício do Explorer (aplica tweaks de interface).</summary>
    public const string RestartExplorer = "opt.ui.explorer";
}
