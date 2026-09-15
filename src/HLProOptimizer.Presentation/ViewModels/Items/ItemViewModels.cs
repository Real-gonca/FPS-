using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using HLProOptimizer.Core.Abstractions;
using HLProOptimizer.Core.Common;
using HLProOptimizer.Core.Enums;
using HLProOptimizer.Core.Models;
using HLProOptimizer.Presentation.Localization;

namespace HLProOptimizer.Presentation.ViewModels.Items;

/// <summary>
/// Base para wrappers de modelos do domínio que precisam de estado de UI
/// (seleção, texto localizado) sem poluir o <c>Core</c>.
/// </summary>
/// <typeparam name="TModel">Tipo do modelo encapsulado.</typeparam>
/// <remarks>
/// <b>Por que wrappers?</b> Os modelos do Core são records/classes puras de domínio
/// (sem <c>INotifyPropertyChanged</c>). As telas precisam de seleção observável e de
/// texto localizado; manter isso na camada de apresentação preserva o domínio
/// (SRP) e permite testar os modelos isoladamente.
/// </remarks>
public abstract partial class SelectableItemViewModel<TModel> : ObservableObject
    where TModel : class
{
    /// <summary>Cria o wrapper.</summary>
    /// <param name="model">Modelo de domínio.</param>
    protected SelectableItemViewModel(TModel model) => Model = model;

    /// <summary>Modelo de domínio encapsulado.</summary>
    public TModel Model { get; }

    /// <summary>Indica se o item está marcado na UI.</summary>
    [ObservableProperty]
    private bool _isSelected;
}

/// <summary>Item de análise exibido na tela de Análise do Sistema.</summary>
public sealed partial class AnalysisIssueViewModel : SelectableItemViewModel<AnalysisIssue>
{
    private readonly ILocalizationService _localization;

    /// <summary>Cria o wrapper de um problema encontrado.</summary>
    /// <param name="model">Problema.</param>
    /// <param name="localization">Localização.</param>
    /// <param name="isSelected">Marcação inicial (padrão: itens corrigíveis automaticamente).</param>
    public AnalysisIssueViewModel(AnalysisIssue model, ILocalizationService localization, bool isSelected = true)
        : base(model)
    {
        _localization = localization;
        IsSelected = isSelected && model.CanAutoFix;
    }

    /// <summary>Título do problema.</summary>
    public string Title => Model.Title;

    /// <summary>Descrição detalhada.</summary>
    public string Description => Model.Description;

    /// <summary>Severidade (usada pelo conversor de cor).</summary>
    public Severity Severity => Model.Severity;

    /// <summary>Categoria (usada pelo conversor de nome).</summary>
    public IssueCategory Category => Model.Category;

    /// <summary>Categoria localizada.</summary>
    public string CategoryText => _localization.GetEnumText(Model.Category);

    /// <summary>Severidade localizada.</summary>
    public string SeverityText => _localization.GetEnumText(Model.Severity);

    /// <summary>Espaço recuperável formatado.</summary>
    public string RecoverableText => string.IsNullOrEmpty(Model.RecoverableFormatted) ? "—" : Model.RecoverableFormatted;

    /// <summary>Ganho de boot estimado.</summary>
    public string BootImpactText => Model.HasBootImpact ? $"{Model.EstimatedBootImpactSeconds:F1}s" : "—";

    /// <summary>Ação recomendada.</summary>
    public string RecommendedAction => Model.RecommendedAction;

    /// <summary>Se pode ser corrigido automaticamente.</summary>
    public bool CanAutoFix => Model.CanAutoFix;

    /// <summary>Se exige administrador.</summary>
    public bool RequiresAdmin => Model.RequiresAdmin;

    /// <summary>Quantidade de itens afetados.</summary>
    public int ItemCount => Model.ItemCount;
}

/// <summary>Alvo de limpeza com seleção observável.</summary>
public sealed partial class CleanupTargetViewModel : ObservableObject
{
    /// <summary>Cria o wrapper de um alvo de limpeza.</summary>
    /// <param name="model">Alvo descoberto pelo scan.</param>
    public CleanupTargetViewModel(CleanupTarget model)
    {
        Model = model;
        _isSelected = model.IsSelected;
    }

    /// <summary>Alvo de domínio.</summary>
    public CleanupTarget Model { get; }

    /// <summary>Nome exibido.</summary>
    public string Name => Model.Name;

    /// <summary>Descrição.</summary>
    public string Description => Model.Description;

