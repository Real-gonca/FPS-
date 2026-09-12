using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Nexus.Application.Profiles;
using Nexus.Application.UseCases;
using Nexus.Domain.Ports;
using Nexus.Presentation.Views;

namespace Nexus.Presentation.ViewModels;

/// <summary>
/// Serviços (spec §4.6): inventário REAL via WMI, ações via sc.exe dentro
/// do CommandExecutor (whitelist), e perfis com preview de impacto antes de
/// aplicar. Tudo reversível (backup + rollback individual por alteração).
/// </summary>
public sealed class ServicesViewModel : ObservableObject
{
    private readonly IServiceInventory _inventory;
    private readonly ServiceOperationUseCase _operations;
    private readonly ILogger<ServicesViewModel> _log;

    private string _searchText = string.Empty;
    private string _stateFilter = "Todos";
    private ServiceRow? _selected;
    private string _statusText = "A carregar inventário de serviços (WMI)…";

    public ServicesViewModel(
        IServiceInventory inventory,
        ServiceOperationUseCase operations,
        ILogger<ServicesViewModel> log)
    {
        _inventory = inventory;
        _operations = operations;
        _log = log;

        Services = new ObservableCollection<ServiceRow>();
        Profiles = new ObservableCollection<ProfileRow>(
            ServiceProfileCatalog.Default.Select(p => new ProfileRow(p, operations)));

        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
        StartCommand = new AsyncRelayCommand<ServiceRow>(s => StartAsync(s));
        StopCommand = new AsyncRelayCommand<ServiceRow>(s => StopAsync(s));
        EnableCommand = new AsyncRelayCommand<ServiceRow>(s => ChangeModeAsync(s, "demand", "Habilitar (arranque manual)"));
        DisableCommand = new AsyncRelayCommand<ServiceRow>(s => ChangeModeAsync(s, "disabled", "Desativar (não arrancar)"));

        _ = RefreshAsync();
    }

    public ObservableCollection<ServiceRow> Services { get; }

    public ObservableCollection<ProfileRow> Profiles { get; }

    public IAsyncRelayCommand RefreshCommand { get; }
    public IAsyncRelayCommand<ServiceRow> StartCommand { get; }
    public IAsyncRelayCommand<ServiceRow> StopCommand { get; }
    public IAsyncRelayCommand<ServiceRow> EnableCommand { get; }
    public IAsyncRelayCommand<ServiceRow> DisableCommand { get; }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (Set(ref _searchText, value))
                RaiseFilteredChanged();
        }
    }

    public string StateFilter
    {
        get => _stateFilter;
        set
        {
            if (Set(ref _stateFilter, value))
                RaiseFilteredChanged();
        }
    }

    public ServiceRow? Selected
    {
        get => _selected;
        set
        {
            if (Set(ref _selected, value))
                OnPropertyChanged(nameof(SelectedName));
        }
    }

    public string SelectedName => Selected?.DisplayName ?? string.Empty;

    public string StatusText
    {
        get => _statusText;
        set => Set(ref _statusText, value);
    }

    /// <summary>Lista filtrada (pesquisa + estado) — o DataGrid liga-se a isto.</summary>
    public IEnumerable<ServiceRow> FilteredServices
    {
        get
        {
            string text = SearchText.Trim();
            return Services.Where(s =>
                (text.Length == 0
                    || s.DisplayName.Contains(text, StringComparison.OrdinalIgnoreCase)
                    || s.Name.Contains(text, StringComparison.OrdinalIgnoreCase))
                && MatchesStateFilter(s))
                .ToList();
        }
    }

    private bool MatchesStateFilter(ServiceRow s) => StateFilter switch
    {
        "Em execução" => s.State == "Running",
        "Parados" => s.State == "Stopped",
        "Desativados" => s.StartMode == "Disabled",
        _ => true,
    };

    private void RaiseFilteredChanged()
    {
        OnPropertyChanged(nameof(FilteredServices));
        OnPropertyChanged(nameof(FilteredCount));
    }

    public string FilteredCount
    {
        get
        {
            int count = 0;
            foreach (var _ in FilteredServices)
                count++;
            return count.ToString();
        }
    }

    public async Task RefreshAsync()
    {
        StatusText = "A carregar inventário de serviços (WMI)…";
        try
        {
            var list = await _inventory.GetServicesAsync();
            Services.Clear();
            foreach (var s in list.OrderBy(x => x.DisplayName))
                Services.Add(new ServiceRow(s));

            StatusText = list.Count == 0
                ? "Inventário indisponível — o WMI falhou ou o sistema não é Windows. Nenhum serviço é mostrado (nada é fabricado)."
                : list.Count + " serviços (fonte real: WMI Win32_Service).";
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Falha ao carregar serviços.");
            StatusText = "Falha ao carregar o inventário de serviços.";
        }
        RaiseFilteredChanged();
    }

    // ── Ações individuais (preview de impacto → confirmação → pipeline) ──

    private async Task StartAsync(ServiceRow? row)
    {
        if (row is null)
            return;
        string details = ServiceDetails(row);
        bool ok = await ConfirmWindow.ShowAsync(
            Application.Current?.MainWindow,
            "Iniciar serviço",
            $"Iniciar \"{row.DisplayName}\" ({row.Name})?",
            details + "\nAção reversível — fica registada no histórico com rollback.");
        if (!ok)
            return;

        StatusText = $"A iniciar {row.Name}…";
        await _operations.StartServiceAsync(
            row.Name, row.DisplayName,
            "Início manual pedido pelo utilizador no Serviços Manager.",
            default);
        await RefreshAsync();
    }

    private async Task StopAsync(ServiceRow? row)
    {
        if (row is null)
            return;
        string details = ServiceDetails(row);
        bool ok = await ConfirmWindow.ShowAsync(
            Application.Current?.MainWindow,
            "Parar serviço",
            $"Parar \"{row.DisplayName}\" ({row.Name})?",
            details + "\nAção reversível — fica registada no histórico com rollback.");
        if (!ok)
            return;

        StatusText = $"A parar {row.Name}…";
        await _operations.StopServiceAsync(
            row.Name, row.DisplayName,
            "Paragem manual pedida pelo utilizador no Serviços Manager.",
            default);
        await RefreshAsync();
    }

    private async Task ChangeModeAsync(ServiceRow? row, string targetMode, string actionLabel)
    {
        if (row is null)
            return;
        string details = ServiceDetails(row) +
            $"\nMudança: arranque \"{DescribeStartMode(row.StartMode)}\" → \"{DescribeScMode(targetMode)}\".";
        bool ok = await ConfirmWindow.ShowAsync(
            Application.Current?.MainWindow,
            actionLabel,
            $"{actionLabel} \"{row.DisplayName}\" ({row.Name})?",
            details + "\nAção reversível — o arranque anterior fica guardado no backup.");
        if (!ok)
            return;

        StatusText = $"{actionLabel}: {row.Name}…";
        await _operations.ChangeStartModeAsync(
            row.Name, row.DisplayName, targetMode,
            $"Alteração de start mode pedida pelo utilizador no Serviços Manager ({actionLabel}).",
            default);
        await RefreshAsync();
    }

    private static string ServiceDetails(ServiceRow row) =>
        $"Serviço: {row.DisplayName} ({row.Name})\nEstado: {DescribeState(row.State)} · Arranque: {DescribeStartMode(row.StartMode)}";

    internal static string DescribeState(string state) => state switch
    {
        "Running" => "Em execução",
        "Stopped" => "Parado",
        "Paused" => "Em pausa",
        "Start Pending" => "A iniciar",
        "Stop Pending" => "A parar",
        _ => state,
    };

    internal static string DescribeStartMode(string mode) => mode switch
    {
        "Auto" => "Automático",
        "Manual" or "Demand" => "Manual",
        "Disabled" => "Desativado",
        "Boot" => "Boot (início do sistema)",
        "System" => "Sistema",
        _ => mode,
    };

    internal static string DescribeScMode(string scMode) => scMode switch
    {
        "auto" => "Automático",
        "demand" => "Manual",
        "disabled" => "Desativado",
        _ => scMode,
    };
}

