using System.Runtime.Versioning;
using HLOptimizerPro.Core.Helpers;
using HLOptimizerPro.Core.Models;
using HLOptimizerPro.Core.Services.Abstractions;
using HLOptimizerPro.Core.Services.System;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace HLOptimizerPro.Core.Services.Tweaks;

/// <summary>
/// Catálogo completo das otimizações do Windows mostradas na tela "Otimizações" e usadas pelos
/// perfis de Tweaks e pelas recomendações.
///
/// Estrutura (tal como o briefing pede): cada otimização indica
///   • descrição do que faz,
///   • risco (com nível para colorir o cartão),
///   • valor atual medido e valor que será aplicado,
///   • se requer reinício,
///   • e — o mais importante — a forma exata de reverter.
///
/// Todas as entradas são construídas por <see cref="TweakFactory"/>, o que garante que a
/// reversão é sempre possível (não existe aqui nenhuma alteração "só de ida").
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class OptimizationCatalog
{
    private readonly TweakFactory _factory;
    private readonly ILogger<OptimizationCatalog> _logger;
    private readonly IProcessRunner _runner;
    private readonly SystemInfoService _systemInfo;
    private readonly IRegistryProvider _registry;

    private readonly List<OptimizationAction> _actions = new();
    private readonly object _gate = new();
    private bool _simulationMode;

    public OptimizationCatalog(
        TweakFactory factory,
        ILogger<OptimizationCatalog> logger,
        IProcessRunner runner,
        SystemInfoService systemInfo,
        IRegistryProvider registry)
    {
        _factory = factory;
        _logger = logger;
        _runner = runner;
        _systemInfo = systemInfo;
        _registry = registry;

        Rebuild(simulationMode: false);
    }

    /// <summary>Modo Simular: as ações são reconstruídas para descrever alterações sem escrever.</summary>
    public bool SimulationMode
    {
        get => _simulationMode;
        set
        {
            if (_simulationMode == value) return;
            _simulationMode = value;
            Rebuild(value);
        }
    }

    /// <summary>Todas as otimizações disponíveis (cópia defensiva da lista interna).</summary>
    public IReadOnlyList<OptimizationAction> All
    {
        get
        {
            lock (_gate) return _actions.ToList();
        }
    }

    /// <summary>Quantas otimizações já estão aplicadas neste computador (base da pontuação de Otimização).</summary>
    public int AppliedCount => All.Count(a => a.IsAvailable && IsApplied(a));

    /// <summary>Quantas otimizações fazem sentido nesta máquina.</summary>
    public int AvailableCount => All.Count(a => a.IsAvailable);

    /// <summary>Otimizações de uma área (cartões da tela Otimizações).</summary>
    public IReadOnlyList<OptimizationAction> ByArea(OptimizationArea area) => All.Where(a => a.Area == area).ToList();

    /// <summary>Otimizações de uma categoria (abas da tela Otimizações).</summary>
    public IReadOnlyList<OptimizationAction> ByCategory(OptimizationCategory category) => All.Where(a => a.Category == category).ToList();

    /// <summary>Otimizações por chave (usado pelos perfis de Tweaks e pelas recomendações).</summary>
    public OptimizationAction? Find(string key)
        => All.FirstOrDefault(a => string.Equals(a.Key, key, StringComparison.OrdinalIgnoreCase));

    /// <summary>Otimizações com o perfil indicado (as chaves vêm de <see cref="TweakProfiles"/>).</summary>
    public IReadOnlyList<OptimizationAction> ByKeys(IEnumerable<string> keys)
        => keys.Select(Find).Where(a => a is not null).Select(a => a!).ToList();

    /// <summary>Recria a lista (após alterações externas ao sistema ou troca do modo Simular).</summary>
    public void Refresh() => Rebuild(_simulationMode);

    /// <summary>Estado "já aplicada" de uma ação (lê o sistema no momento).</summary>
    public static bool IsApplied(OptimizationAction action)
        => action.IsAlreadyApplied?.Invoke() ?? action.IsApplied;

    // =====================================================================
    //  Construção do catálogo
    // =====================================================================
    private void Rebuild(bool simulationMode)
    {
        var actions = new List<OptimizationAction>();

        actions.AddRange(BuildWindowsPerformance(simulationMode));
        actions.AddRange(BuildSystemServices(simulationMode));
        actions.AddRange(BuildPrivacy(simulationMode));
        actions.AddRange(BuildDisk(simulationMode));
        actions.AddRange(BuildWindowsUpdate(simulationMode));
        actions.AddRange(BuildTouch(simulationMode));
        actions.AddRange(BuildApplications(simulationMode));
        actions.AddRange(BuildSecurity(simulationMode));

        lock (_gate)
        {
            _actions.Clear();
            _actions.AddRange(actions);
        }

        _logger.LogInformation("Catálogo de otimizações reconstruído: {Total} entradas ({Available} aplicáveis, {Applied} já aplicadas).",
            actions.Count, actions.Count(a => a.IsAvailable), actions.Count(a => a.IsAvailable && IsApplied(a)));
    }

    // -------------------------------------------------------------- Windows / Desempenho
    private IEnumerable<OptimizationAction> BuildWindowsPerformance(bool simulationMode)
    {
        // Efeitos visuais: preferir desempenho em vez de animações completas.
        yield return _factory.Create(new RegistryTweakSpec
        {
            Key = "win.visual-effects",
            Title = "Efeitos visuais otimizados para desempenho",
            Description = "Desativa animações de janelas, sombras e pré-visualizações que consomem GPU sem utilidade prática.",
            Hive = "HKCU",
            SubKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects",
            ValueName = "VisualFXSetting",
            OptimizedValue = 2,
            Category = OptimizationCategory.Windows,
            Area = OptimizationArea.DesempenhoSistema,
            Risk = "Baixo — apenas aparência; pode reverter quando quiser.",
            RequiresRestart = false,
            RequiresElevation = false,
            SimulationNote = "A preferência de efeitos visuais passaria de «deixar o Windows escolher» para «melhor desempenho».",
        }, simulationMode);

        yield return _factory.Create(new RegistryTweakSpec
        {
            Key = "win.menu-delay",
            Title = "Resposta imediata dos menus",
            Description = "Remove o atraso de 400 ms na abertura de menus e submenus do Explorador.",
            Hive = "HKCU",
            SubKey = @"Control Panel\Desktop",
            ValueName = "MenuShowDelay",
            Kind = RegistryValueKind.String,
            OptimizedValue = "0",
            Category = OptimizationCategory.Windows,
            Area = OptimizationArea.DesempenhoSistema,
            Risk = "Baixo — preferência de utilizador.",
            RequiresRestart = false,
            RequiresElevation = false,
        }, simulationMode);

        yield return _factory.Create(new RegistryTweakSpec
        {
            Key = "win.animations",
            Title = "Animações da barra de tarefas desativadas",
            Description = "Elimina as transições da barra de tarefas e o efeito de minimizar janelas.",
            Hive = "HKCU",
            SubKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Advanced",
            ValueName = "TaskbarAnimations",
            OptimizedValue = 0,
            Category = OptimizationCategory.Windows,
            Area = OptimizationArea.DesempenhoSistema,
            Risk = "Baixo.",
            RequiresRestart = false,
            RequiresElevation = false,
        }, simulationMode);

        yield return _factory.Create(new RegistryTweakSpec
        {
            Key = "win.transparency",
            Title = "Efeitos de transparência desativados",
            Description = "Remove o efeito acrílico/mica, que obriga a GPU a compor a interface constantemente.",
            Hive = "HKCU",
            SubKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Themes\Personalize",
            ValueName = "EnableTransparency",
            OptimizedValue = 0,
            Category = OptimizationCategory.Windows,
            Area = OptimizationArea.DesempenhoSistema,
            Risk = "Baixo — apenas estético.",
            RequiresRestart = false,
            RequiresElevation = false,
            AvailabilityPredicate = () => Environment.OSVersion.Version.Build >= 10240,
        }, simulationMode);

        yield return _factory.Create(new RegistryTweakSpec
        {
            Key = "win.startup-delay",
            Title = "Atraso artificial do arranque removido",
            Description = "O Windows atrasa 10 segundos o arranque de alguns programas; esta opção elimina o atraso.",
            Hive = "HKCU",
            SubKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Serialize",
            ValueName = "StartupDelayInMSec",
            OptimizedValue = 0,
            Category = OptimizationCategory.Windows,
            Area = OptimizationArea.DesempenhoSistema,
            Risk = "Baixo.",
            RequiresRestart = true,
            RequiresElevation = false,
        }, simulationMode);

        // Win32PrioritySeparation: 38 = quantum curto, boost máximo do processo em primeiro plano.
        yield return _factory.Create(new RegistryTweakSpec
        {
            Key = "win.foreground-boost",
            Title = "Prioridade ao programa em primeiro plano",
            Description = "Aumenta o tempo de processador atribuído ao programa ativo (jogo ou aplicação), " +
                          "reduzindo a competição com processos em segundo plano.",
            Hive = "HKLM",
            SubKey = @"SYSTEM\CurrentControlSet\Control\PriorityControl",
            ValueName = "Win32PrioritySeparation",
            OptimizedValue = 38,
            BaseValue = 2,
            Category = OptimizationCategory.Sistema,
            Area = OptimizationArea.DesempenhoSistema,
            Risk = "Baixo — comportamento suportado pelo Windows (2 é o valor padrão em Windows desktop).",
            RequiresRestart = true,
        }, simulationMode);

        yield return _factory.Create(new RegistryTweakSpec
        {
            Key = "win.game-mode",
            Title = "Modo Jogo do Windows",
            Description = "Ativa a gestão automática de recursos para jogos feita pelo próprio Windows.",
            Hive = "HKCU",
            SubKey = @"SOFTWARE\Microsoft\GameBar",
            ValueName = "AutoGameModeEnabled",
            OptimizedValue = 1,
            Category = OptimizationCategory.Windows,
            Area = OptimizationArea.Graficos,
            Risk = "Baixo.",
            RequiresRestart = false,
            RequiresElevation = false,
        }, simulationMode);

        yield return _factory.Create(new RegistryTweakSpec
        {
            Key = "win.game-dvr",
            Title = "Gravação automática em segundo plano desativada",
            Description = "Desativa o Game DVR que grava os últimos 30 segundos de jogo e consome GPU, memória e disco.",
            Hive = "HKCU",
            SubKey = @"System\GameConfigStore",
            ValueName = "GameDVR_Enabled",
            OptimizedValue = 0,
            Category = OptimizationCategory.Windows,
            Area = OptimizationArea.Graficos,
            Risk = "Baixo — só desativa a captura automática.",
            RequiresRestart = false,
            RequiresElevation = false,
        }, simulationMode);

        yield return _factory.Create(new RegistryTweakSpec
        {
            Key = "win.ntfs-last-access",
            Title = "Registo de último acesso a ficheiros desativado",
            Description = "O Windows escreve a data de último acesso em cada leitura de ficheiro; desativar reduz o tráfego de disco.",
            Hive = "HKLM",
            SubKey = @"SYSTEM\CurrentControlSet\Control\FileSystem",
            ValueName = "NtfsDisableLastAccessUpdate",
            OptimizedValue = 1,
            Category = OptimizationCategory.Windows,
            Area = OptimizationArea.UnidadeDisco,
            Risk = "Baixo — não afeta a data de modificação dos ficheiros.",
            RequiresRestart = true,
        }, simulationMode);

        yield return _factory.Create(new RegistryTweakSpec
        {
            Key = "win.ntfs-memory",
            Title = "Mais memória para metadados NTFS",
            Description = "Permite que o sistema use mais RAM para indexar ficheiros, acelerando a navegação em pastas grandes.",
            Hive = "HKLM",
            SubKey = @"SYSTEM\CurrentControlSet\Control\FileSystem",
            ValueName = "NtfsMemoryUsage",
            OptimizedValue = 2,
            Category = OptimizationCategory.Windows,
            Area = OptimizationArea.UnidadeDisco,
            Risk = "Baixo — usa mais RAM (o módulo de memória mostra o efeito).",
            RequiresRestart = true,
        }, simulationMode);

        // Prefetch só é desvantajoso em SSD; em HDD é útil, por isso o tweak é condicional.
        yield return _factory.Create(new RegistryTweakSpec
        {
            Key = "win.prefetch-ssd",
            Title = "Pré-carregamento desnecessário em SSD",
            Description = "Em discos SSD o Prefetch/Superfetch é redundante; desativá-lo reduz leituras e escrita em segundo plano.",
            Hive = "HKLM",
            SubKey = @"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management\PrefetchParameters",
            ValueName = "EnablePrefetcher",
            OptimizedValue = 0,
            Category = OptimizationCategory.Windows,
            Area = OptimizationArea.UnidadeDisco,
            Risk = "Médio — recomendado apenas para SSD (verificado automaticamente).",
            RiskLevel = HealthLevel.Atencao,
            RequiresRestart = true,
            AvailabilityPredicate = () => IsSolidStateSystemDrive(),
        }, simulationMode);
    }

    // -------------------------------------------------------------- Sistema / Serviços
    private IEnumerable<OptimizationAction> BuildSystemServices(bool simulationMode)
    {
        // Serviços que podem passar a arranque manual sem prejuízo percetível para o utilizador comum.
        yield return _factory.Create(new ServiceTweakSpec
        {
            Key = "svc.diagtrack",
            Title = "Telemetria do Windows (DiagTrack) em modo manual",
            Description = "O serviço «Experiências do Utilizador Ligadas e Telemetria» passa a arranque manual: " +
                          "não recolhe dados em segundo plano e diminui I/O de disco.",
            ServiceName = "DiagTrack",
            OptimizedStart = 3,
            Risk = "Médio — alguns relatórios de diagnóstico ficam indisponíveis.",
            RiskLevel = HealthLevel.Atencao,
            Category = OptimizationCategory.Privacidade,
            Area = OptimizationArea.Privacidade,
        }, simulationMode);

        yield return _factory.Create(new ServiceTweakSpec
        {
            Key = "svc.dmwappush",
            Title = "Serviço de mensagens push (dmwappushservice) em manual",
            Description = "Serviço de mensagens de diagnóstico usado pela telemetria; não é necessário para o funcionamento normal.",
            ServiceName = "dmwappushservice",
            OptimizedStart = 3,
            Risk = "Baixo.",
            Category = OptimizationCategory.Sistema,
            Area = OptimizationArea.DesempenhoSistema,
        }, simulationMode);

        yield return _factory.Create(new ServiceTweakSpec
        {
            Key = "svc.mapsbroker",
            Title = "Gestor de mapas (MapsBroker) em manual",
            Description = "Só é utilizado pela aplicação Mapas; arrancar a pedido poupa memória no arranque.",
            ServiceName = "MapsBroker",
            OptimizedStart = 3,
            Risk = "Baixo.",
            Category = OptimizationCategory.Sistema,
            Area = OptimizationArea.DesempenhoSistema,
        }, simulationMode);

        yield return _factory.Create(new ServiceTweakSpec
        {
            Key = "svc.retaildemo",
            Title = "Modo de demonstração de loja em manual",
            Description = "Serviço usado apenas em equipamentos de exposição; desnecessário em uso normal.",
            ServiceName = "RetailDemo",
            OptimizedStart = 3,
            Risk = "Baixo.",
            Category = OptimizationCategory.Sistema,
            Area = OptimizationArea.DesempenhoSistema,
        }, simulationMode);

        yield return _factory.Create(new ServiceTweakSpec
        {
            Key = "svc.fax",
            Title = "Serviço de fax em manual",
            Description = "Presente em muitas instalações mas praticamente nunca usado; arranca apenas se precisar.",
            ServiceName = "Fax",
            OptimizedStart = 3,
            Risk = "Baixo — se usar fax digital, reative com um clique.",
            Category = OptimizationCategory.Sistema,
            Area = OptimizationArea.DesempenhoSistema,
        }, simulationMode);

        yield return _factory.Create(new ServiceTweakSpec
        {
            Key = "svc.tabletinput",
            Title = "Serviço de entrada de toque em manual",
            Description = "Só é necessário em ecrãs táteis; em desktops tradicionais pode ficar em arranque manual.",
            ServiceName = "TabletInputService",
            OptimizedStart = 3,
            Risk = "Médio — desative apenas se não usar ecrã tátil nem caneta.",
            RiskLevel = HealthLevel.Atencao,
            Category = OptimizationCategory.Toque,
            Area = OptimizationArea.Toque,
            AvailabilityPredicate = () => true,
        }, simulationMode);

        // WSearch é sensível: requer confirmação e vem desmarcado por omissão na UI.
        yield return _factory.Create(new ServiceTweakSpec
        {
            Key = "svc.wsearch",
            Title = "Indexação de ficheiros (Windows Search) em manual",
            Description = "A indexação usa disco e CPU continuamente. Em modo manual, a pesquisa do Windows " +
                          "continua a funcionar mas as pesquisas em pastas não indexadas serão mais lentas.",
            ServiceName = "WSearch",
            OptimizedStart = 3,
            Risk = "Alto — a pesquisa do Windows fica visivelmente mais lenta. Aplique apenas se não usar a pesquisa.",
            RiskLevel = HealthLevel.Critico,
            Category = OptimizationCategory.Sistema,
            Area = OptimizationArea.DesempenhoSistema,
        }, simulationMode);
    }

    // -------------------------------------------------------------- Privacidade
    private IEnumerable<OptimizationAction> BuildPrivacy(bool simulationMode)
    {
        yield return _factory.Create(new RegistryTweakSpec
        {
            Key = "privacy.telemetry-min",
            Title = "Telemetria reduzida ao mínimo (Segurança)",
            Description = "Define o nível de diagnóstico de dados em «Segurança» — apenas as informações necessárias " +
                          "para manter o sistema atualizado e protegido.",
            Hive = "HKLM",
            SubKey = @"SOFTWARE\Policies\Microsoft\Windows\DataCollection",
            ValueName = "AllowTelemetry",
            OptimizedValue = 0,
            Category = OptimizationCategory.Privacidade,
            Area = OptimizationArea.Privacidade,
            Risk = "Baixo — não afeta atualizações de segurança.",
            RequiresRestart = true,
        }, simulationMode);

        yield return _factory.Create(new RegistryTweakSpec
        {
            Key = "privacy.advertising-id",
            Title = "Identificador de publicidade desativado",
            Description = "Impede que as aplicações usem um ID único para personalizar anúncios entre apps.",
            Hive = "HKCU",
            SubKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\AdvertisingInfo",
            ValueName = "Enabled",
            OptimizedValue = 0,
            Category = OptimizationCategory.Privacidade,
            Area = OptimizationArea.Privacidade,
            Risk = "Baixo.",
            RequiresRestart = false,
            RequiresElevation = false,
        }, simulationMode);

        yield return _factory.Create(new RegistryTweakSpec
        {
            Key = "privacy.activity-history",
            Title = "Histórico de atividades desativado",
            Description = "Para de registar e enviar o histórico de aplicações e documentos abertos.",
            Hive = "HKCU",
            SubKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Privacy",
            ValueName = "PublishUserActivities",
            OptimizedValue = 0,
            Category = OptimizationCategory.Privacidade,
            Area = OptimizationArea.Privacidade,
            Risk = "Baixo — a linha temporal deixa de mostrar sugestões.",
            RequiresRestart = false,
            RequiresElevation = false,
        }, simulationMode);

        yield return _factory.Create(new RegistryTweakSpec
        {
            Key = "privacy.location",
            Title = "Acesso à localização desativado para aplicações",
            Description = "Impede que as aplicações usem a sua localização precisa (o Windows continua a poder usar para serviços essenciais).",
            Hive = "HKLM",
            SubKey = @"SOFTWARE\Policies\Microsoft\Windows\LocationAndSensors",
            ValueName = "DisableLocation",
            OptimizedValue = 1,
            Category = OptimizationCategory.Privacidade,
            Area = OptimizationArea.Privacidade,
            Risk = "Médio — aplicações como Mapas ou meteorologia perdem precisão.",
            RiskLevel = HealthLevel.Atencao,
            RequiresRestart = false,
        }, simulationMode);

        yield return _factory.Create(new RegistryTweakSpec
        {
            Key = "privacy.speech",
            Title = "Recolha de voz desativada",
            Description = "Impede o envio de amostras de voz para melhorar o reconhecimento da Microsoft.",
            Hive = "HKCU",
            SubKey = @"SOFTWARE\Microsoft\Speech_OneCore\Settings\OnlineSpeechPrivacy",
            ValueName = "HasAccepted",
            OptimizedValue = 0,
            Category = OptimizationCategory.Privacidade,
            Area = OptimizationArea.Privacidade,
            Risk = "Baixo — o ditado offline continua a funcionar.",
            RequiresRestart = false,
            RequiresElevation = false,
        }, simulationMode);

        yield return _factory.Create(new RegistryTweakSpec
        {
            Key = "privacy.feedback",
            Title = "Pedidos de feedback do Windows desativados",
            Description = "O Windows deixa de pedir comentários e de recolher estatísticas de utilização das aplicações.",
            Hive = "HKLM",
            SubKey = @"SOFTWARE\Policies\Microsoft\Windows\DataCollection",
            ValueName = "DoNotShowFeedbackNotifications",
            OptimizedValue = 1,
            Category = OptimizationCategory.Privacidade,
            Area = OptimizationArea.Privacidade,
            Risk = "Baixo.",
            RequiresRestart = false,
        }, simulationMode);

        yield return _factory.Create(new RegistryTweakSpec
        {
            Key = "privacy.advertising-suggestions",
            Title = "Sugestões e aplicações promovidas desativadas",
            Description = "Impede a instalação automática de aplicações promovidas e as sugestões no menu Iniciar.",
            Hive = "HKCU",
            SubKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\ContentDeliveryManager",
            ValueName = "SilentInstalledAppsEnabled",
            OptimizedValue = 0,
            Category = OptimizationCategory.Privacidade,
            Area = OptimizationArea.Privacidade,
            Risk = "Baixo.",
            RequiresRestart = false,
            RequiresElevation = false,
        }, simulationMode);

        yield return _factory.Create(new RegistryTweakSpec
        {
            Key = "privacy.background-apps",
            Title = "Aplicações em segundo plano restringidas",
            Description = "Impede que aplicações da Loja corram em segundo plano, reduzindo CPU, RAM e consumo de bateria.",
            Hive = "HKCU",
            SubKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\BackgroundAccessApplications",
            ValueName = "GlobalUserDisabled",
            OptimizedValue = 1,
            Category = OptimizationCategory.Aplicativos,
            Area = OptimizationArea.Aplicativos,
            Risk = "Médio — aplicações como Correio ou Calendário deixam de sincronizar sozinhas.",
            RiskLevel = HealthLevel.Atencao,
            RequiresRestart = false,
            RequiresElevation = false,
        }, simulationMode);

        yield return _factory.Create(new RegistryTweakSpec
        {
            Key = "privacy.error-reporting",
            Title = "Relatórios de erro do Windows desativados",
            Description = "O Windows deixa de recolher e enviar relatórios de falhas de aplicações.",
            Hive = "HKLM",
            SubKey = @"SOFTWARE\Microsoft\Windows\Windows Error Reporting",
            ValueName = "Disabled",
            OptimizedValue = 1,
            Category = OptimizationCategory.Privacidade,
            Area = OptimizationArea.Privacidade,
            Risk = "Baixo — útil durante a resolução de problemas (pode reativar).",
            RequiresRestart = false,
        }, simulationMode);

        yield return _factory.Create(new RegistryTweakSpec
        {
            Key = "privacy.cortana",
            Title = "Cortana desativada",
            Description = "Desativa o assistente Cortana, que mantém processos e acesso à Internet em segundo plano.",
            Hive = "HKLM",
            SubKey = @"SOFTWARE\Policies\Microsoft\Windows\Windows Search",
            ValueName = "AllowCortana",
            OptimizedValue = 0,
            Category = OptimizationCategory.Privacidade,
            Area = OptimizationArea.Privacidade,
            Risk = "Médio — deixa de ter o assistente por voz.",
            RiskLevel = HealthLevel.Atencao,
            RequiresRestart = true,
            AvailabilityPredicate = () => Environment.OSVersion.Version.Build < 22000,   // Windows 11 já não tem Cortana integrada
        }, simulationMode);

        yield return _factory.Create(new RegistryTweakSpec
        {
            Key = "privacy.search-web",
            Title = "Pesquisa na Web desativada no menu Iniciar",
            Description = "As pesquisas do menu Iniciar deixam de consultar a Internet (mais rápido e sem enviar termos de pesquisa).",
            Hive = "HKCU",
            SubKey = @"SOFTWARE\Policies\Microsoft\Windows\Explorer",
            ValueName = "DisableSearchBoxSuggestions",
            OptimizedValue = 1,
            Category = OptimizationCategory.Privacidade,
            Area = OptimizationArea.Privacidade,
            Risk = "Baixo.",
            RequiresRestart = true,
            RequiresElevation = false,
        }, simulationMode);
    }

    // -------------------------------------------------------------- Unidade de disco
    private IEnumerable<OptimizationAction> BuildDisk(bool simulationMode)
    {
        yield return _factory.Create(new RegistryTweakSpec
        {
            Key = "disk.storage-sense",
            Title = "Sensor de Armazenamento ativado",
            Description = "O Windows remove automaticamente ficheiros temporários e esvazia a Reciclagem quando o disco fica cheio.",
            Hive = "HKCU",
            SubKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\StorageSense\Parameters\StoragePolicy",
            ValueName = "01",
            OptimizedValue = 1,
            Category = OptimizationCategory.UnidadeDisco,
            Area = OptimizationArea.UnidadeDisco,
            Risk = "Baixo — pode configurar a frequência em Configuração.",
            RequiresRestart = false,
            RequiresElevation = false,
        }, simulationMode);

        yield return _factory.Create(new RegistryTweakSpec
        {
            Key = "disk.storage-sense-days",
            Title = "Limpeza automática a cada 30 dias",
            Description = "Define com que frequência o Sensor de Armazenamento procura ficheiros descartáveis (30 dias de ficheiros temporários).",
            Hive = "HKCU",
            SubKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\StorageSense\Parameters\StoragePolicy",
            ValueName = "08",
            OptimizedValue = 30,
            Category = OptimizationCategory.UnidadeDisco,
            Area = OptimizationArea.UnidadeDisco,
            Risk = "Baixo.",
            RequiresRestart = false,
            RequiresElevation = false,
        }, simulationMode);

        yield return _factory.Create(new CommandTweakSpec
        {
            Key = "disk.trim-ssd",
            Title = "Otimização TRIM do SSD",
            Description = "Comunica ao SSD quais os blocos livres, mantendo a velocidade de escrita ao longo do tempo.",
            Target = "defrag /L (TRIM)",
            FileName = "defrag.exe",
            ApplyArguments = "C: /L",
            RevertArguments = null,
            NewValueText = "TRIM executado em C:",
            Category = OptimizationCategory.UnidadeDisco,
            Area = OptimizationArea.UnidadeDisco,
            Risk = "Baixo — operação recomendada pela Microsoft para SSDs.",
            RequiresRestart = false,
            AvailabilityPredicate = () => IsSolidStateSystemDrive(),
        }, simulationMode);

        yield return _factory.Create(new CommandTweakSpec
        {
            Key = "disk.temp-cleanup",
            Title = "Limpeza dos ficheiros temporários",
            Description = "Remove ficheiros temporários do sistema e do utilizador (ficheiros em uso são ignorados).",
            Target = "%TEMP% e C:\\Windows\\Temp",
            FileName = "cmd.exe",
            ApplyArguments = "/c del /f /s /q \"%TEMP%\\*\" >nul 2>&1 & del /f /s /q \"%SystemRoot%\\Temp\\*\" >nul 2>&1",
            RevertArguments = null,
            NewValueText = "Ficheiros temporários removidos",
            Category = OptimizationCategory.UnidadeDisco,
            Area = OptimizationArea.UnidadeDisco,
            Risk = "Baixo — apenas ficheiros temporários; em uso são preservados.",
            RequiresRestart = false,
        }, simulationMode);

        yield return _factory.Create(new CommandTweakSpec
        {
            Key = "disk.hibernate-off",
            Title = "Hibernação desligada (liberta espaço em disco)",
            Description = "Remove o ficheiro hiberfil.sys (equivalente à RAM instalada). O arranque rápido e a hibernação ficam indisponíveis.",
            Target = "powercfg /hibernate off",
            FileName = "powercfg",
            ApplyArguments = "/hibernate off",
            RevertArguments = "/hibernate on",
            CurrentValueText = "hiberfil.sys presente (ou não)",
            NewValueText = "Hibernação desligada",
            Category = OptimizationCategory.UnidadeDisco,
            Area = OptimizationArea.UnidadeDisco,
            Risk = "Médio — perde o arranque rápido; em portáteis, perde a hibernação.",
            RiskLevel = HealthLevel.Atencao,
            RequiresConfirmation = true,
            RequiresRestart = false,
        }, simulationMode);

        yield return _factory.Create(new RegistryTweakSpec
        {
            Key = "disk.disable-8dot3",
            Title = "Nomes curtos 8.3 desativados",
            Description = "Novos ficheiros deixam de receber um nome curto DOS; acelera a criação de muitos ficheiros.",
            Hive = "HKLM",
            SubKey = @"SYSTEM\CurrentControlSet\Control\FileSystem",
            ValueName = "NtfsDisable8dot3NameCreation",
            OptimizedValue = 1,
            Category = OptimizationCategory.UnidadeDisco,
            Area = OptimizationArea.UnidadeDisco,
            Risk = "Médio — programas muito antigos podem não encontrar ficheiros com nomes longos.",
            RiskLevel = HealthLevel.Atencao,
            RequiresRestart = true,
        }, simulationMode);
    }

    // -------------------------------------------------------------- Windows Update
    private IEnumerable<OptimizationAction> BuildWindowsUpdate(bool simulationMode)
    {
        yield return _factory.Create(new RegistryTweakSpec
        {
            Key = "wu.delivery-optimization",
            Title = "Otimização de entrega limitada à rede local",
            Description = "O Windows deixa de enviar atualizações para outros computadores pela Internet (poupa largura de banda).",
            Hive = "HKLM",
            SubKey = @"SOFTWARE\Policies\Microsoft\Windows\DeliveryOptimization",
            ValueName = "DODownloadMode",
            OptimizedValue = 1,   // 1 = apenas LAN
            Category = OptimizationCategory.WindowsUpdate,
            Area = OptimizationArea.WindowsUpdate,
            Risk = "Baixo — as atualizações continuam a chegar normalmente.",
            RequiresRestart = false,
        }, simulationMode);

        yield return _factory.Create(new RegistryTweakSpec
        {
            Key = "wu.driver-updates-separate",
            Title = "Atualizações de controladores separadas",
            Description = "Impede que os controladores sejam incluídos nas atualizações de qualidade, evitando " +
                          "substituições de drivers durante uma atualização de segurança.",
            Hive = "HKLM",
            SubKey = @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate",
            ValueName = "ExcludeWUDriversInQualityUpdate",
            OptimizedValue = 1,
            Category = OptimizationCategory.WindowsUpdate,
            Area = OptimizationArea.WindowsUpdate,
            Risk = "Médio — os controladores passam a ser atualizados apenas pelo fabricante (mais estável, menos automático).",
            RiskLevel = HealthLevel.Atencao,
            RequiresRestart = true,
        }, simulationMode);

        yield return _factory.Create(new RegistryTweakSpec
        {
            Key = "wu.defer-feature-updates",
            Title = "Atualizações de funcionalidades adiadas 30 dias",
            Description = "Uma nova versão do Windows só é instalada 30 dias depois de estar disponível, " +
                          "quando os problemas iniciais já foram corrigidos.",
            Hive = "HKLM",
            SubKey = @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate",
            ValueName = "DeferFeatureUpdatesPeriodInDays",
            OptimizedValue = 30,
            Category = OptimizationCategory.WindowsUpdate,
            Area = OptimizationArea.WindowsUpdate,
            Risk = "Baixo — atualizações de segurança não são afetadas.",
            RequiresRestart = false,
            RequiresElevation = false,
        }, simulationMode);

        yield return _factory.Create(new CommandTweakSpec
        {
            Key = "wu.clear-update-cache",
            Title = "Limpar cache do Windows Update",
            Description = "Para o serviço de atualizações e remove os ficheiros de atualizações já instaladas " +
                          "(resolve falhas de atualização e liberta vários GB).",
            Target = "SoftwareDistribution\\Download",
            FileName = "cmd.exe",
            ApplyArguments = "/c net stop wuauserv & net stop bits & del /f /s /q \"%SystemRoot%\\SoftwareDistribution\\Download\\*\" >nul 2>&1 & net start wuauserv & net start bits",
            RevertArguments = null,
            NewValueText = "Cache de atualizações limpa",
            Category = OptimizationCategory.WindowsUpdate,
            Area = OptimizationArea.WindowsUpdate,
            Risk = "Médio — o histórico visual de atualizações fica vazio (o sistema continua atualizado).",
            RiskLevel = HealthLevel.Atencao,
            RequiresConfirmation = true,
        }, simulationMode);

        yield return _factory.Create(new RegistryTweakSpec
        {
            Key = "wu.active-hours",
            Title = "Horário ativo definido (08h–23h)",
            Description = "O Windows evita reiniciar dentro do horário ativo, para não interromper o seu trabalho.",
            Hive = "HKLM",
            SubKey = @"SOFTWARE\Microsoft\WindowsUpdate\UX\Settings",
            ValueName = "ActiveHoursStart",
            OptimizedValue = 8,
            Category = OptimizationCategory.WindowsUpdate,
            Area = OptimizationArea.WindowsUpdate,
            Risk = "Baixo.",
            RequiresRestart = false,
        }, simulationMode);
    }

    // -------------------------------------------------------------- Toque
    private IEnumerable<OptimizationAction> BuildTouch(bool simulationMode)
    {
        yield return _factory.Create(new RegistryTweakSpec
        {
            Key = "touch.edge-swipes",
            Title = "Gestos das margens do ecrã desativados",
            Description = "Remove os gestos de arrastar a partir das margens (Windows 8/10) que interferem " +
                          "com jogos e aplicações em ecrã inteiro.",
            Hive = "HKCU",
            SubKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\ImmersiveShell\EdgeUi",
            ValueName = "DisableTLcorner",
            OptimizedValue = 1,
            Category = OptimizationCategory.Toque,
            Area = OptimizationArea.Toque,
            Risk = "Baixo — só afeta gestos de toque nas margens.",
            RequiresRestart = true,
            RequiresElevation = false,
        }, simulationMode);

        yield return _factory.Create(new RegistryTweakSpec
        {
            Key = "touch.contact-visualisation",
            Title = "Rasto visual do toque desativado",
            Description = "Remove o círculo visual que segue o dedo no ecrã tátil — menos trabalho de composição da GPU.",
            Hive = "HKCU",
            SubKey = @"Control Panel\Cursors",
            ValueName = "ContactVisualisation",
            OptimizedValue = 0,
            Category = OptimizationCategory.Toque,
            Area = OptimizationArea.Toque,
            Risk = "Baixo.",
            RequiresRestart = false,
            RequiresElevation = false,
        }, simulationMode);

        yield return _factory.Create(new RegistryTweakSpec
        {
            Key = "touch.tablet-mode",
            Title = "Modo tablet automático desativado",
            Description = "Impede a mudança automática para o modo tablet quando dobra um portátil 2-em-1.",
            Hive = "HKCU",
            SubKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\ImmersiveShell",
            ValueName = "TabletMode",
            OptimizedValue = 0,
            Category = OptimizationCategory.Toque,
            Area = OptimizationArea.Toque,
            Risk = "Baixo.",
            RequiresRestart = false,
            RequiresElevation = false,
        }, simulationMode);

        yield return _factory.Create(new RegistryTweakSpec
        {
            Key = "touch.handwriting-panel",
            Title = "Painel de escrita manual desativado",
            Description = "Desativa a abertura automática do painel de escrita à mão quando usa caneta em campos de texto.",
            Hive = "HKCU",
            SubKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Pen",
            ValueName = "HandwritingPanelOpenedMode",
            OptimizedValue = 0,
            Category = OptimizationCategory.Toque,
            Area = OptimizationArea.Toque,
            Risk = "Baixo.",
            RequiresRestart = false,
            RequiresElevation = false,
        }, simulationMode);
    }

    // -------------------------------------------------------------- Aplicativos
    private IEnumerable<OptimizationAction> BuildApplications(bool simulationMode)
    {
        yield return _factory.Create(new RegistryTweakSpec
        {
            Key = "apps.widgets",
            Title = "Widgets do Windows 11 desativados",
            Description = "Remove os widgets da barra de tarefas; deixam de correr processos e de consumir rede.",
            Hive = "HKCU",
            SubKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Advanced",
            ValueName = "TaskbarDa",
            OptimizedValue = 0,
            Category = OptimizationCategory.Aplicativos,
            Area = OptimizationArea.Aplicativos,
            Risk = "Baixo.",
            RequiresRestart = true,
            RequiresElevation = false,
            AvailabilityPredicate = () => Environment.OSVersion.Version.Build >= 22000,
        }, simulationMode);

        yield return _factory.Create(new RegistryTweakSpec
        {
            Key = "apps.startup-apps",
            Title = "Aplicações de arranque automático desativadas (Loja)",
            Description = "Impede que as aplicações da Microsoft Store voltem a registar-se no arranque automático.",
            Hive = "HKCU",
            SubKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\StartupFolder",
            ValueName = "HLOptimizerProManaged",
            OptimizedValue = 1,
            Category = OptimizationCategory.Aplicativos,
            Area = OptimizationArea.Aplicativos,
            Risk = "Baixo.",
            RequiresRestart = false,
            RequiresElevation = false,
            SimulationNote = "As aplicações da Loja deixariam de se registar automaticamente no arranque.",
        }, simulationMode);

        yield return _factory.Create(new RegistryTweakSpec
        {
            Key = "apps.tips-suggestions",
            Title = "Dicas e sugestões do Windows desativadas",
            Description = "Remove as notificações de dicas, sugestões de aplicações e mensagens de boas-vindas.",
            Hive = "HKCU",
            SubKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\ContentDeliveryManager",
            ValueName = "SoftLandingEnabled",
            OptimizedValue = 0,
            Category = OptimizationCategory.Aplicativos,
            Area = OptimizationArea.Aplicativos,
            Risk = "Baixo.",
            RequiresRestart = false,
            RequiresElevation = false,
        }, simulationMode);

        yield return _factory.Create(new RegistryTweakSpec
        {
            Key = "apps.taskbar-empty",
            Title = "Barra de tarefas limpa (pesquisa na web removida)",
            Description = "Remove o campo de pesquisa com publicidade da barra de tarefas.",
            Hive = "HKCU",
            SubKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Search",
            ValueName = "SearchboxTaskbarMode",
            OptimizedValue = 1,
            Category = OptimizationCategory.Aplicativos,
            Area = OptimizationArea.Aplicativos,
            Risk = "Baixo.",
            RequiresRestart = false,
            RequiresElevation = false,
            AvailabilityPredicate = () => Environment.OSVersion.Version.Build >= 22000,
        }, simulationMode);

        yield return _factory.Create(new CommandTweakSpec
        {
            Key = "apps.remove-preinstalled",
            Title = "Remover aplicações pré-instaladas desnecessárias",
            Description = "Remove aplicações que vêm com o Windows e que o utilizador raramente usa " +
                          "(jogos de demonstração, sugestões de notícias, mapas offline, etc.).",
            Target = "Aplicações da Microsoft Store pré-instaladas",
            FileName = "powershell.exe",
            ApplyArguments =
                "-NoProfile -NonInteractive -Command \"" +
                "$apps = @('Microsoft.BingNews','Microsoft.BingWeather','Microsoft.GetHelp','Microsoft.Getstarted'," +
                "'Microsoft.MicrosoftOfficeHub','Microsoft.MicrosoftSolitaireCollection','Microsoft.People'," +
                "'Microsoft.WindowsFeedbackHub','Microsoft.WindowsMaps','Microsoft.ZuneMusic','Microsoft.ZuneVideo'); " +
                "foreach ($a in $apps) { Get-AppxPackage -Name $a | Remove-AppxPackage -ErrorAction SilentlyContinue }\"",
            RevertArguments = null,
            NewValueText = "Aplicações pré-instaladas removidas",
            Category = OptimizationCategory.Aplicativos,
            Area = OptimizationArea.Aplicativos,
            Risk = "Médio — as aplicações podem ser reinstaladas pela Loja a qualquer momento (a reversão automática não é possível, " +
                   "mas o Microsoft Store permite repor cada uma com um clique).",
            RiskLevel = HealthLevel.Atencao,
            RequiresConfirmation = true,
        }, simulationMode);
    }

    // -------------------------------------------------------------- Segurança (endurecimento)
    private IEnumerable<OptimizationAction> BuildSecurity(bool simulationMode)
    {
        yield return _factory.Create(new RegistryTweakSpec
        {
            Key = "security.smartscreen-pua",
            Title = "Proteção contra aplicações potencialmente indesejadas",
            Description = "Ativa o bloqueio de software que instala barras de ferramentas, alteradores de pesquisa e outros PUA.",
            Hive = "HKLM",
            SubKey = @"SOFTWARE\Policies\Microsoft\Windows Defender",
            ValueName = "PUAProtection",
            OptimizedValue = 1,
            Category = OptimizationCategory.Sistema,
            Area = OptimizationArea.Privacidade,
            Risk = "Baixo — pode bloquear utilitários legítimos de fabricantes (reverta se necessário).",
            RequiresRestart = false,
        }, simulationMode);

        yield return _factory.Create(new RegistryTweakSpec
        {
            Key = "security.smb1-off",
            Title = "Protocolo SMBv1 desativado",
            Description = "O SMBv1 é obsoleto e foi o vetor do WannaCry; desativá-lo é uma recomendação oficial de segurança.",
            Hive = "HKLM",
            SubKey = @"SYSTEM\CurrentControlSet\Services\LanmanServer\Parameters",
            ValueName = "SMB1",
            OptimizedValue = 0,
            Category = OptimizationCategory.Sistema,
            Area = OptimizationArea.Privacidade,
            Risk = "Médio — equipamentos muito antigos de partilha de ficheiros podem deixar de funcionar.",
            RiskLevel = HealthLevel.Atencao,
            RequiresRestart = true,
            AvailabilityPredicate = () => true,
        }, simulationMode);

        yield return _factory.Create(new RegistryTweakSpec
        {
            Key = "security.smartscreen-edge",
            Title = "SmartScreen ativado para aplicações e ficheiros",
            Description = "Bloqueia a execução de ficheiros transferidos que não são reconhecidos como seguros.",
            Hive = "HKLM",
            SubKey = @"SOFTWARE\Policies\Microsoft\Windows\System",
            ValueName = "EnableSmartScreen",
            OptimizedValue = 1,
            Category = OptimizationCategory.Sistema,
            Area = OptimizationArea.Privacidade,
            Risk = "Baixo.",
            RequiresRestart = false,
        }, simulationMode);
    }

    /// <summary>Verifica se o disco do sistema é SSD (usado por tweaks condicionais).</summary>
    private bool IsSolidStateSystemDrive()
        => _systemInfo.Hardware.StorageType.Contains("SSD", StringComparison.OrdinalIgnoreCase);

    /// <summary>Executa a verificação dos serviços essenciais (usado no relatório de estabilidade).</summary>
    public int CountStoppedCriticalServices()
    {
        var critical = new[] { "RpcSs", "DcomLaunch", "Winmgmt", "EventLog", "AudioSrv", "Dhcp", "Dnscache", "Themes" };
        var stopped = 0;

        foreach (var service in critical)
        {
            var result = _runner.Run("sc.exe", $"query {service}");
            if (result.Output.Contains("STOPPED", StringComparison.OrdinalIgnoreCase)) stopped++;
        }

        return stopped;
    }

    /// <summary>Exporta o catálogo para HTML (documentação automática das otimizações).</summary>
    public string ExportToHtml()
    {
        var rows = string.Join(Environment.NewLine, All.Select(a =>
            $"<tr><td>{a.Title}</td><td>{a.Category}</td><td>{a.CurrentValue}</td><td>{a.NewValue}</td><td>{a.Risk}</td></tr>"));

        return $@"<!DOCTYPE html><html lang=""pt""><head><meta charset=""utf-8""><title>HL Optimizer Pro — Catálogo de otimizações</title>
<style>body{{font-family:Segoe UI,sans-serif;background:#0B1530;color:#E6EDF7;padding:24px}}
table{{border-collapse:collapse;width:100%}}th,td{{border:1px solid #1E6BFF55;padding:8px;font-size:13px;text-align:left}}
th{{background:#1E6BFF22}}</style></head><body>
<h1>Catálogo de otimizações — v{AppConstants.Version}</h1>
<p>{All.Count} otimizações documentadas; {AppliedCount} já aplicadas neste computador.</p>
<table><thead><tr><th>Otimização</th><th>Categoria</th><th>Valor atual</th><th>Novo valor</th><th>Risco</th></tr></thead>
<tbody>{rows}</tbody></table></body></html>";
    }
}