    /// <summary>Tamanho formatado.</summary>
    public string SizeFormatted => Model.SizeFormatted;

    /// <summary>Bytes estimados.</summary>
    public long EstimatedBytes => Model.EstimatedBytes;

    /// <summary>Quantidade de arquivos.</summary>
    public int FileCount => Model.FileCount;

    /// <summary>Se a remoção é segura.</summary>
    public bool IsSafe => Model.IsSafe;

    /// <summary>Se exige administrador.</summary>
    public bool RequiresAdmin => Model.RequiresAdmin;

    /// <summary>Severidade (colorização).</summary>
    public Severity Severity => Model.Severity;

    /// <summary>Indica se o alvo será removido.</summary>
    [ObservableProperty]
    private bool _isSelected;

    /// <summary>Disparado quando a seleção muda (o grupo recalcula os totais).</summary>
    public event EventHandler? SelectionChanged;

    partial void OnIsSelectedChanged(bool value)
    {
        // Mantém o modelo de domínio em sincronia: é ele que o ICleanupService lê.
        Model.IsSelected = value;

        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }
}

/// <summary>Grupo de alvos de limpeza (uma categoria) com seleção tri-estado.</summary>
public sealed partial class CleanupGroupViewModel : ObservableObject
{
    /// <summary>Cria o grupo a partir do resultado do scan.</summary>
    /// <param name="group">Grupo retornado pelo <see cref="ICleanupService"/>.</param>
    public CleanupGroupViewModel(CleanupCategoryGroup group)
    {
        Category = group.Category;
        DisplayName = group.DisplayName;
        TotalBytes = group.TotalBytes;

        foreach (var target in group.Targets)
        {
            var item = new CleanupTargetViewModel(target);
            item.SelectionChanged += (_, _) => Recalculate();
            Targets.Add(item);
        }

        _isGroupSelected = Targets.Count > 0 && Targets.All(t => t.IsSelected);

        Recalculate();
    }

    /// <summary>Categoria do grupo.</summary>
    public IssueCategory Category { get; }

    /// <summary>Nome exibido.</summary>
    public string DisplayName { get; }

    /// <summary>Total de bytes do grupo (independente da seleção).</summary>
    public long TotalBytes { get; }

    /// <summary>Alvos do grupo.</summary>
    public ObservableCollection<CleanupTargetViewModel> Targets { get; } = [];

    /// <summary>Total formatado.</summary>
    public string TotalFormatted => ByteFormat.Format(TotalBytes);

    /// <summary>Seleção tri-estado do cabeçalho do grupo.</summary>
    /// <remarks>
    /// <c>null</c> = parcialmente selecionado; a atualização é feita por
    /// <see cref="Recalculate"/> sem reentrar na propagação para os filhos.
    /// </remarks>
    [ObservableProperty]
    private bool? _isGroupSelected;

    /// <summary>Bytes selecionados formatados.</summary>
    public string SelectedFormatted => ByteFormat.Format(SelectedBytes);

    /// <summary>Bytes selecionados.</summary>
    public long SelectedBytes { get; private set; }

    /// <summary>Quantidade de alvos selecionados.</summary>
    public int SelectedCount { get; private set; }

    /// <summary>Disparado quando qualquer seleção do grupo muda.</summary>
    public event EventHandler? SelectionChanged;

    partial void OnIsGroupSelectedChanged(bool? value)
    {
        if (value is not { } desired)
        {
            return;
        }

        foreach (var target in Targets)
        {
            target.IsSelected = desired;
        }

        Recalculate();
    }

    /// <summary>Recalcula totais e o estado tri-estado do grupo.</summary>
    private void Recalculate()
    {
        SelectedBytes = Targets.Where(t => t.IsSelected).Sum(t => t.EstimatedBytes);
        SelectedCount = Targets.Count(t => t.IsSelected);

        var all = Targets.Count > 0 && Targets.All(t => t.IsSelected);
        var none = Targets.All(t => !t.IsSelected);

        SetGroupState(all ? true : none ? false : null);

        OnPropertyChanged(nameof(SelectedFormatted));
        OnPropertyChanged(nameof(SelectedBytes));
        OnPropertyChanged(nameof(SelectedCount));

        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Atualiza o tri-estado sem disparar a propagação para os filhos.</summary>
    private void SetGroupState(bool? state)
    {
        if (_isGroupSelected == state)
        {
            return;
        }

        // Campo direto: OnIsGroupSelectedChanged não roda (evita loop).
        _isGroupSelected = state;

        OnPropertyChanged(nameof(IsGroupSelected));
    }
}

/// <summary>Programa de inicialização com estado de UI.</summary>
public sealed partial class StartupProgramViewModel : ObservableObject
{
    private readonly ILocalizationService _localization;

