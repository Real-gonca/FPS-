using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HLProOptimizer.Core.Abstractions;
using HLProOptimizer.Core.Enums;
using HLProOptimizer.Core.Models;
using HLProOptimizer.Presentation.ViewModels.Items;
using Microsoft.Extensions.Logging;

namespace HLProOptimizer.Presentation.ViewModels;

/// <summary>
/// Tela "Inicialização": lista os programas que iniciam com o Windows
/// (registro, pastas de inicialização e tarefas agendadas), estima o impacto no boot
/// e permite ativar/desativar, remover ou pesquisar um item.
/// </summary>
/// <remarks>
/// <para>
/// <b>Reversibilidade primeiro.</b> Desativar usa o mecanismo do próprio Windows
/// (chave <c>StartupApproved</c>), que é reversível; remover exige confirmação
/// explícita porque apaga a entrada.
/// </para>
/// <para>
/// <b>Boot estimado.</b> <see cref="IStartupManager.Summarize"/> devolve o tempo de
/// boot atual e o projetado com apenas os itens essenciais — exibimos a economia em
/// segundos e percentual, que é o que o usuário entende.
/// </para>
/// </remarks>
public sealed partial class StartupViewModel : ViewModelBase
{
    private readonly IStartupManager _startupManager;
    private readonly IDialogService _dialogs;
    private readonly IElevationService _elevation;
    private readonly ICollectionView _programsView;

    /// <summary>Cria o ViewModel da inicialização.</summary>
    /// <param name="startupManager">Gerenciador de inicialização.</param>
    /// <param name="dialogs">Diálogos.</param>
    /// <param name="elevation">Elevação sob demanda.</param>
    /// <param name="localization">Localização.</param>
    /// <param name="logger">Logger.</param>
    public StartupViewModel(
        IStartupManager startupManager,
        IDialogService dialogs,
        IElevationService elevation,
        ILocalizationService localization,
        ILogger<StartupViewModel> logger)
        : base(localization, logger)
    {
        _startupManager = startupManager;
        _dialogs = dialogs;
        _elevation = elevation;

        _programsView = CollectionViewSource.GetDefaultView(Programs);
        _programsView.Filter = item => MatchesFilter((StartupProgramViewModel)item);

        IsElevated = _elevation.IsElevated;
    }

    /// <summary>Programas de inicialização.</summary>
    public ObservableCollection<StartupProgramViewModel> Programs { get; } = [];

    /// <summary>Visão filtrada exibida na tabela.</summary>
    public ICollectionView ProgramsView => _programsView;

    /// <summary>Texto da busca.</summary>
    [ObservableProperty]
    private string _searchText = string.Empty;

    /// <summary>Mostra apenas itens ativos.</summary>
    [ObservableProperty]
    private bool _onlyEnabled;

    /// <summary>Mostra apenas itens órfãos (executável ausente).</summary>
    [ObservableProperty]
    private bool _onlyOrphaned;

    /// <summary>Mostra apenas itens de alto impacto.</summary>
    [ObservableProperty]
    private bool _onlyHighImpact;

    /// <summary>Total de programas.</summary>
    [ObservableProperty]
    private int _totalCount;

    /// <summary>Programas ativos.</summary>
    [ObservableProperty]
    private int _enabledCount;

    /// <summary>Programas desativados.</summary>
    [ObservableProperty]
    private int _disabledCount;

    /// <summary>Programas de alto impacto ativos.</summary>
    [ObservableProperty]
    private int _highImpactCount;

    /// <summary>Itens órfãos.</summary>
    [ObservableProperty]
    private int _orphanedCount;

    /// <summary>Tempo de boot atual estimado.</summary>
    [ObservableProperty]
    private string _currentBootText = "—";

    /// <summary>Tempo de boot otimizado estimado.</summary>
    [ObservableProperty]
    private string _optimizedBootText = "—";

    /// <summary>Economia estimada em segundos.</summary>
    [ObservableProperty]
    private string _savingsText = "—";

    /// <summary>Economia estimada em percentual.</summary>
    [ObservableProperty]
    private double _savingsPercent;

    /// <summary>Indica se a lista foi carregada.</summary>
    [ObservableProperty]
    private bool _isLoaded;

    /// <summary>Indica se o processo é administrador.</summary>
    [ObservableProperty]
    private bool _isElevated;

