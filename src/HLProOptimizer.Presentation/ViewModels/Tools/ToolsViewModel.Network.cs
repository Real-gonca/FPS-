using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HLProOptimizer.Core.Enums;
using HLProOptimizer.Core.Models;

namespace HLProOptimizer.Presentation.ViewModels.Tools;

/// <summary>Seções "Hosts", "DNS" e "Rede" da tela de Ferramentas.</summary>
/// <remarks>
/// <para>
/// <b>Hosts com round-trip fiel.</b> <see cref="HostsFileModel"/> preserva as linhas
/// originais (comentários incluídos) — a edição não destrói o arquivo do usuário. Todo
/// salvamento passa por <see cref="IHostsEditorService.SaveAsync"/>, que cria backup
/// automático antes de gravar.
/// </para>
/// <para>
/// <b>DoH é limitado pelo Windows.</b> <c>netsh dns set encryption</c> só aceita
/// templates conhecidos; ao aplicar um preset (Cloudflare/Google/Quad9) habilitamos o
/// DoH em seguida, e o resultado real aparece no log — sem prometer criptografia para
/// servidores customizados.
/// </para>
/// </remarks>
public sealed partial class ToolsViewModel
{
    private HostsFileModel? _hostsModel;
    private ICollectionView? _hostsView;

    /// <summary>Configurações de DNS por adaptador.</summary>
    public ObservableCollection<DnsConfiguration> DnsConfigurations { get; } = [];

    /// <summary>Entradas do cache DNS.</summary>
    public ObservableCollection<string> DnsCacheEntries { get; } = [];

    /// <summary>Entradas do arquivo hosts.</summary>
    public ObservableCollection<HostsEntry> HostsEntries { get; } = [];

    /// <summary>Visão filtrada do hosts.</summary>
    public ICollectionView HostsView => _hostsView ??= CreateHostsView();

    /// <summary>Domínios de rastreamento conhecidos pelo produto.</summary>
    public IReadOnlyList<string> KnownTrackers => HostsEditor.KnownTrackerDomains;

    /// <summary>Configuração de DNS selecionada.</summary>
    [ObservableProperty]
    private DnsConfiguration? _selectedDnsConfiguration;

    /// <summary>Preset de DNS selecionado.</summary>
    [ObservableProperty]
    private DnsPreset? _selectedDnsPreset;

    /// <summary>Busca no hosts.</summary>
    [ObservableProperty]
    private string _hostsSearchText = string.Empty;

    /// <summary>Mostra apenas domínios bloqueados.</summary>
    [ObservableProperty]
    private bool _onlyBlockedHosts;

    /// <summary>Domínio informado pelo usuário para bloquear.</summary>
    [ObservableProperty]
    private string _newBlockedDomain = string.Empty;

    /// <summary>Domínio selecionado na lista do hosts.</summary>
    [ObservableProperty]
    private HostsEntry? _selectedHostsEntry;

    /// <summary>Indica se o arquivo hosts é somente leitura.</summary>
    [ObservableProperty]
    private bool _hostsIsReadOnly;

    /// <summary>Última modificação do hosts.</summary>
    [ObservableProperty]
    private string _hostsLastModifiedText = string.Empty;

    /// <summary>Resumo do hosts.</summary>
    [ObservableProperty]
    private string _hostsSummaryText = string.Empty;

    /// <summary>Host usado no teste de latência.</summary>
    [ObservableProperty]
    private string _latencyHost = "8.8.8.8";

    /// <summary>Resultado do teste de latência.</summary>
    [ObservableProperty]
    private string _latencyResultText = "—";

    /// <summary>Indica se o DoH está ativo no adaptador selecionado.</summary>
    [ObservableProperty]
    private bool _isDohEnabled;

    // ---------------------------------------------------------------------
    // DNS
    // ---------------------------------------------------------------------

