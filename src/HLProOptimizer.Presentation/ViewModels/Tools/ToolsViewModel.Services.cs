using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HLProOptimizer.Core.Enums;
using HLProOptimizer.Presentation.ViewModels.Items;
using Microsoft.Extensions.Logging;

namespace HLProOptimizer.Presentation.ViewModels.Tools;

/// <summary>Seção "Serviços" da tela de Ferramentas.</summary>
/// <remarks>
/// <para>
/// Serviços críticos (<see cref="Core.Models.WindowsServiceInfo.IsCritical"/>) podem ser
/// inspecionados, mas parar um deles exige confirmação explícita — o produto não deve
/// conseguir "quebrar" o Windows com um clique distraído.
/// </para>
/// <para>
/// A ação em lote "Desativar telemetria" mira apenas serviços conhecidos e reversíveis
/// (DiagTrack, dmwappushservice), gravando o tipo de inicialização anterior no log para
/// permitir desfazer manualmente.
/// </para>
/// </remarks>
public sealed partial class ToolsViewModel
{
    /// <summary>Serviços conhecidos de telemetria (reversíveis).</summary>
    private static readonly string[] TelemetryServices = ["DiagTrack", "dmwappushservice"];

    private ICollectionView? _servicesView;

    /// <summary>Serviços carregados.</summary>
    public ObservableCollection<WindowsServiceViewModel> Services { get; } = [];

    /// <summary>Visão filtrada dos serviços.</summary>
    public ICollectionView ServicesView => _servicesView ??= CreateServicesView();

    /// <summary>Busca de serviços.</summary>
    [ObservableProperty]
    private string _serviceSearchText = string.Empty;

    /// <summary>Mostra apenas serviços em execução.</summary>
    [ObservableProperty]
    private bool _onlyRunningServices;

    /// <summary>Mostra apenas serviços automáticos.</summary>
    [ObservableProperty]
    private bool _onlyAutomaticServices;

    /// <summary>Resumo da lista de serviços.</summary>
    [ObservableProperty]
    private string _servicesSummaryText = string.Empty;

    /// <summary>Serviço selecionado.</summary>
    [ObservableProperty]
    private WindowsServiceViewModel? _selectedService;

    /// <summary>Carrega os serviços do Windows.</summary>
    [RelayCommand]
    private async Task LoadServicesAsync()
    {
        await RunBusyAsync(async () =>
        {
            var services = await ServiceManager.GetServicesAsync().ConfigureAwait(true);

            Services.Clear();

            foreach (var service in services.OrderBy(s => s.DisplayName, StringComparer.OrdinalIgnoreCase))
            {
                Services.Add(new WindowsServiceViewModel(service, Localization));
            }

            var running = Services.Count(s => s.IsRunning);

            ServicesSummaryText = LF("Tools_ServicesSummary", Services.Count, running);

            ServicesView.Refresh();
        }, L("Common_Loading")).ConfigureAwait(true);
    }

    /// <summary>Inicia um serviço.</summary>
    /// <param name="service">Serviço (ou o selecionado).</param>
    [RelayCommand]
    private Task StartServiceAsync(WindowsServiceViewModel? service) =>
        ExecuteSimpleAsync(
            ct => ServiceManager.StartAsync((service ?? SelectedService)?.ServiceName ?? string.Empty, ct),
            L("Common_Start"),
            ActionKind.Services);

    /// <summary>Para um serviço (com confirmação quando crítico).</summary>
    /// <param name="service">Serviço (ou o selecionado).</param>
    [RelayCommand]
    private async Task StopServiceAsync(WindowsServiceViewModel? service)
    {
        var target = service ?? SelectedService;

        if (target is null)
        {
            return;
        }

        if (target.IsCritical || RequiresConfirmation)
        {
            var confirmed = await ConfirmAsync(
                L("Common_Stop"),
                LF("Tools_StopServiceConfirm", target.DisplayName)).ConfigureAwait(true);

            if (!confirmed)
            {
                return;
            }
        }

        await ExecuteSimpleAsync(
            async ct =>
            {
                var stopped = await ServiceManager.StopAsync(target.ServiceName, ct).ConfigureAwait(true);

                if (stopped)
                {
                    OnUiThread(() => target.State = ServiceState.Stopped);
                }

                return stopped;
            },
            L("Common_Stop"),
            ActionKind.Services,
            requiresAdmin: true).ConfigureAwait(true);
    }

