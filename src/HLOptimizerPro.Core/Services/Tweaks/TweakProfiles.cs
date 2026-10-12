using HLOptimizerPro.Core.Models;

namespace HLOptimizerPro.Core.Services.Tweaks;

/// <summary>
/// Perfis de Tweaks (3.4d): Competitivo, Streaming, Balanceado e Extremo.
///
/// Cada perfil é apenas uma lista de chaves do <see cref="OptimizationCatalog"/>. Isso é
/// deliberado: um perfil não inventa alterações novas, apenas decide quais aplicar — o que
/// garante que tudo o que um perfil faz é descrito, reversível e auditável na tela Otimizações.
/// </summary>
public static class TweakProfiles
{
    /// <summary>Os quatro perfis, pela ordem apresentada na interface.</summary>
    public static IReadOnlyList<TweakProfileDefinition> All { get; } = new List<TweakProfileDefinition>
    {
        new()
        {
            Profile = TweakProfile.Competitivo,
            Name = "Competitivo",
            Summary = "Prioridade máxima para jogos e resposta imediata de entrada.",
            Description = "Perfil pensado para jogos online competitivos: o processador mantém-se em frequência alta, " +
                          "a latência de entrada é reduzida ao mínimo e os processos de fundo perdem prioridade.",
            IconKind = "ControllerClassic",
            RecommendedFor = "Jogadores de Free Fire, Valorant, CS2 e Fortnite.",
            Adjustments = new[]
            {
                "Latência de entrada do rato (buffer reduzido e aceleração desativada)",
                "Prioridade de CPU ao processo em primeiro plano",
                "Desativação de serviços desnecessários (telemetria, fax, demo de loja)",
                "Rede: Nagle desativado e TCP otimizado para resposta",
                "Plano de energia de Alto Desempenho",
                "Gráficos: agendamento por hardware e Game DVR desativado",
                "Efeitos visuais do Windows reduzidos",
            },
            ActionKeys = new[]
            {
                "latency.input.mousedataqueue",
                "latency.input.acceleration",
                "win.foreground-boost",
                "latency.cpu.throttlemin",
                "latency.kernel.gamespriority",
                "latency.kernel.networkthrottling",
                "latency.gpu.gamedvr",
                "latency.display.fsebehavior",
                "win.game-mode",
                "win.visual-effects",
                "svc.diagtrack",
                "svc.dmwappush",
            },
        },
        new()
        {
            Profile = TweakProfile.Streaming,
            Name = "Streaming",
            Summary = "Gravação e transmissão estáveis, com o jogo a manter prioridade.",
            Description = "Perfil para quem transmite ou grava: mantém o jogo prioritário mas reserva recursos " +
                          "para o codificador, e evita interrupções de rede e de disco durante a emissão.",
            IconKind = "Video",
            RecommendedFor = "Criadores de conteúdo (OBS, Streamlabs) e transmissões em direto.",
            Adjustments = new[]
            {
                "Prioridade elevada ao programa em primeiro plano (jogo)",
                "MMCSS configurado para manter o codificador sem cortes",
                "Rede TCP otimizada (estabilidade de upload)",
                "Sensor de Armazenamento ligado (evita encher o disco durante a emissão)",
                "Efeitos visuais reduzidos (menos GPU para a interface)",
                "Telemetria reduzida (menos tráfego em segundo plano)",
            },
            ActionKeys = new[]
            {
                "win.foreground-boost",
                "latency.kernel.systemresponsiveness",
                "latency.kernel.networkthrottling",
                "latency.gpu.gamedvr",
                "disk.storage-sense",
                "win.visual-effects",
                "privacy.telemetry-min",
                "svc.diagtrack",
            },
        },
        new()
        {
            Profile = TweakProfile.Balanceado,
            Name = "Balanceado",
            Summary = "Ganhos reais no dia a dia, sem alterar comportamentos essenciais.",
            Description = "Perfil recomendado para a maioria dos utilizadores: melhora o arranque, o disco e a " +
                          "privacidade, mantendo intactas as funcionalidades do Windows que as pessoas usam.",
            IconKind = "ScaleBalance",
            RecommendedFor = "Uso geral, escritório, estudo e jogos ocasionais.",
            Adjustments = new[]
            {
                "Otimização de disco e limpeza automática",
                "Privacidade: telemetria mínima, ID de publicidade desativado",
                "Serviços secundários em arranque manual",
                "Efeitos visuais equilibrados",
                "Windows Update com otimização de entrega limitada à LAN",
            },
            ActionKeys = new[]
            {
                "win.visual-effects",
                "win.menu-delay",
                "win.ntfs-last-access",
                "disk.storage-sense",
                "disk.storage-sense-days",
                "privacy.telemetry-min",
                "privacy.advertising-id",
                "privacy.activity-history",
                "privacy.advertising-suggestions",
                "svc.diagtrack",
                "svc.mapsbroker",
                "wu.delivery-optimization",
            },
        },
        new()
        {
            Profile = TweakProfile.Extremo,
            Name = "Extremo",
            Summary = "Desempenho máximo — inclui alterações agressivas e reversíveis.",
            Description = "Perfil para quem quer extrair cada milissegundo: desativa indexação, telemetria, " +
                          "prefetch em SSD, hibernação e gravação em segundo plano, e ativa o plano de energia " +
                          "de Desempenho Máximo (quando disponível).",
            IconKind = "Flash",
            RecommendedFor = "Benchmarks, competições e máquinas dedicadas a jogos.",
            Adjustments = new[]
            {
                "Tudo o que o perfil Competitivo faz",
                "Indexação de ficheiros desativada (Windows Search)",
                "Prefetch/Superfetch desativado (apenas SSD)",
                "Hibernação desligada (liberta espaço equivalente à RAM)",
                "Plano de energia Desempenho Máximo (desbloqueado se necessário)",
                "Cortana e pesquisa na Web desativadas",
                "Atualizações de funcionalidades adiadas 30 dias",
            },
            ActionKeys = new[]
            {
                "latency.input.mousedataqueue",
                "latency.input.acceleration",
                "win.foreground-boost",
                "latency.cpu.throttlemin",
                "latency.kernel.gamespriority",
                "latency.kernel.networkthrottling",
                "latency.gpu.hwschmode",
                "latency.gpu.gamedvr",
                "latency.display.fsebehavior",
                "win.prefetch-ssd",
                "svc.wsearch",
                "svc.diagtrack",
                "svc.dmwappush",
                "svc.retaildemo",
                "svc.fax",
                "privacy.telemetry-min",
                "privacy.cortana",
                "privacy.search-web",
                "disk.hibernate-off",
                "wu.defer-feature-updates",
            },
            Warning = "Este perfil altera funcionalidades do Windows que alguns utilizadores usam. " +
                      "O sistema cria um ponto de restauro antes de aplicar e todas as alterações podem ser revertidas em " +
                      "«Otimizações → Reverter tudo».",
        },
    };

    public static TweakProfileDefinition Get(TweakProfile profile)
        => All.FirstOrDefault(p => p.Profile == profile) ?? All[2];

    /// <summary>Lista de descrições do que será ajustado (mostrada acima do botão Aplicar Perfil).</summary>
    public static IReadOnlyList<string> DescribeAdjustments(TweakProfile profile) => Get(profile).Adjustments;
}