    /// <summary>Carrega a lista de programas.</summary>
    [RelayCommand]
    private async Task LoadAsync()
    {
        await RunBusyAsync(async () =>
        {
            var programs = await _startupManager.GetStartupProgramsAsync().ConfigureAwait(true);

            ApplyPrograms(programs);

            IsLoaded = true;
            IsElevated = _elevation.IsElevated;

            StatusMessage = LF("Startup_LoadedSummary", TotalCount, EnabledCount);
        }, L("Common_Loading")).ConfigureAwait(true);
    }

    /// <summary>
    /// Ativa/desativa um item a partir de um botão da linha.
    /// </summary>
    /// <param name="item">Item exibido na tabela.</param>
    /// <remarks>
    /// Apenas inverte <see cref="StartupProgramViewModel.IsEnabled"/>: quem persiste é
    /// <see cref="OnItemEnabledChangedAsync"/>, acionado pelo evento do wrapper. Assim
    /// ToggleSwitch e botão compartilham exatamente o mesmo caminho (DRY) e não há
    /// risco de dupla gravação.
    /// </remarks>
    [RelayCommand]
    private void ToggleProgram(StartupProgramViewModel? item)
    {
        if (item is null || IsBusy)
        {
            return;
        }

        item.IsEnabled = !item.IsEnabled;
    }

    /// <summary>Remove permanentemente a entrada de inicialização.</summary>
    /// <param name="item">Item selecionado.</param>
    [RelayCommand]
    private async Task RemoveAsync(StartupProgramViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        var confirmed = await _dialogs.ConfirmAsync(
            L("Startup_RemoveConfirm"),
            LF("Startup_RemoveMessage", item.Name),
            confirmText: L("Common_Remove"),
            isDestructive: true).ConfigureAwait(true);

        if (!confirmed)
        {
            return;
        }

        await RunBusyAsync(async () =>
        {
            var removed = await _startupManager.RemoveAsync(item.Model).ConfigureAwait(true);

            if (removed)
            {
                Programs.Remove(item);
                RecalculateSummary();
                StatusMessage = LF("Startup_ItemRemoved", item.Name);
            }
            else
            {
                StatusMessage = L("Msg_GenericError");
            }
        }, L("Common_Loading")).ConfigureAwait(true);
    }

    /// <summary>Desativa em lote os itens considerados não essenciais.</summary>
    [RelayCommand]
    private async Task DisableNonEssentialAsync()
    {
        var confirmed = await _dialogs.ConfirmAsync(
            L("Startup_DisableNonEssential"),
            L("Startup_DisableNonEssentialConfirm")).ConfigureAwait(true);

        if (!confirmed)
        {
            return;
        }

        await RunBusyAsync(async () =>
        {
            var candidates = Programs.Where(p => p.IsEnabled && p.IsSafeToDisable).Select(p => p.Model).ToList();

            var disabled = await _startupManager.DisableNonEssentialAsync(candidates).ConfigureAwait(true);

            foreach (var item in Programs.Where(p => disabled.Contains(p.Name, StringComparer.OrdinalIgnoreCase)))
            {
                item.IsEnabled = false;
            }

            RecalculateSummary();

            StatusMessage = LF("Startup_NonEssentialDisabled", disabled.Count);
        }, L("Common_Loading")).ConfigureAwait(true);
    }

