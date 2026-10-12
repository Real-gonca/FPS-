using HLOptimizerPro.Core.Models;

namespace HLOptimizerPro.Core.Services;

/// <summary>
/// Catálogo de aplicações e jogos conhecidos (App Tuner + Game Mode + PC Boost).
///
/// Como a deteção funciona: cada perfil lista os nomes de processo reais e as chaves de registo
/// de desinstalação onde o programa se regista. A app só aparece como "Instalado" quando UMA
/// destas verificações dá positivo — não mostramos lista fixa de "app que talvez tenha".
///
/// Sobre as prioridades: "High" é o máximo que aplicamos. Valores mais altos (Realtime) bloqueiam
/// o teclado e o rato em alguns sistemas — não vale o risco para um ganho não mensurável.
/// </summary>
public static class AppCatalog
{
    /// <summary>Aplicações de uso geral otimizáveis (3.4c).</summary>
    public static IReadOnlyList<AppOptimizationProfile> Applications { get; } = new List<AppOptimizationProfile>
    {
        new()
        {
            AppKey = "discord",
            DisplayName = "Discord",
            ProcessNames = new[] { "Discord" },
            RegistryNames = new[] { "Discord" },
            DetectionHint = "Discord",
            IconGlyph = "\uE7FC",
            ProcessPriority = "AboveNormal",
            PreferredCores = 0,
            PowerPlanGuid = null,
            Notes = "Prioridade de CPU ligeiramente elevada para manter a voz estável durante o jogo.",
            ExpectedGain = "Menos cortes de áudio e sobreposição mais responsiva.",
        },
        new()
        {
            AppKey = "chrome",
            DisplayName = "Google Chrome",
            ProcessNames = new[] { "chrome" },
            RegistryNames = new[] { "Google Chrome" },
            DetectionHint = "Google\\Chrome",
            IconGlyph = "\uE774",
            ProcessPriority = "Normal",
            PreferredCores = 0,
            Notes = "Processos de renderização mantidos em prioridade normal; separadores inativos reduzem uso de RAM.",
            ExpectedGain = "Navegação fluida sem competir com o jogo.",
        },
        new()
        {
            AppKey = "edge",
            DisplayName = "Microsoft Edge",
            ProcessNames = new[] { "msedge" },
            RegistryNames = new[] { "Microsoft Edge" },
            DetectionHint = "Microsoft\\Edge",
            IconGlyph = "\uE773",
            ProcessPriority = "Normal",
            Notes = "Prioridade normal e desativação do preload de arranque do Edge.",
            ExpectedGain = "Menos memória ocupada em segundo plano.",
        },
        new()
        {
            AppKey = "firefox",
            DisplayName = "Mozilla Firefox",
            ProcessNames = new[] { "firefox" },
            RegistryNames = new[] { "Mozilla Firefox" },
            DetectionHint = "Mozilla Firefox",
            IconGlyph = "\uE773",
            ProcessPriority = "Normal",
            Notes = "Prioridade normal, com afinidade opcional nos últimos núcleos.",
            ExpectedGain = "Navegação estável com o jogo em primeiro plano.",
        },
        new()
        {
            AppKey = "steam",
            DisplayName = "Steam",
            ProcessNames = new[] { "steam", "steamwebhelper" },
            RegistryNames = new[] { "Steam" },
            DetectionHint = "Steam",
            IconGlyph = "\uE7FC",
            ProcessPriority = "AboveNormal",
            Notes = "O cliente Steam recebe prioridade alta; as descargas em segundo plano continuam a funcionar.",
            ExpectedGain = "Downloads e atualizações sem afetar o jogo.",
        },
        new()
        {
            AppKey = "obs",
            DisplayName = "OBS Studio",
            ProcessNames = new[] { "obs64", "obs32" },
            RegistryNames = new[] { "OBS Studio" },
            DetectionHint = "obs-studio",
            IconGlyph = "\uE7FC",
            ProcessPriority = "AboveNormal",
            PreferredCores = 0,
            EnableWindowsGameMode = false,
            Notes = "Prioridade alta e quantos mais núcleos melhor — a codificação usa muitos threads.",
            ExpectedGain = "Menos frames perdidos na gravação/streaming.",
        },
        new()
        {
            AppKey = "spotify",
            DisplayName = "Spotify",
            ProcessNames = new[] { "Spotify" },
            RegistryNames = new[] { "Spotify" },
            DetectionHint = "Spotify",
            IconGlyph = "\uE8D6",
            ProcessPriority = "BelowNormal",
            Notes = "Executado com prioridade baixa: a música não compete com jogos ou trabalho.",
            ExpectedGain = "Uso de CPU quase nulo durante o jogo.",
        },
        new()
        {
            AppKey = "vlc",
            DisplayName = "VLC Media Player",
            ProcessNames = new[] { "vlc" },
            RegistryNames = new[] { "VLC media player" },
            DetectionHint = "VideoLAN",
            IconGlyph = "\uE8B2",
            ProcessPriority = "Normal",
            Notes = "Prioridade normal e aceleração de hardware mantida.",
            ExpectedGain = "Reprodução suave sem sobrecarregar a CPU.",
        },
    };