    /// <summary>Cria o wrapper.</summary>
    /// <param name="model">Programa de inicialização.</param>
    /// <param name="localization">Localização.</param>
    public StartupProgramViewModel(StartupProgram model, ILocalizationService localization)
    {
        Model = model;
        _localization = localization;
        _isEnabled = model.IsEnabled;
    }

    /// <summary>Modelo de domínio.</summary>
    public StartupProgram Model { get; }

    /// <summary>Nome.</summary>
    public string Name => Model.Name;

    /// <summary>Fabricante.</summary>
    public string Publisher => Model.Publisher;

    /// <summary>Comando executado no boot.</summary>
    public string Command => Model.Command;

    /// <summary>Impacto estimado.</summary>
    public StartupImpact Impact => Model.Impact;

    /// <summary>Impacto localizado.</summary>
    public string ImpactText => _localization.GetEnumText(Model.Impact);

    /// <summary>Tempo estimado em segundos.</summary>
    public string EstimatedTimeText => Model.EstimatedSeconds > 0 ? $"{Model.EstimatedSeconds:F1}s" : "—";

    /// <summary>Localização localizada.</summary>
    public string LocationText => _localization.GetEnumText(Model.Location);

    /// <summary>Indica item órfão (executável ausente).</summary>
    public bool IsOrphaned => !Model.FileExists;

    /// <summary>Indica se é seguro desativar.</summary>
    public bool IsSafeToDisable => Model.IsSafeToDisable;

    /// <summary>Se está ativo no boot.</summary>
    [ObservableProperty]
    private bool _isEnabled;

    /// <summary>Disparado quando o usuário alterna o item (a tela persiste via serviço).</summary>
    public event EventHandler<bool>? EnabledChanged;

    partial void OnIsEnabledChanged(bool value) => EnabledChanged?.Invoke(this, value);
}

/// <summary>Item de privacidade com seleção de aplicação.</summary>
public sealed partial class PrivacyItemViewModel : ObservableObject
{
    private readonly ILocalizationService _localization;

    /// <summary>Cria o wrapper.</summary>
    /// <param name="model">Item de privacidade.</param>
    /// <param name="localization">Localização.</param>
    public PrivacyItemViewModel(PrivacyItem model, ILocalizationService localization)
    {
        Model = model;
        _localization = localization;

        // Recomendados já vêm marcados — o usuário só desmarca o que não quer.
        _isToApply = model.IsRecommended && !model.IsProtected;
    }

    /// <summary>Modelo de domínio.</summary>
    public PrivacyItem Model { get; }

    /// <summary>Nome exibido.</summary>
    public string DisplayName => Model.DisplayName;

    /// <summary>Descrição.</summary>
    public string Description => Model.Description;

    /// <summary>Categoria localizada.</summary>
    public string CategoryText => _localization.GetEnumText(Model.Category);

    /// <summary>Categoria (agrupamento).</summary>
    public PrivacyCategory Category => Model.Category;

    /// <summary>Se a proteção já está aplicada.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    private bool _isProtected;

    /// <summary>Se é recomendado pelo produto.</summary>
    public bool IsRecommended => Model.IsRecommended;

    /// <summary>Se exige administrador.</summary>
    public bool RequiresAdmin => Model.RequiresAdmin;

    /// <summary>Aviso de risco (vazio quando não há).</summary>
    public string RiskNote => Model.RiskNote;

    /// <summary>Se há aviso de risco.</summary>
    public bool HasRiskNote => !string.IsNullOrWhiteSpace(Model.RiskNote);

    /// <summary>Se é reversível.</summary>
    public bool IsReversible => Model.IsReversible;

    /// <summary>Ganho de desempenho estimado (0-5).</summary>
    public int PerformanceGainScore => Model.PerformanceGainScore;

    /// <summary>Indica se o item será aplicado na próxima execução.</summary>
    [ObservableProperty]
    private bool _isToApply;

    /// <summary>Texto do estado atual.</summary>
    public string StatusText => IsProtected
        ? _localization["Privacy_Protected"]
        : _localization["Privacy_Exposed"];