    /// <summary>Reinicia um serviço.</summary>
    /// <param name="service">Serviço (ou o selecionado).</param>
    [RelayCommand]
    private async Task RestartServiceAsync(WindowsServiceViewModel? service)
    {
        var target = service ?? SelectedService;

        if (target is null)
        {
            return;
        }

        await ExecuteSimpleAsync(
            async ct =>
            {
                var restarted = await ServiceManager.RestartAsync(target.ServiceName, ct).ConfigureAwait(true);

                if (restarted)
                {
                    OnUiThread(() => target.State = ServiceState.Running);
                }

                return restarted;
            },
            L("Common_Restart"),
            ActionKind.Services,
            requiresAdmin: true).ConfigureAwait(true);
    }

    /// <summary>Altera o tipo de inicialização de um serviço.</summary>
    /// <param name="startupKind">Nome do <see cref="ServiceStartupKind"/>.</param>
    [RelayCommand]
    private async Task SetServiceStartupAsync(string? startupKind)
    {
        var target = SelectedService;

        if (target is null || string.IsNullOrWhiteSpace(startupKind) ||
            !Enum.TryParse<ServiceStartupKind>(startupKind, ignoreCase: true, out var kind))
        {
            return;
        }

        if (target.IsCritical && kind == ServiceStartupKind.Disabled)
        {
            await _dialogs.ShowWarningAsync(L("Common_Disable"), LF("Tools_CannotDisableCritical", target.DisplayName)).ConfigureAwait(true);
            return;
        }

        var applied = await ExecuteSimpleAsync(
            async ct =>
            {
                var ok = await ServiceManager.SetStartupKindAsync(target.ServiceName, kind, ct).ConfigureAwait(true);

                if (ok)
                {
                    OnUiThread(() => target.StartupKind = kind);
                }

                return ok;
            },
            L("Tools_StartupType"),
            ActionKind.Services,
            requiresAdmin: true).ConfigureAwait(true);

        if (applied)
        {
            AppendOutput($"{target.ServiceName} → {Localization.GetEnumText(kind)}");
        }
    }

    /// <summary>Desativa os serviços de telemetria conhecidos.</summary>
    [RelayCommand]
    private async Task DisableTelemetryServicesAsync()
    {
        var confirmed = await ConfirmAsync(
            L("Tools_DisableTelemetryServices"),
            L("Tools_DisableTelemetryConfirm")).ConfigureAwait(true);

        if (!confirmed)
        {
            return;
        }

        await RunBusyAsync(async () =>
        {
            var affected = 0;

            foreach (var name in TelemetryServices)
            {
                var info = await ServiceManager.GetServiceAsync(name).ConfigureAwait(true);

                if (info is null)
                {
                    AppendOutput($"{name}: {L("Common_NotAvailable")}");
                    continue;
                }

                AppendOutput($"{name}: estado anterior = {Localization.GetEnumText(info.StartupKind)}");

                var stopped = info.IsRunning && await ServiceManager.StopAsync(name).ConfigureAwait(true);
                var disabled = await ServiceManager.SetStartupKindAsync(name, ServiceStartupKind.Disabled).ConfigureAwait(true);

                if (disabled)
                {
                    affected++;
                }

                AppendOutput($"{name}: parado={stopped} desativado={disabled}");
            }

            await LoadServicesAsync().ConfigureAwait(true);

            StatusMessage = LF("Tools_TelemetryServicesDisabled", affected);
        }, L("Common_Loading")).ConfigureAwait(true);
    }

    /// <summary>Abre o snap-in de serviços do Windows.</summary>
    [RelayCommand]
    private Task OpenServicesConsoleAsync() => OpenExternalAsync("services.msc");

    partial void OnServiceSearchTextChanged(string value) => ServicesView.Refresh();

    partial void OnOnlyRunningServicesChanged(bool value) => ServicesView.Refresh();

    partial void OnOnlyAutomaticServicesChanged(bool value) => ServicesView.Refresh();

    private ICollectionView CreateServicesView()
    {
        var view = CollectionViewSource.GetDefaultView(Services);

        view.Filter = item => MatchesServiceFilter((WindowsServiceViewModel)item);
        view.SortDescriptions.Add(new SortDescription(nameof(WindowsServiceViewModel.DisplayName), ListSortDirection.Ascending));

        return view;
    }

    private bool MatchesServiceFilter(WindowsServiceViewModel service)
    {
        if (OnlyRunningServices && !service.IsRunning)
        {
            return false;
        }

        if (OnlyAutomaticServices &&
            service.StartupKind is not (ServiceStartupKind.Automatic or ServiceStartupKind.AutomaticDelayed))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(ServiceSearchText))
        {
            return true;
        }

        return service.DisplayName.Contains(ServiceSearchText, StringComparison.OrdinalIgnoreCase) ||
               service.ServiceName.Contains(ServiceSearchText, StringComparison.OrdinalIgnoreCase) ||
               service.Description.Contains(ServiceSearchText, StringComparison.OrdinalIgnoreCase);
    }
}