    /// <summary>Abre a localização do executável no Explorer.</summary>
    /// <param name="item">Item selecionado.</param>
    [RelayCommand]
    private async Task OpenLocationAsync(StartupProgramViewModel? item)
    {
        var path = item?.Model.ResolvedLocationPath ?? item?.Model.ExecutablePath;

        if (string.IsNullOrWhiteSpace(path))
        {
            await _dialogs.ShowInfoAsync(L("Common_OpenLocation"), L("Startup_PathUnavailable")).ConfigureAwait(true);
            return;
        }

        await RunBusyAsync(async () =>
        {
            await Task.Run(() => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true
            })).ConfigureAwait(true);
        }, L("Common_Loading")).ConfigureAwait(true);
    }

    /// <summary>Abre a pesquisa online do item (identificação de software/malware).</summary>
    /// <param name="item">Item selecionado.</param>
    [RelayCommand]
    private async Task SearchOnlineAsync(StartupProgramViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        var url = _startupManager.BuildSearchUrl(item.Model);

        await RunBusyAsync(async () =>
        {
            await Task.Run(() => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            })).ConfigureAwait(true);
        }, L("Common_Loading")).ConfigureAwait(true);
    }

    /// <summary>Limpa os filtros aplicados.</summary>
    [RelayCommand]
    private void ClearFilters()
    {
        SearchText = string.Empty;
        OnlyEnabled = false;
        OnlyOrphaned = false;
        OnlyHighImpact = false;
    }

    partial void OnSearchTextChanged(string value) => _programsView.Refresh();

    partial void OnOnlyEnabledChanged(bool value) => _programsView.Refresh();

    partial void OnOnlyOrphanedChanged(bool value) => _programsView.Refresh();

    partial void OnOnlyHighImpactChanged(bool value) => _programsView.Refresh();

    // ---------------------------------------------------------------------
    // Internos
    // ---------------------------------------------------------------------

    private bool MatchesFilter(StartupProgramViewModel item)
    {
        if (OnlyEnabled && !item.IsEnabled)
        {
            return false;
        }

        if (OnlyOrphaned && !item.IsOrphaned)
        {
            return false;
        }

        if (OnlyHighImpact && item.Impact != StartupImpact.High)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(SearchText))
        {
            return true;
        }

        return item.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
               item.Publisher.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
               item.Command.Contains(SearchText, StringComparison.OrdinalIgnoreCase);
    }

    private void ApplyPrograms(IReadOnlyList<StartupProgram> programs)
    {
        Programs.Clear();

        foreach (var program in programs
                     .OrderByDescending(p => p.IsEnabled)
                     .ThenByDescending(p => p.Impact)
                     .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase))
        {
            var item = new StartupProgramViewModel(program, Localization);
            item.EnabledChanged += async (_, enabled) => await OnItemEnabledChangedAsync(item, enabled).ConfigureAwait(true);
            Programs.Add(item);
        }

        RecalculateSummary();
    }

    /// <summary>
    /// Sincroniza o modelo quando o usuário alterna o <c>ToggleSwitch</c> da linha
    /// (o comando <see cref="ToggleAsync"/> também cobre cliques no botão).
    /// </summary>
    private async Task OnItemEnabledChangedAsync(StartupProgramViewModel item, bool enabled)
    {
        if (IsBusy || !IsLoaded)
        {
            return;
        }

        try
        {
            var applied = await _startupManager.SetEnabledAsync(item.Model, enabled).ConfigureAwait(true);

            OnUiThread(() =>
            {
                if (!applied)
                {
                    item.IsEnabled = !enabled;
                    StatusMessage = L("Msg_NeedAdmin");
                    return;
                }

                RecalculateSummary();

                StatusMessage = enabled
                    ? LF("Startup_ItemEnabled", item.Name)
                    : LF("Startup_ItemDisabled", item.Name);
            });
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Falha ao alterar o estado do item de inicialização {Name}.", item.Name);

            OnUiThread(() =>
            {
                item.IsEnabled = !enabled;
                StatusMessage = L("Msg_GenericError");
            });
        }
    }

    /// <summary>
    /// Recalcula as estatísticas de boot a partir do estado atual da UI.
    /// </summary>
    /// <remarks>
    /// <see cref="StartupProgram.IsEnabled"/> é <c>init</c>-only (modelo imutável de
    /// domínio), então o baseline do boot vem de <see cref="IStartupManager.Summarize"/>
    /// e a economia é derivada dos itens que o usuário desativou na tela.
    /// </remarks>
    private void RecalculateSummary()
    {
        var models = Programs.Select(p => p.Model).ToList();
        var summary = _startupManager.Summarize(models);

        TotalCount = Programs.Count;
        EnabledCount = Programs.Count(p => p.IsEnabled);
        DisabledCount = TotalCount - EnabledCount;
        HighImpactCount = Programs.Count(p => p.IsEnabled && p.Impact == StartupImpact.High);
        OrphanedCount = Programs.Count(p => p.IsOrphaned);

        var baseline = summary.CurrentBootSeconds;
        var savedSeconds = Programs.Where(p => !p.IsEnabled).Sum(p => p.Model.EstimatedSeconds);
        var optimized = Math.Max(0d, baseline - savedSeconds);

        CurrentBootText = baseline > 0 ? $"{baseline:F0}s" : "—";
        OptimizedBootText = baseline > 0 ? $"{optimized:F0}s" : "—";
        SavingsText = savedSeconds > 0 ? $"{savedSeconds:F0}s" : "0s";
        SavingsPercent = baseline > 0 ? Math.Clamp(savedSeconds / baseline * 100d, 0d, 100d) : 0d;
    }
}
