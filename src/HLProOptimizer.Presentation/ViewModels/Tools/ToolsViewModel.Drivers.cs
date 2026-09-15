using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HLProOptimizer.Core.Enums;
using HLProOptimizer.Core.Models;

namespace HLProOptimizer.Presentation.ViewModels.Tools;

/// <summary>Seção "Drivers" da tela de Ferramentas.</summary>
/// <remarks>
/// <para>
/// Trabalhamos com o driver store real (<c>pnputil</c>) e com os dispositivos com
/// problema (Code 10/28/43). Três decisões honestas do produto aparecem aqui:
/// </para>
/// <list type="bullet">
///   <item><b>Rollback</b> não tem CLI suportado: informamos e abrimos o
///   <c>devmgmt.msc</c> em vez de fingir sucesso.</item>
///   <item><b>Desinstalar</b> exige confirmação dupla e mostra o
///   <c>PublishedName</c> (oemXX.inf) que será removido.</item>
///   <item><b>Scan de hardware</b> usa <c>pnputil /scan-devices</c> no Windows 11 e
///   <c>CM_Reenumerate_DevNode</c> como fallback (implementado na infraestrutura).</item>
/// </list>
/// </remarks>
public sealed partial class ToolsViewModel
{
    private ICollectionView? _driversView;

    /// <summary>Drivers de terceiros publicados no driver store.</summary>
    public ObservableCollection<DriverInfo> Drivers { get; } = [];

    /// <summary>Dispositivos com problema.</summary>
    public ObservableCollection<DriverInfo> ProblemDevices { get; } = [];

    /// <summary>Visão filtrada dos drivers.</summary>
    public ICollectionView DriversView => _driversView ??= CreateDriversView();

    /// <summary>Busca de drivers.</summary>
    [ObservableProperty]
    private string _driverSearchText = string.Empty;

    /// <summary>Mostra apenas drivers com problema.</summary>
    [ObservableProperty]
    private bool _onlyProblemDrivers;

    /// <summary>Driver selecionado.</summary>
    [ObservableProperty]
    private DriverInfo? _selectedDriver;

    /// <summary>Resumo da seção.</summary>
    [ObservableProperty]
    private string _driversSummaryText = string.Empty;

    /// <summary>Indica se há dispositivos com problema.</summary>
    public bool HasProblemDevices => ProblemDevices.Count > 0;

    /// <summary>Carrega os drivers publicados.</summary>
    [RelayCommand]
    private async Task LoadDriversAsync()
    {
        await RunBusyAsync(async () =>
        {
            var drivers = await DriverManager.GetPublishedDriversAsync().ConfigureAwait(true);

            Drivers.Clear();

            foreach (var driver in drivers.OrderBy(d => d.DeviceName, StringComparer.OrdinalIgnoreCase))
            {
                Drivers.Add(driver);
            }

            await LoadProblemDevicesCoreAsync().ConfigureAwait(true);

            DriversSummaryText = LF("Tools_DriversSummary", Drivers.Count, ProblemDevices.Count);

            DriversView.Refresh();
        }, L("Common_Loading")).ConfigureAwait(true);
    }

    /// <summary>Carrega apenas os dispositivos com problema.</summary>
    [RelayCommand]
    private async Task LoadProblemDevicesAsync()
    {
        await RunBusyAsync(async () =>
        {
            await LoadProblemDevicesCoreAsync().ConfigureAwait(true);

            DriversSummaryText = LF("Tools_DriversSummary", Drivers.Count, ProblemDevices.Count);
        }, L("Common_Loading")).ConfigureAwait(true);
    }

    /// <summary>Força a atualização do driver de um dispositivo.</summary>
    /// <param name="driver">Driver selecionado.</param>
    [RelayCommand]
    private Task UpdateDriverAsync(DriverInfo? driver)
    {
        var target = driver ?? SelectedDriver;

        if (target is null || string.IsNullOrWhiteSpace(target.DeviceId))
        {
            return Task.CompletedTask;
        }

        return ExecuteToolAsync(
            (_, ct) => DriverManager.UpdateDriverAsync(target.DeviceId, ct),
            L("Tools_UpdateDriver"),
            ActionKind.Drivers,
            requiresAdmin: true);
    }