    /// <summary>Biblioteca de jogos incluída (3.4e) — deteção automática por processo.</summary>
    public static IReadOnlyList<AppOptimizationProfile> Games { get; } = new List<AppOptimizationProfile>
    {
        new()
        {
            AppKey = "freefire",
            DisplayName = "Free Fire",
            ProcessNames = new[] { "FreeFire", "GarenaFreeFire", "Free Fire" },
            RegistryNames = new[] { "Free Fire", "Garena" },
            IconGlyph = "\uE7FC",
            ProcessPriority = "High",
            EnableWindowsGameMode = true,
            PowerPlanGuid = PowerPlanInfo.HighPerformance,
            IsGame = true,
            Notes = "Prioridade alta e plano de energia de alto desempenho enquanto o emulador/jogo estiver aberto.",
            ExpectedGain = "Menos quedas de FPS nos momentos de maior ação.",
        },
        new()
        {
            AppKey = "gta5",
            DisplayName = "Grand Theft Auto V",
            ProcessNames = new[] { "GTA5", "GTA5_Enhanced", "PlayGTAV", "RAGE" },
            RegistryNames = new[] { "Grand Theft Auto V", "GTAV" },
            IconGlyph = "\uE7FC",
            ProcessPriority = "High",
            EnableWindowsGameMode = true,
            PowerPlanGuid = PowerPlanInfo.HighPerformance,
            IsGame = true,
            Notes = "Prioridade alta do processo principal; serviços e launchers em segundo plano são reduzidos.",
            ExpectedGain = "Streaming de mapa mais estável e menos stutter ao conduzir.",
        },
        new()
        {
            AppKey = "fortnite",
            DisplayName = "Fortnite",
            ProcessNames = new[] { "FortniteClient-Win64-Shipping", "FortniteLauncher", "EpicGamesLauncher" },
            RegistryNames = new[] { "Fortnite" },
            IconGlyph = "\uE7FC",
            ProcessPriority = "High",
            EnableWindowsGameMode = true,
            PowerPlanGuid = PowerPlanInfo.HighPerformance,
            IsGame = true,
            Notes = "Prioridade alta e modo de jogo do Windows; o launcher Epic é reduzido depois de abrir o jogo.",
            ExpectedGain = "FPS mais consistentes e menor latência de entrada.",
        },
        new()
        {
            AppKey = "valorant",
            DisplayName = "VALORANT",
            ProcessNames = new[] { "VALORANT-Win64-Shipping", "RiotClientServices", "VALORANT" },
            RegistryNames = new[] { "VALORANT", "Riot Vanguard" },
            IconGlyph = "\uE7FC",
            ProcessPriority = "High",
            EnableWindowsGameMode = true,
            PowerPlanGuid = PowerPlanInfo.HighPerformance,
            IsGame = true,
            Notes = "Prioridade alta. O Vanguard (anti-cheat) NUNCA é tocado — é um componente de segurança.",
            ExpectedGain = "Menor latência de disparo e resposta consistente.",
        },
        new()
        {
            AppKey = "roblox",
            DisplayName = "Roblox",
            ProcessNames = new[] { "RobloxPlayerBeta", "RobloxPlayerLauncher", "RobloxStudioBeta" },
            RegistryNames = new[] { "Roblox" },
            IconGlyph = "\uE7FC",
            ProcessPriority = "High",
            EnableWindowsGameMode = true,
            PowerPlanGuid = PowerPlanInfo.HighPerformance,
            IsGame = true,
            Notes = "Prioridade alta do cliente; o Roblox Studio é ignorado durante o jogo.",
            ExpectedGain = "Menos travamentos ao carregar cenários.",
        },
        new()
        {
            AppKey = "cs2",
            DisplayName = "Counter-Strike 2",
            ProcessNames = new[] { "cs2", "csgo" },
            RegistryNames = new[] { "Counter-Strike 2", "Counter-Strike" },
            IconGlyph = "\uE7FC",
            ProcessPriority = "High",
            EnableCpuAffinity = true,
            PreferredCores = 0,
            EnableWindowsGameMode = true,
            PowerPlanGuid = PowerPlanInfo.HighPerformance,
            IsGame = true,
            Notes = "Prioridade alta, processo afinado e latência de rede priorizada (ver módulo Latência).",
            ExpectedGain = "Tick rate estável e menor latência de rede em partidas competitivas.",
        },
        new()
        {
            AppKey = "warzone",
            DisplayName = "Call of Duty: Warzone",
            ProcessNames = new[] { "ModernWarfare", "cod", "CallOfDuty" },
            RegistryNames = new[] { "Call of Duty" },
            IconGlyph = "\uE7FC",
            ProcessPriority = "High",
            EnableWindowsGameMode = true,
            PowerPlanGuid = PowerPlanInfo.HighPerformance,
            IsGame = true,
            Notes = "Prioridade alta e VRAM gerida preferencialmente para o jogo.",
            ExpectedGain = "Menos oscilações de frames em zonas densas.",
        },
    };