    /// <summary>Sincroniza o estado vindo do serviço (após aplicar/restaurar).</summary>
    /// <param name="model">Item atualizado.</param>
    public void SyncFrom(PrivacyItem model)
    {
        IsProtected = model.IsProtected;
        Model.IsProtected = model.IsProtected;
    }
}

/// <summary>Serviço do Windows com estado observável.</summary>
public sealed partial class WindowsServiceViewModel : ObservableObject
{
    private readonly ILocalizationService _localization;

    /// <summary>Cria o wrapper.</summary>
    /// <param name="model">Serviço.</param>
    /// <param name="localization">Localização.</param>
    public WindowsServiceViewModel(WindowsServiceInfo model, ILocalizationService localization)
    {
        Model = model;
        _localization = localization;
        _state = model.State;
        _startupKind = model.StartupKind;
    }

    /// <summary>Modelo de domínio.</summary>
    public WindowsServiceInfo Model { get; }

    /// <summary>Nome do serviço.</summary>
    public string ServiceName => Model.ServiceName;

    /// <summary>Nome amigável.</summary>
    public string DisplayName => Model.DisplayName;

    /// <summary>Descrição.</summary>
    public string Description => Model.Description;

    /// <summary>Estado atual.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StateText))]
    [NotifyPropertyChangedFor(nameof(IsRunning))]
    private ServiceState _state;

    /// <summary>Tipo de inicialização.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StartupKindText))]
    private ServiceStartupKind _startupKind;

    /// <summary>Se o serviço está em execução.</summary>
    public bool IsRunning => State == ServiceState.Running;

    /// <summary>Texto do estado.</summary>
    public string StateText => _localization.GetEnumText(State);

    /// <summary>Texto do tipo de inicialização.</summary>
    public string StartupKindText => _localization.GetEnumText(StartupKind);

    /// <summary>Se pode ser parado.</summary>
    public bool CanStop => Model.CanStop;

    /// <summary>Se é um serviço crítico do sistema.</summary>
    public bool IsCritical => Model.IsCritical;

    /// <summary>Serviços que dependem deste.</summary>
    public string DependentsText => Model.DependentServices.Count == 0 ? "—" : string.Join(", ", Model.DependentServices);
}

/// <summary>Ajuste do Modo Gamer com seleção observável.</summary>
public sealed partial class GameTweakViewModel : ObservableObject
{
    /// <summary>Cria o wrapper.</summary>
    /// <param name="model">Ajuste disponível.</param>
    public GameTweakViewModel(GameTweak model)
    {
        Model = model;
        _isEnabled = model.IsEnabled;
    }

    /// <summary>Modelo de domínio.</summary>
    public GameTweak Model { get; }

    /// <summary>Identificador.</summary>
    public string Id => Model.Id;

    /// <summary>Nome exibido.</summary>
    public string DisplayName => Model.DisplayName;

    /// <summary>Descrição do efeito.</summary>
    public string Description => Model.Description;

    /// <summary>Se exige administrador.</summary>
    public bool RequiresAdmin => Model.RequiresAdmin;

    /// <summary>Ganho estimado de FPS.</summary>
    public int EstimatedFpsGain => Model.EstimatedFpsGain;

    /// <summary>Se foi aplicado na última ativação.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ResultText))]
    private bool _wasApplied;

    /// <summary>Mensagem do último resultado.</summary>
    [ObservableProperty]
    private string _lastResult = string.Empty;

    /// <summary>Indica se o ajuste está habilitado para a próxima ativação.</summary>
    [ObservableProperty]
    private bool _isEnabled;

    /// <summary>Texto de resultado (mensagem ou vazio).</summary>
    public string ResultText => string.IsNullOrWhiteSpace(LastResult)
        ? (WasApplied ? LocalizationProxy.Instance["Common_Ok"] : "—")
        : LastResult;

    partial void OnIsEnabledChanged(bool value) => Model.IsEnabled = value;

    /// <summary>Atualiza o estado a partir do <see cref="GameModeState"/>.</summary>
    /// <param name="state">Estado retornado pelo serviço.</param>
    public void SyncFrom(GameModeState state)
    {
        var applied = state.AppliedTweaks.FirstOrDefault(t => t.Id == Model.Id);
        var failed = state.FailedTweaks.FirstOrDefault(t => t.Id == Model.Id);

        WasApplied = applied is not null;
        LastResult = applied?.LastResult ?? failed?.LastResult ?? state.Error ?? string.Empty;
    }
}
