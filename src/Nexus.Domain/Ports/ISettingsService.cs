namespace Nexus.Domain.Ports;

/// <summary>Definições persistentes da aplicação (linha única em SQLite).</summary>
public sealed class AppSettings
{
    /// <summary>false = Modo Simples; true = Modo Avançado (spec §1).</summary>
    public bool AdvancedMode { get; set; }

    /// <summary>Janela (horas) do gráfico de tendência do Dashboard.</summary>
    public int ChartWindowHours { get; set; } = 12;

    /// <summary>Auto-dismiss das notificações (segundos).</summary>
    public int NotificationAutoDismissSeconds { get; set; } = 6;
}

public interface ISettingsService
{
    Task<AppSettings> LoadAsync(CancellationToken ct = default);

    Task SaveAsync(AppSettings settings, CancellationToken ct = default);
}