    /// <summary>Carrega as configurações de DNS dos adaptadores.</summary>
    [RelayCommand]
    private async Task LoadDnsAsync()
    {
        await RunBusyAsync(async () =>
        {
            var configurations = await Dns.GetConfigurationsAsync().ConfigureAwait(true);

            DnsConfigurations.Clear();

            foreach (var configuration in configurations)
            {
                DnsConfigurations.Add(configuration);
            }

            SelectedDnsConfiguration = DnsConfigurations.FirstOrDefault();
            IsDohEnabled = SelectedDnsConfiguration?.DohEnabled ?? false;
            SelectedDnsPreset ??= DnsPresets.FirstOrDefault(p =>
                p.Name.Equals(_settings.Current.PreferredDnsPreset, StringComparison.OrdinalIgnoreCase));
        }, L("Common_Loading")).ConfigureAwait(true);
    }

    /// <summary>Aplica um preset de DNS a todos os adaptadores ativos.</summary>
    /// <param name="preset">Preset selecionado.</param>
    [RelayCommand]
    private async Task ApplyDnsPresetAsync(DnsPreset? preset)
    {
        var target = preset ?? SelectedDnsPreset;

        if (target is null)
        {
            return;
        }

        await RunBusyAsync(async () =>
        {
            var applied = await Dns.ApplyPresetAsync(target).ConfigureAwait(true);

            // DoH só funciona com templates conhecidos: tentamos após aplicar o preset.
            var doh = applied > 0 &&
                      await Dns.SetDnsOverHttpsAsync(SelectedDnsConfiguration?.AdapterName ?? string.Empty, true)
                          .ConfigureAwait(true);

            await _history.RecordAsync(
                ActionKind.Network,
                $"{L("Tools_Dns")}: {target.Name}",
                LF("Tools_DnsAppliedDetail", applied, target.Primary, target.Secondary),
                success: applied > 0).ConfigureAwait(true);

            AppendOutput($"{L("Tools_Dns")}: {target.Name} → {applied} adaptador(es) · DoH={(doh ? "on" : "off")}");

            StatusMessage = applied > 0
                ? LF("Tools_DnsApplied", target.Name, applied)
                : L("Msg_GenericError");

            var settings = _settings.Current.Clone();
            settings.PreferredDnsPreset = target.Name;
            await _settings.SaveAsync(settings).ConfigureAwait(true);

            await LoadDnsAsync().ConfigureAwait(true);
        }, L("Common_Loading")).ConfigureAwait(true);
    }

    /// <summary>Volta o adaptador selecionado para DNS automático (DHCP).</summary>
    [RelayCommand]
    private async Task ResetDnsAsync()
    {
        var adapter = SelectedDnsConfiguration?.AdapterName;

        if (string.IsNullOrWhiteSpace(adapter))
        {
            return;
        }

        await ExecuteSimpleAsync(
            ct => Dns.ResetToAutomaticAsync(adapter, ct),
            L("Tools_DnsAutomatic"),
            ActionKind.Network,
            requiresAdmin: true).ConfigureAwait(true);

        await LoadDnsAsync().ConfigureAwait(true);
    }

    /// <summary>Habilita/desabilita DNS over HTTPS no adaptador selecionado.</summary>
    /// <param name="enabled">"true"/"false".</param>
    [RelayCommand]
    private async Task SetDnsOverHttpsAsync(string? enabled)
    {
        var adapter = SelectedDnsConfiguration?.AdapterName;

        if (string.IsNullOrWhiteSpace(adapter) || !bool.TryParse(enabled, out var desired))
        {
            return;
        }

        var applied = await ExecuteSimpleAsync(
            ct => Dns.SetDnsOverHttpsAsync(adapter, desired, ct),
            L("Tools_DnsOverHttps"),
            ActionKind.Network,
            requiresAdmin: true).ConfigureAwait(true);

        IsDohEnabled = applied && desired;
    }

    /// <summary>Executa o flush do cache DNS.</summary>
    [RelayCommand]
    private Task FlushDnsAsync() =>
        ExecuteToolAsync((_, ct) => Dns.FlushCacheAsync(ct), L("Tools_FlushDns"), ActionKind.Network);