    /// <summary>
    /// Reverte um driver para a versão anterior.
    /// </summary>
    /// <param name="driver">Driver selecionado.</param>
    /// <remarks>
    /// O Windows não expõe rollback por linha de comando. Em vez de reportar um falso
    /// sucesso, explicamos a limitação e abrimos o Gerenciador de Dispositivos na aba
    /// correta — o caminho real para o usuário concluir a ação.
    /// </remarks>
    [RelayCommand]
    private async Task RollbackDriverAsync(DriverInfo? driver)
    {
        var target = driver ?? SelectedDriver;

        await _dialogs.ShowInfoAsync(L("Tools_RollbackDriver"), L("Tools_RollbackUnavailable")).ConfigureAwait(true);

        await OpenDeviceManagerAsync().ConfigureAwait(true);

        if (target is not null)
        {
            AppendOutput($"{L("Tools_RollbackDriver")}: {target.DeviceName} ({target.DeviceId})");
        }
    }

    /// <summary>Remove um pacote de driver do driver store.</summary>
    /// <param name="driver">Driver selecionado.</param>
    [RelayCommand]
    private async Task UninstallDriverAsync(DriverInfo? driver)
    {
        var target = driver ?? SelectedDriver;

        if (target is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(target.PublishedName))
        {
            await _dialogs.ShowWarningAsync(L("Tools_UninstallDriver"), L("Tools_NoPublishedName")).ConfigureAwait(true);
            return;
        }

        var confirmed = await ConfirmAsync(
            L("Tools_UninstallDriver"),
            LF("Tools_UninstallDriverConfirm", target.DeviceName, target.PublishedName)).ConfigureAwait(true);

        if (!confirmed)
        {
            return;
        }

        var success = await ExecuteToolAsync(
            (_, ct) => DriverManager.UninstallDriverAsync(target.PublishedName!, deleteBinary: false, ct),
            L("Tools_UninstallDriver"),
            ActionKind.Drivers,
            requiresAdmin: true).ConfigureAwait(true);

        if (success)
        {
            await LoadDriversAsync().ConfigureAwait(true);
        }
    }

    /// <summary>Procura por alterações de hardware.</summary>
    [RelayCommand]
    private Task ScanForHardwareChangesAsync() =>
        ExecuteToolAsync(
            (_, ct) => DriverManager.ScanForHardwareChangesAsync(ct),
            L("Tools_ScanHardware"),
            ActionKind.Drivers,
            requiresAdmin: true);

    /// <summary>Abre o Gerenciador de Dispositivos.</summary>
    [RelayCommand]
    private Task OpenDeviceManagerAsync() => OpenExternalAsync("devmgmt.msc");

    partial void OnDriverSearchTextChanged(string value) => DriversView.Refresh();

    partial void OnOnlyProblemDriversChanged(bool value) => DriversView.Refresh();

    private async Task LoadProblemDevicesCoreAsync()
    {
        try
        {
            var problems = await DriverManager.GetProblemDevicesAsync().ConfigureAwait(true);

            ProblemDevices.Clear();

            foreach (var device in problems)
            {
                ProblemDevices.Add(device);
            }

            OnPropertyChanged(nameof(HasProblemDevices));
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Falha ao listar os dispositivos com problema.");
        }
    }

    private ICollectionView CreateDriversView()
    {
        var view = CollectionViewSource.GetDefaultView(Drivers);

        view.Filter = item => MatchesDriverFilter((DriverInfo)item);
        view.SortDescriptions.Add(new SortDescription(nameof(DriverInfo.DeviceName), ListSortDirection.Ascending));

        return view;
    }

    private bool MatchesDriverFilter(DriverInfo driver)
    {
        if (OnlyProblemDrivers && !driver.HasProblem)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(DriverSearchText))
        {
            return true;
        }

        return driver.DeviceName.Contains(DriverSearchText, StringComparison.OrdinalIgnoreCase) ||
               driver.Manufacturer.Contains(DriverSearchText, StringComparison.OrdinalIgnoreCase) ||
               driver.DeviceClass.Contains(DriverSearchText, StringComparison.OrdinalIgnoreCase) ||
               driver.Version.Contains(DriverSearchText, StringComparison.OrdinalIgnoreCase);
    }
}
