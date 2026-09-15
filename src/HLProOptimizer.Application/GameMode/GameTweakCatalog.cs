using HLProOptimizer.Core.Models;

namespace HLProOptimizer.Application.GameMode;

/// <summary>
/// Metadados dos tweaks do Modo Gamer (nome, descrição, ganho estimado e ordem).
/// Separados da implementação para que a UI possa listar os tweaks mesmo antes
/// de qualquer aplicação.
/// </summary>
public static class GameTweakCatalog
{
    /// <summary>Ids dos tweaks.</summary>
    public static class Ids
    {
        /// <summary>Encerra processos em segundo plano.</summary>
        public const string BackgroundProcesses = "gamer.backgroundprocess";

        /// <summary>Libera memória RAM.</summary>
        public const string Memory = "gamer.memory";

        /// <summary>Ativa o plano Ultimate Performance.</summary>
        public const string PowerPlan = "gamer.powerplan";

        /// <summary>Desativa Game Bar e DVR.</summary>
        public const string GameBar = "gamer.gamebar";

        /// <summary>Desativa notificações (Focus Assist).</summary>
        public const string Notifications = "gamer.notifications";

        /// <summary>Eleva a prioridade do jogo.</summary>
        public const string GamePriority = "gamer.priority";

        /// <summary>Otimiza a GPU para desempenho (HAGS + GPU Priority).</summary>
        public const string Gpu = "gamer.gpu";

        /// <summary>Pausa o Windows Update.</summary>
        public const string WindowsUpdate = "gamer.windowsupdate";

        /// <summary>Otimiza a rede para baixa latência.</summary>
        public const string Network = "gamer.network";

        /// <summary>Desativa a suspensão seletiva de USB.</summary>
        public const string UsbPower = "gamer.usbpower";

        /// <summary>Desativa a indexação de busca.</summary>
        public const string Indexing = "gamer.indexing";

        /// <summary>Otimiza o MMCSS.</summary>
        public const string Mmcss = "gamer.mmcss";
    }

    /// <summary>Definições na ordem de aplicação.</summary>
    public static IReadOnlyList<GameTweakDefinition> All { get; } =
    [
        new(Ids.PowerPlan, "Plano Ultimate Performance", "Ativa o plano de desempenho máximo e remove limites de clock.", true, 8, 10),
        new(Ids.UsbPower, "Energia USB", "Desativa a suspensão seletiva de USB, eliminando micro-travamentos de mouse/teclado/controlador.", true, 3, 20),
        new(Ids.Mmcss, "MMCSS", "Prioriza jogos no agendador multimídia e remove o throttling de rede.", true, 6, 30),
        new(Ids.GameBar, "Game Bar e DVR", "Desativa a captura em segundo plano que causa stuttering.", false, 5, 40),
        new(Ids.Gpu, "Otimização de GPU", "Ativa o agendamento de GPU acelerado por hardware (HAGS) e a prioridade máxima de GPU para jogos.", true, 5, 50),
        new(Ids.Network, "Rede de baixa latência", "Desativa o algoritmo de Nagle e aplica DNS de baixa latência.", true, 3, 60),
        new(Ids.Indexing, "Indexação de busca", "Interrompe o Windows Search para liberar I/O de disco.", true, 4, 70),
        new(Ids.WindowsUpdate, "Pausa do Windows Update", "Interrompe downloads/reinícios automáticos durante os jogos.", true, 2, 80),
        new(Ids.Notifications, "Notificações", "Ativa o modo sem interrupções para evitar pop-ups durante a partida.", false, 1, 90),
        new(Ids.BackgroundProcesses, "Processos em segundo plano", "Encerra processos dispensáveis que consomem CPU, RAM e disco.", false, 6, 100),
        new(Ids.Memory, "Liberação de RAM", "Compacta o working set dos processos restantes, devolvendo RAM ao jogo.", false, 3, 110),
        new(Ids.GamePriority, "Prioridade do jogo", "Eleva a prioridade de CPU do processo do jogo detectado.", false, 7, 120)
    ];

    /// <summary>Obtém a definição de um tweak.</summary>
    /// <param name="id">Identificador.</param>
    public static GameTweakDefinition? Find(string id)
        => All.FirstOrDefault(d => string.Equals(d.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>Cria instâncias não aplicadas de todos os tweaks (para exibição na UI).</summary>
    public static IReadOnlyList<GameTweak> CreateTweaks()
        => All.Select(d => new GameTweak
        {
            Id = d.Id,
            DisplayName = d.DisplayName,
            Description = d.Description,
            IsEnabled = d.EnabledByDefault,
            RequiresAdmin = d.RequiresAdmin,
            EstimatedFpsGain = d.EstimatedFpsGain,
            Order = d.Order
        }).ToList();
}

/// <summary>Definição estática de um tweak.</summary>
/// <param name="Id">Identificador.</param>
/// <param name="DisplayName">Nome exibido.</param>
/// <param name="Description">Descrição do efeito.</param>
/// <param name="RequiresAdmin">Se exige administrador.</param>
/// <param name="EstimatedFpsGain">Ganho de FPS estimado (0-10).</param>
/// <param name="Order">Ordem de aplicação.</param>
public sealed record GameTweakDefinition(
    string Id,
    string DisplayName,
    string Description,
    bool RequiresAdmin,
    int EstimatedFpsGain,
    int Order)
{
    /// <summary>Todos os tweaks são habilitados por padrão (o usuário pode desmarcar).</summary>
    public bool EnabledByDefault => true;
}
