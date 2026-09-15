using HLProOptimizer.Core.Enums;

namespace HLProOptimizer.Core.Models;

/// <summary>
/// Opções configuráveis da tela "Otimização". Cada toggle habilita/desabilita
/// passos individuais do <c>IOptimizationService</c>.
/// </summary>
public sealed class OptimizationOptions
{
    /// <summary>Modo de otimização selecionado.</summary>
    public OptimizationMode Mode { get; set; } = OptimizationMode.Full;

    /// <summary>Nível de agressividade.</summary>
    public OptimizationLevel Level { get; set; } = OptimizationLevel.Balanced;

    /// <summary>Limpar arquivos temporários.</summary>
    public bool CleanTemporaryFiles { get; set; } = true;

    /// <summary>Limpar cache do sistema (thumbnails, shaders).</summary>
    public bool CleanSystemCache { get; set; } = true;

    /// <summary>Otimizar/limpar registro.</summary>
    public bool OptimizeRegistry { get; set; } = true;

    /// <summary>Executar flush de DNS.</summary>
    public bool FlushDns { get; set; } = true;

    /// <summary>Otimizar serviços (colocar em manual os não essenciais).</summary>
    public bool OptimizeServices { get; set; }

    /// <summary>Desativar telemetria e rastreamento.</summary>
    public bool DisableTelemetry { get; set; } = true;

    /// <summary>Criar ponto de restauração antes de aplicar.</summary>
    public bool CreateRestorePoint { get; set; } = true;

    /// <summary>Ativar plano de desempenho máximo.</summary>
    public bool EnableMaximumPerformance { get; set; } = true;

    /// <summary>Esvaziar a lixeira.</summary>
    public bool EmptyRecycleBin { get; set; }

    /// <summary>Limpar cache dos navegadores.</summary>
    public bool CleanBrowserCache { get; set; }

    /// <summary>Desativar itens de inicialização não essenciais.</summary>
    public bool OptimizeStartup { get; set; }

    /// <summary>Aplicar ajustes de rede para baixa latência.</summary>
    public bool OptimizeNetworkLatency { get; set; }

    /// <summary>Reiniciar o Explorer ao final (aplica tweaks de UI).</summary>
    public bool RestartExplorer { get; set; }

    /// <summary>Ids de passos extras habilitados pelo usuário/plugins.</summary>
    public List<string> ExtraStepIds { get; set; } = [];

    /// <summary>Opções padrão do modo Rápido.</summary>
    public static OptimizationOptions ForQuick() => new()
    {
        Mode = OptimizationMode.Quick,
        CleanSystemCache = false,
        OptimizeRegistry = false,
        OptimizeServices = false,
        DisableTelemetry = false,
        CreateRestorePoint = false,
        EnableMaximumPerformance = false
    };

    /// <summary>Opções padrão do modo Completo.</summary>
    public static OptimizationOptions ForFull() => new()
    {
        Mode = OptimizationMode.Full,
        OptimizeServices = true,
        EmptyRecycleBin = true,
        OptimizeStartup = true
    };

    /// <summary>Opções padrão do modo Gamer.</summary>
    public static OptimizationOptions ForGamer() => new()
    {
        Mode = OptimizationMode.Gamer,
        OptimizeRegistry = false,
        OptimizeServices = true,
        EnableMaximumPerformance = true,
        OptimizeNetworkLatency = true,
        OptimizeStartup = true,
        CreateRestorePoint = true
    };

    /// <summary>Opções padrão do modo Privacidade.</summary>
    public static OptimizationOptions ForPrivacy() => new()
    {
        Mode = OptimizationMode.Privacy,
        CleanSystemCache = false,
        OptimizeRegistry = false,
        DisableTelemetry = true,
        EnableMaximumPerformance = false,
        CreateRestorePoint = true
    };
}