    /// <summary>Lê o cache DNS atual.</summary>
    [RelayCommand]
    private async Task LoadDnsCacheAsync()
    {
        await RunBusyAsync(async () =>
        {
            var entries = await Dns.GetCachedEntriesAsync().ConfigureAwait(true);

            OnUiThread(() =>
            {
                DnsCacheEntries.Clear();

                foreach (var entry in entries.Take(500))
                {
                    DnsCacheEntries.Add(entry);
                }
            });

            StatusMessage = LF("Tools_DnsCacheLoaded", entries.Count);
        }, L("Common_Loading")).ConfigureAwait(true);
    }

    // ---------------------------------------------------------------------
    // Hosts
    // ---------------------------------------------------------------------

    /// <summary>Carrega o arquivo hosts.</summary>
    [RelayCommand]
    private async Task LoadHostsAsync()
    {
        await RunBusyAsync(async () =>
        {
            _hostsModel = await HostsEditor.LoadAsync().ConfigureAwait(true);

            OnUiThread(() =>
            {
                HostsEntries.Clear();

                foreach (var entry in _hostsModel.Entries)
                {
                    HostsEntries.Add(entry);
                }

                HostsIsReadOnly = _hostsModel.IsReadOnly;
                HostsLastModifiedText = _hostsModel.LastModified?.ToString("dd/MM/yyyy HH:mm") ?? "—";
                HostsSummaryText = LF("Tools_HostsSummary", _hostsModel.BlockedHostnames.Count, HostsEntries.Count);

                HostsView.Refresh();
            });
        }, L("Common_Loading")).ConfigureAwait(true);
    }

    /// <summary>Bloqueia os domínios de rastreamento conhecidos.</summary>
    [RelayCommand]
    private async Task BlockTrackerDomainsAsync()
    {
        var confirmed = await ConfirmAsync(
            L("Tools_BlockDomains"),
            LF("Tools_BlockTrackersConfirm", KnownTrackers.Count)).ConfigureAwait(true);

        if (!confirmed)
        {
            return;
        }

        await RunBusyAsync(async () =>
        {
            var added = await HostsEditor.BlockDomainsAsync(KnownTrackers).ConfigureAwait(true);

            await _history.RecordAsync(ActionKind.Network, L("Tools_BlockDomains"), $"{added} domínio(s)", success: true)
                .ConfigureAwait(true);

            StatusMessage = LF("Tools_DomainsBlocked", added);

            await LoadHostsAsync().ConfigureAwait(true);
        }, L("Common_Loading")).ConfigureAwait(true);
    }

    /// <summary>Bloqueia um domínio digitado pelo usuário.</summary>
    [RelayCommand]
    private async Task BlockDomainAsync()
    {
        var domain = NewBlockedDomain?.Trim();

        if (string.IsNullOrWhiteSpace(domain))
        {
            return;
        }

        await RunBusyAsync(async () =>
        {
            var added = await HostsEditor.BlockDomainsAsync([domain]).ConfigureAwait(true);

            StatusMessage = added > 0 ? LF("Tools_DomainsBlocked", added) : L("Tools_DomainAlreadyBlocked");

            NewBlockedDomain = string.Empty;

            await LoadHostsAsync().ConfigureAwait(true);
        }, L("Common_Loading")).ConfigureAwait(true);
    }

    /// <summary>Libera o domínio selecionado.</summary>
    [RelayCommand]
    private async Task UnblockDomainAsync()
    {
        var entry = SelectedHostsEntry;

        if (entry is null || entry.IsCommentOnly)
        {
            return;
        }

        await RunBusyAsync(async () =>
        {
            var removed = await HostsEditor.UnblockDomainsAsync([entry.Hostname]).ConfigureAwait(true);

            StatusMessage = LF("Tools_DomainsUnblocked", removed);

            await LoadHostsAsync().ConfigureAwait(true);
        }, L("Common_Loading")).ConfigureAwait(true);
    }

