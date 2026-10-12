using System.Collections.ObjectModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HLOptimizerPro.App.Services;
using HLOptimizerPro.Core.Helpers;
using HLOptimizerPro.Core.Models;
using HLOptimizerPro.Core.Services;

namespace HLOptimizerPro.App.ViewModels;

/// <summary>
/// Linha da lista de processos. Guarda o processo original e os textos já formatados em pt-PT
/// (a View não faz cálculos nem formatação).
/// </summary>
public sealed record ProcessRow(
    ProcessUsageInfo Process,
    string Name,
    string Description,
    string MemoryText,
    string CpuText,
    string PidText,
    double PercentOfRam,
    bool IsProtected)
{
    public int Pid => Process.Pid;
    public long WorkingSetBytes => Process.WorkingSetBytes;
}

/// <summary>
/// ViewModel da ferramenta Memória RAM (3.4-a).
///
/// O que faz:
///   • Mede o uso real de memória (total, livre, em cache, standby e páginas modificadas);
///   • Lista os processos que mais memória ocupam (dados reais do sistema);
///   • Liberta memória em quatro passos, todos reversíveis por natureza (o sistema volta a usar a
///     memória conforme precisa): EmptyWorkingSet, limpeza de cache, descarga de páginas
///     modificadas e limpeza de standby;
///   • Nunca encerra processos críticos — a lista de proteção é aplicada antes de qualquer ação.
/// </summary>
public sealed partial class MemoryViewModel : ViewModelBase
{
    private readonly MemoryOptimizationService _memory;
    private readonly MonitoringService _monitoring;
    private readonly IDialogService _dialogs;
    private readonly SettingsService _settings;

    private readonly DispatcherTimer _refreshTimer;