    /// <summary>Processos considerados "de fundo" e reduzidos durante o Modo Jogo.</summary>
    public static IReadOnlyList<string> BackgroundProcessNames { get; } = new[]
    {
        "OneDrive", "Dropbox", "GoogleDriveFS", "MEGAsync", "Telegram", "WhatsApp",
        "Spotify", "iTunes", "Adobe Desktop Service", "Creative Cloud", "Acrobat",
        "Discord", "Teams", "ms-teams", "Zoom", "Skype", "slack", "BlueStacks", "Nox",
        "vmware-vmx", "VirtualBoxVM", "EpicGamesLauncher", "GalaxyClient", "UbisoftConnect",
        "Origin", "EADesktop", "Battle.net", "steamwebhelper", "RiotClientServices",
        "ccleaner", "YourPhone", "PhoneExperienceHost", "SearchApp",
    };

    /// <summary>Programas populares mostrados na tela PC Boost para otimização rápida.</summary>
    public static IReadOnlyList<AppOptimizationProfile> BoostTargets { get; }
        = Applications.Concat(Games).ToList();

    /// <summary>Devolve todos os perfis (apps + jogos).</summary>
    public static IReadOnlyList<AppOptimizationProfile> All() => BoostTargets;

    public static AppOptimizationProfile? Find(string appKey)
        => All().FirstOrDefault(a => string.Equals(a.AppKey, appKey, StringComparison.OrdinalIgnoreCase));
}