/// <summary>Linha do inventário de serviços.</summary>
public sealed class ServiceRow
{
    public ServiceRow(ServiceInfo info)
    {
        Name = info.Name;
        DisplayName = info.DisplayName;
        State = info.State;
        StartMode = info.StartMode;
        StartName = info.StartName;
        StateText = ServicesViewModel.DescribeState(info.State);
        StartModeText = ServicesViewModel.DescribeStartMode(info.StartMode);
    }

    public string Name { get; }
    public string DisplayName { get; }
    public string State { get; }
    public string StartMode { get; }
    public string StartName { get; }
    public string StateText { get; }
    public string StartModeText { get; }
}

/// <summary>Cartão de perfil (Gaming/Privacidade) com preview de impacto.</summary>
public sealed class ProfileRow : ObservableObject
{
    private readonly ServiceOperationUseCase _operations;

    private string _statusText = string.Empty;
    private bool _isApplying;

    public ProfileRow(ServiceProfile profile, ServiceOperationUseCase operations)
    {
        Profile = profile;
        _operations = operations;
        Key = profile.Key;
        Title = profile.Name;
        Description = profile.Description;
        ChangesText = string.Join("\n\n", profile.Changes.Select(c =>
            $"• {c.DisplayName} ({c.ServiceKey}) → {ServicesViewModel.DescribeScMode(c.TargetStartMode)}\n  {c.Justification}"));
    }

    public ServiceProfile Profile { get; }
    public string Key { get; }
    public string Title { get; }
    public string Description { get; }
    public string ChangesText { get; }
    public int ChangesCount => Profile.Changes.Count;

    public string StatusText
    {
        get => _statusText;
        set => Set(ref _statusText, value);
    }

    public bool IsApplying
    {
        get => _isApplying;
        set
        {
            if (Set(ref _isApplying, value))
                OnPropertyChanged(nameof(ApplyButtonText));
        }
    }

    public string ApplyButtonText => IsApplying ? "A aplicar…" : "Aplicar perfil";

    /// <summary>
    /// Preview de impacto (lista completa de alterações) → confirmação →
    /// aplicação alteração a alteração via pipeline (cada uma com backup e
    /// rollback individual; uma falha interrompe o perfil).
    /// </summary>
    public async Task ApplyAsync()
    {
        if (IsApplying)
            return;
        IsApplying = true;
        try
        {
            StatusText = string.Empty;
            bool ok = await ConfirmWindow.ShowAsync(
                Application.Current?.MainWindow,
                "Aplicar perfil: " + Title,
                $"{ChangesCount} alteração(ões) de serviço. Cada uma tem backup e rollback individual — se uma falhar, as restantes não são aplicadas.",
                ChangesText,
                okText: "Aplicar");
            if (!ok)
            {
                StatusText = "Cancelado.";
                return;
            }

            StatusText = "A aplicar…";
            var outcome = await _operations.ApplyProfileAsync(Profile);
            StatusText = outcome.Summary;
        }
        catch (Exception ex)
        {
            StatusText = "Falha: " + ex.Message;
        }
        finally
        {
            IsApplying = false;
        }
    }
}