    /// <summary>Restaura o último backup do hosts.</summary>
    [RelayCommand]
    private async Task RestoreHostsBackupAsync()
    {
        var confirmed = await ConfirmAsync(L("Tools_RestoreHostsBackup"), L("Tools_RestoreHostsConfirm"))
            .ConfigureAwait(true);

        if (!confirmed)
        {
            return;
        }

        await ExecuteSimpleAsync(
            ct => HostsEditor.RestoreBackupAsync(ct),
            L("Tools_RestoreHostsBackup"),
            ActionKind.Network,
            requiresAdmin: true).ConfigureAwait(true);

        await LoadHostsAsync().ConfigureAwait(true);
    }

    /// <summary>Exporta o hosts para um arquivo escolhido pelo usuário.</summary>
    [RelayCommand]
    private async Task ExportHostsAsync()
    {
        var path = await _dialogs.ShowSaveFileDialogAsync(new FileDialogOptions(
            L("Common_Export"),
            "Texto (*.txt)|*.txt|Todos os arquivos (*.*)|*.*",
            InitialFileName: $"hosts-{DateTime.Now:yyyy-MM-dd}.txt",
            DefaultExtension: ".txt")).ConfigureAwait(true);

        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        await RunBusyAsync(async () =>
        {
            var saved = await HostsEditor.ExportAsync(path).ConfigureAwait(true);

            StatusMessage = LF("Common_Exported", saved);
        }, L("Common_Loading")).ConfigureAwait(true);
    }

    /// <summary>Importa uma lista de domínios para bloqueio.</summary>
    [RelayCommand]
    private async Task ImportHostsAsync()
    {
        var path = await _dialogs.ShowOpenFileDialogAsync(new FileDialogOptions(
            L("Common_Import"),
            "Texto (*.txt)|*.txt|Todos os arquivos (*.*)|*.*")).ConfigureAwait(true);

        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        await RunBusyAsync(async () =>
        {
            var imported = await HostsEditor.ImportAsync(path, blockImported: true).ConfigureAwait(true);

            StatusMessage = LF("Tools_DomainsImported", imported);

            await LoadHostsAsync().ConfigureAwait(true);
        }, L("Common_Loading")).ConfigureAwait(true);
    }

    // ---------------------------------------------------------------------
    // Reparo de rede
    // ---------------------------------------------------------------------

    /// <summary>Reparo completo da pilha de rede (winsock + TCP/IP + DNS + renew).</summary>
    [RelayCommand]
    private async Task RepairNetworkAsync()
    {
        var confirmed = await ConfirmAsync(
            L("Tools_RepairNetwork"),
            L("Tools_RepairNetworkConfirm"),
            L("Common_Start")).ConfigureAwait(true);

        if (!confirmed)
        {
            return;
        }

        if (IsBusy)
        {
            return;
        }

        IReadOnlyList<CommandResult>? results = null;

        var completed = await _dialogs.ShowProgressAsync(
            L("Tools_RepairNetwork"),
            async (progress, cancellationToken) =>
            {
                var textProgress = new Progress<string>(line =>
                {
                    AppendOutput(line);
                    progress.Report(new ScanProgress(IssueCategory.Network, L("Tools_RepairNetwork"), 0, 0, line));
                });

                results = await NetworkTools.RepairNetworkAsync(textProgress, cancellationToken).ConfigureAwait(true);
            }).ConfigureAwait(true);

        if (!completed || results is null)
        {
            StatusMessage = L("Dlg_CancelledTitle");
            return;
        }

        var success = results.Count(r => r.IsSuccess);

        await _history.RecordAsync(
            ActionKind.Network,
            L("Tools_RepairNetwork"),
            $"{success}/{results.Count} comandos OK",
            success: success == results.Count).ConfigureAwait(true);

        StatusMessage = LF("Tools_NetworkRepaired", success, results.Count);

        AppendOutput($"{L("Tools_RepairNetwork")}: {success}/{results.Count} OK · {L("Msg_RestartRequired")}");
    }