    public MemoryViewModel(
        NotificationService notifications,
        MemoryOptimizationService memory,
        MonitoringService monitoring,
        IDialogService dialogs,
        SettingsService settings)
        : base(notifications)
    {
        _memory = memory;
        _monitoring = monitoring;
        _dialogs = dialogs;
        _settings = settings;

        Title = "Memória RAM";
        Subtitle = "Liberte memória sem fechar os seus programas";
        IconKind = "Memory";

        foreach (var action in _memory.GetActions()) Actions.Add(action);

        var interval = Math.Clamp(_settings.Current.RealtimeIntervalMs * 2, 1500, 5000);
        _refreshTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(interval),
        };
        _refreshTimer.Tick += (_, _) => Refresh();
    }

    // ---- Medições -------------------------------------------------------------

    [ObservableProperty] private double _usagePercent;
    [ObservableProperty] private string _usageText = "—";
    [ObservableProperty] private string _totalText = "—";
    [ObservableProperty] private string _availableText = "—";
    [ObservableProperty] private string _cachedText = "—";
    [ObservableProperty] private string _standbyText = "—";
    [ObservableProperty] private string _modifiedText = "—";
    [ObservableProperty] private string _pageFileText = "—";
    [ObservableProperty] private string _pressureText = "—";
    [ObservableProperty] private HealthLevel _pressureLevel = HealthLevel.Desconhecido;
    [ObservableProperty] private bool _lowFreeWarning;
    [ObservableProperty] private string _tipText = string.Empty;
    [ObservableProperty] private string _lastOptimizationText = "Ainda não libertou memória nesta sessão.";

    /// <summary>Ações de libertação de memória reais (com backup/reversão do motor de otimizações).</summary>
    public ObservableCollection<OptimizationAction> Actions { get; } = new();

    /// <summary>Processos que mais memória ocupam (medidos agora, com textos formatados).</summary>
    public ObservableCollection<ProcessRow> TopProcesses { get; } = new();

    public string FreeRamPercentText => $"{(100 - UsagePercent):0} % livre de RAM";

    // ---- Ciclo de vida --------------------------------------------------------

    public override Task OnNavigatedToAsync(object? parameter = null)
    {
        Refresh();
        _refreshTimer.Start();
        return base.OnNavigatedToAsync(parameter);
    }

    public override Task OnNavigatedFromAsync()
    {
        _refreshTimer.Stop();
        return base.OnNavigatedFromAsync();
    }

    /// <summary>Lê o estado real da memória e atualiza indicadores, dica e lista de processos.</summary>
    private void Refresh()
    {
        try
        {
            var snapshot = _memory.GetSnapshot();

            UsagePercent = snapshot.Usage * 100.0;
            UsageText = Formatters.Percent(snapshot.Usage, 0);
            TotalText = Formatters.Bytes(snapshot.TotalBytes);
            AvailableText = Formatters.Bytes(snapshot.AvailableBytes);
            CachedText = Formatters.Bytes(snapshot.CachedBytes);
            StandbyText = Formatters.Bytes(snapshot.StandbyBytes);
            ModifiedText = Formatters.Bytes(snapshot.ModifiedBytes);
            PageFileText = snapshot.PageFileTotalBytes > 0
                ? $"{Formatters.Bytes(snapshot.PageFileUsedBytes)} de {Formatters.Bytes(snapshot.PageFileTotalBytes)}"
                : "sem ficheiro de paginação";

            var free = snapshot.FreePercent;
            LowFreeWarning = free < AppConstants.MinFreeRamPercent;

            (PressureText, PressureLevel) = snapshot.Usage switch
            {
                < 0.65 => ("Pressão baixa — a memória está folgada.", HealthLevel.Otimo),
                < 0.82 => ("Pressão média — começa a faltar folga.", HealthLevel.Atencao),
                _ => ("Pressão alta — os programas podem ficar lentos.", HealthLevel.Critico),
            };

            TipText = LowFreeWarning
                ? $"Só tem {free:0} % de memória livre (recomendado: pelo menos " +
                  $"{AppConstants.MinFreeRamPercent:0} %). Experimente «Reduzir Uso de RAM» e feche " +
                  "programas que não esteja a usar — os jogos agradecem."
                : $"Tem {free:0} % de RAM livre — acima do recomendado " +
                  $"({AppConstants.MinFreeRamPercent:0} %). Pode começar a jogar sem libertar memória.";

            var total = snapshot.TotalBytes;
            var processes = _monitoring.GetTopMemoryProcesses(12);

            TopProcesses.Clear();
            foreach (var process in processes)
            {
                TopProcesses.Add(new ProcessRow(
                    process,
                    process.Name,
                    string.IsNullOrWhiteSpace(process.Description) ? "—" : process.Description,
                    Formatters.Bytes(process.WorkingSetBytes),
                    process.CpuPercent > 0 ? Formatters.Percent(process.CpuPercent / 100.0, 1) : "—",
                    $"PID {process.Pid}",
                    total <= 0 ? 0 : process.WorkingSetBytes * 100.0 / total,
                    process.IsProtected || process.IsSystemCritical));
            }

            OnPropertyChanged(nameof(FreeRamPercentText));
        }
        catch (Exception ex)
        {
            SetStatus($"Falha ao ler a memória: {ex.Message}", isError: true);
        }
    }

    // ---- Optimização ----------------------------------------------------------

    /// <summary>Executa uma das quatro limpezas de memória.</summary>
    [RelayCommand]
    private async Task RunActionAsync(string? actionKey)
    {
        if (string.IsNullOrWhiteSpace(actionKey)) return;

        await RunBusyAsync(() =>
        {
            var result = _memory.Execute(actionKey);
            Report(result, "Memória");

            LastOptimizationText = result.Success
                ? $"{DateTime.Now:HH:mm:ss} — {result.Message}"
                : $"Última tentativa falhou: {result.Message}";

            // A leitura do estado tem de ser feita depois de a limpeza terminar.
            Refresh();
            return Task.CompletedTask;
        }, "A libertar memória…");
    }

    /// <summary>Liberta memória em sequência (útil antes de abrir um jogo).</summary>
    [RelayCommand]
    private async Task ReleaseAllAsync()
    {
        var keys = Actions.Select(a => a.Key).ToList();
        if (keys.Count == 0) return;

        var confirmed = await _dialogs.ConfirmAsync(
            "Libertar memória",
            $"Vão ser executadas {keys.Count} limpezas de memória:\n\n" +
            string.Join("\n", Actions.Select(a => $"• {a.Title}")) +
            "\n\nNenhum programa será encerrado — apenas memória em cache e páginas não usadas são libertadas.",
            "Libertar memória");

        if (!confirmed) return;

        await RunBusyAsync(() =>
        {
            var successes = 0;
            var details = new List<string>();

            foreach (var key in keys)
            {
                var result = _memory.Execute(key);
                if (result.Success) successes++;
                details.Add($"{key}: {result.Message}");
            }

            SetStatus($"{successes} de {keys.Count} limpezas concluídas com sucesso.", successes == 0);
            Notifications.Success("Memória", $"{successes} de {keys.Count} limpezas aplicadas.");

            LastOptimizationText = $"{DateTime.Now:HH:mm:ss} — {successes} de {keys.Count} limpezas concluídas.";
            Refresh();
            return Task.CompletedTask;
        }, "A libertar memória…");
    }

    /// <summary>
    /// Encerra um processo escolhido pelo utilizador. Esta é a única ação com impacto real nos
    /// programas abertos, por isso exige confirmação e respeita a lista de processos protegidos.
    /// </summary>
    [RelayCommand]
    private async Task TerminateProcessAsync(ProcessRow? row)
    {
        if (row is null) return;
        var process = row.Process;

        if (row.IsProtected)
        {
            await _dialogs.ShowInfoAsync(
                "Processo protegido",
                $"«{process.Name}» faz parte da lista de processos críticos do Windows e nunca será encerrado " +
                "pelo HL Optimizer Pro — mesmo a pedido. Isto evita desestabilizar o sistema.");
            return;
        }

        var confirmed = await _dialogs.ConfirmAsync(
            "Encerrar programa",
            $"Encerrar «{process.Name}»?\n\n" +
            $"• Memória ocupada: {Formatters.Bytes(process.WorkingSetBytes)}\n" +
            $"• Processo: {process.Name} (PID {process.Pid})\n\n" +
            "Os ficheiros abertos nesse programa podem não ser guardados. Esta ação não pode ser revertida.\n\n" +
            $"Caminho: {process.ExecutablePath ?? "desconhecido"}",
            "Encerrar processo", danger: true);

        if (!confirmed) return;

        await RunBusyAsync(() =>
        {
            var result = _memory.TerminateProcess(process, confirmed: true);
            Report(result, "Memória");
            Refresh();
            return Task.CompletedTask;
        }, "A encerrar o processo…");
    }

    [RelayCommand]
    private void RefreshNow() => Refresh();
}
