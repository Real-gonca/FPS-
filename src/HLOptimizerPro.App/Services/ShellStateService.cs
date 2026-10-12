using CommunityToolkit.Mvvm.ComponentModel;
using HLOptimizerPro.Core.Models;

namespace HLOptimizerPro.App.Services;

/// <summary>
/// Estado partilhado do shell (estado do sistema, modo Jogo ativo, contagem de otimizações pendentes).
///
/// Porque existe um serviço só para isto? Porque várias páginas precisam de atualizar o mesmo
/// indicador no cabeçalho/rodapé (Dashboard mede, Modo Jogo altera, Avaliação conclui). Um único
/// dono do estado evita que o cabeçalho apareça com informação contraditória.
/// </summary>
public sealed partial class ShellStateService : ObservableObject
{
    /// <summary>Estado geral do sistema mostrado no rodapé ("Sistema otimizado").</summary>
    [ObservableProperty]
    private HealthLevel _systemState = HealthLevel.Desconhecido;

    [ObservableProperty]
    private string _systemStateText = "A analisar…";

    [ObservableProperty]
    private bool _isGameModeActive;

    /// <summary>Número de otimizações pendentes apresentado no painel.</summary>
    [ObservableProperty]
    private int _pendingOptimizations;

    /// <summary>Aplica uma alteração de estado a partir de uma medição (Dashboard, Avaliação).</summary>
    public void UpdateSystemState(HealthLevel level)
    {
        SystemState = level;
        SystemStateText = level switch
        {
            HealthLevel.Otimo => "Sistema otimizado",
            HealthLevel.Atencao => "Atenção necessária",
            HealthLevel.Critico => "Requer intervenção",
            _ => "A analisar…",
        };
    }

    /// <summary>Deduz o estado a partir das medições em tempo real (regras transparentes).</summary>
    public void UpdateFromMetrics(double cpuPercent, double ramPercent, double? temperatureCelsius)
    {
        var level = HealthLevel.Otimo;

        if (ramPercent >= 92 || cpuPercent >= 96 || temperatureCelsius is >= 92)
            level = HealthLevel.Critico;
        else if (ramPercent >= 82 || cpuPercent >= 88 || temperatureCelsius is >= 82)
            level = HealthLevel.Atencao;

        UpdateSystemState(level);
    }
}