    /// <summary>Reinicia a pilha TCP/IP.</summary>
    [RelayCommand]
    private Task ResetTcpIpAsync() =>
        ExecuteToolAsync((_, ct) => NetworkTools.ResetTcpIpAsync(ct), L("Tools_ResetTcpIp"), ActionKind.Network, requiresAdmin: true);

    /// <summary>Reinicia o catálogo Winsock.</summary>
    [RelayCommand]
    private Task ResetWinsockAsync() =>
        ExecuteToolAsync((_, ct) => NetworkTools.ResetWinsockAsync(ct), L("Tools_ResetWinsock"), ActionKind.Network, requiresAdmin: true);

    /// <summary>Libera e renova o endereço IP.</summary>
    [RelayCommand]
    private async Task RenewIpAsync()
    {
        var released = await ExecuteToolAsync(
            (_, ct) => NetworkTools.ReleaseIpAsync(ct),
            L("Tools_ReleaseIp"),
            ActionKind.Network,
            requiresAdmin: true).ConfigureAwait(true);

        if (released)
        {
            await ExecuteToolAsync(
                (_, ct) => NetworkTools.RenewIpAsync(ct),
                L("Tools_RenewIp"),
                ActionKind.Network,
                requiresAdmin: true).ConfigureAwait(true);
        }
    }

    /// <summary>Mede a latência até o host informado.</summary>
    [RelayCommand]
    private async Task MeasureLatencyAsync()
    {
        if (string.IsNullOrWhiteSpace(LatencyHost))
        {
            return;
        }

        await RunBusyAsync(async () =>
        {
            var latency = await NetworkTools.MeasureLatencyAsync(LatencyHost.Trim()).ConfigureAwait(true);

            LatencyResultText = latency is { } value ? $"{value:F0} ms" : L("Tools_LatencyUnreachable");

            AppendOutput($"ping {LatencyHost} → {LatencyResultText}");

            var settings = _settings.Current.Clone();
            settings.LatencyProbeHost = LatencyHost.Trim();
            await _settings.SaveAsync(settings).ConfigureAwait(true);
        }, L("Common_Loading")).ConfigureAwait(true);
    }

    /// <summary>Aplica os ajustes de baixa latência.</summary>
    [RelayCommand]
    private Task ApplyLowLatencyAsync() =>
        ExecuteSimpleAsync(
            ct => NetworkTools.ApplyLowLatencyTweaksAsync(ct),
            L("Tools_LowLatency"),
            ActionKind.Network,
            requiresAdmin: true);

    /// <summary>Reverte os ajustes de baixa latência.</summary>
    [RelayCommand]
    private Task RevertLowLatencyAsync() =>
        ExecuteSimpleAsync(
            ct => NetworkTools.RevertLowLatencyTweaksAsync(ct),
            L("Tools_LowLatency"),
            ActionKind.Network,
            requiresAdmin: true);

    /// <summary>Abre as configurações de rede do Windows.</summary>
    [RelayCommand]
    private Task OpenNetworkSettingsAsync() => OpenExternalAsync("ms-settings:network-status");

    partial void OnHostsSearchTextChanged(string value) => HostsView.Refresh();

    partial void OnOnlyBlockedHostsChanged(bool value) => HostsView.Refresh();

    partial void OnSelectedDnsConfigurationChanged(DnsConfiguration? value) => IsDohEnabled = value?.DohEnabled ?? false;

    private ICollectionView CreateHostsView()
    {
        var view = CollectionViewSource.GetDefaultView(HostsEntries);

        view.Filter = item => MatchesHostsFilter((HostsEntry)item);

        return view;
    }

    private bool MatchesHostsFilter(HostsEntry entry)
    {
        if (OnlyBlockedHosts && !entry.IsBlocked)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(HostsSearchText))
        {
            return true;
        }

        return entry.Hostname.Contains(HostsSearchText, StringComparison.OrdinalIgnoreCase) ||
               entry.IpAddress.Contains(HostsSearchText, StringComparison.OrdinalIgnoreCase);
    }
}
