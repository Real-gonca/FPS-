using HLOptimizerPro.Core.Models;
using HLOptimizerPro.Core.Services.Tweaks;
using Microsoft.Extensions.Logging;

namespace HLOptimizerPro.Core.Services;

/// <summary>Uso principal declarado pelo utilizador na tela PC Boost (3.6).</summary>
public enum PrimaryUsage
{
    NaoDefinido = 0,
    Gamer = 1,
    Empresarial = 2,
}

/// <summary>
/// PC Boost (3.6): pergunta ao utilizador qual é o uso principal e monta um plano de
/// otimização adequado — em vez de aplicar o mesmo conjunto a toda a gente.
///
/// O plano é sempre uma seleção de ações do <see cref="OptimizationCatalog"/> mais os ajustes de
/// perfil que façam sentido, e cada item é descrito antes de ser aplicado ("Vamos otimizar do
/// jeito certo!").
/// </summary>
public sealed class PerformanceBoostService
{
    private readonly ILogger<PerformanceBoostService> _logger;
    private readonly OptimizationCatalog _catalog;
    private readonly SystemAssessmentService _assessment;
    private readonly SystemInfoService _systemInfo;
    private readonly GameModeService _gameMode;

    public PerformanceBoostService(
        ILogger<PerformanceBoostService> logger,
        OptimizationCatalog catalog,
        SystemAssessmentService assessment,
        SystemInfoService systemInfo,
        GameModeService gameMode)
    {
        _logger = logger;
        _catalog = catalog;
        _assessment = assessment;
        _systemInfo = systemInfo;
        _gameMode = gameMode;
    }

    /// <summary>Hardware detetado, apresentado no topo da tela PC Boost.</summary>
    public HardwareInfo Hardware => _systemInfo.Hardware;

    /// <summary>Programas e jogos populares para otimização rápida, com o estado de deteção.</summary>
    public IReadOnlyList<AppOptimizationProfile> GetQuickTargets()
    {
        var list = new List<AppOptimizationProfile>();

        foreach (var profile in AppCatalog.BoostTargets)
        {
            var pid = profile.IsGame
                ? _gameMode.Library.FirstOrDefault(g => g.AppKey == profile.AppKey)?.RunningPid ?? 0
                : 0;

            profile.RunningPid = pid;
            list.Add(profile);
        }

        return list;
    }

    /// <summary>Monta o plano de otimização adequado ao uso declarado.</summary>
    public IReadOnlyList<OptimizationAction> BuildPlan(PrimaryUsage usage)
    {
        var keys = usage switch
        {
            PrimaryUsage.Gamer => new[]
            {
                "win.game-mode", "win.game-dvr", "win.foreground-boost", "win.visual-effects",
                "latency.input.acceleration", "latency.gpu.gamedvr", "latency.kernel.gamespriority",
                "latency.kernel.networkthrottling", "disk.trim-ssd", "win.ntfs-last-access",
                "svc.diagtrack", "svc.dmwappush", "privacy.telemetry-min",
            },
            PrimaryUsage.Empresarial => new[]
            {
                "win.menu-delay", "win.visual-effects", "win.startup-delay", "win.ntfs-last-access",
                "disk.storage-sense", "disk.storage-sense-days", "privacy.advertising-id",
                "privacy.activity-history", "privacy.advertising-suggestions", "privacy.feedback",
                "svc.mapsbroker", "svc.fax", "svc.retaildemo", "wu.delivery-optimization",
                "wu.active-hours", "security.smartscreen-edge",
            },
            _ => new[]
            {
                "win.visual-effects", "win.menu-delay", "disk.storage-sense",
                "privacy.telemetry-min", "privacy.advertising-id", "svc.mapsbroker",
            },
        };

        var plan = _catalog.ByKeys(keys).Where(a => a.IsAvailable).ToList();

        _logger.LogInformation("Plano PC Boost para {Usage}: {Count} otimizações.", usage, plan.Count);
        return plan;
    }

    /// <summary>Descrição do plano (o "porquê" de cada escolha), mostrada antes de aplicar.</summary>
    public IReadOnlyList<string> DescribePlan(PrimaryUsage usage) => usage switch
    {
        PrimaryUsage.Gamer => new[]
        {
            "Prioridade máxima ao jogo em execução (Modo Jogo do Windows + prioridade de processo).",
            "Latência de entrada reduzida: buffer do rato e aceleração do ponteiro ajustados.",
            "Rede afinada para resposta imediata (Nagle desativado, sem limitador de pacotes).",
            "GPU sem gravações automáticas em segundo plano.",
            "Telemetria e serviços secundários em modo manual.",
            "Limpeza automática ativada — jogos ocupam muito espaço.",
        },
        PrimaryUsage.Empresarial => new[]
        {
            "Arranque mais rápido: atraso artificial removido e serviços secundários em manual.",
            "Interface mais responsiva (menus, animações e transparência ajustados).",
            "Privacidade reforçada: ID de publicidade, histórico de atividades e sugestões desativados.",
            "Windows Update controlado: horário ativo definido e entrega limitada à rede local.",
            "Limpeza automática ativada para nunca ficar sem espaço.",
            "Sem qualquer alteração que afete redes corporativas, VPNs ou políticas de domínio.",
        },
        _ => new[] { "Conjunto equilibrado de melhorias sem impacto funcional." },
    };

    /// <summary>Avaliação do sistema para o painel lateral do PC Boost (reaproveita a pontuação).</summary>
    public Task<AssessmentReport> AssessAsync(CancellationToken cancellationToken = default)
        => _assessment.AssessAsync(null, cancellationToken);

    /// <summary>Estimativa honesta do efeito do plano (não prometemos FPS exatos).</summary>
    public string EstimateImpact(PrimaryUsage usage, int actionCount) => usage switch
    {
        PrimaryUsage.Gamer =>
            $"{actionCount} ajustes selecionados. Efeito esperado: arranque dos jogos mais rápido, " +
            "menos oscilações de FPS e latência de entrada mais baixa. O ganho exato depende do hardware " +
            "e do jogo — medimos as alterações reais no histórico de atividades.",
        PrimaryUsage.Empresarial =>
            $"{actionCount} ajustes selecionados. Efeito esperado: arranque mais rápido, menos memória ocupada " +
            "em segundo plano e menos dados recolhidos. Todas as funcionalidades de trabalho ficam intactas.",
        _ => $"{actionCount} ajustes selecionados.",
    };
}
