using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HL.Optimizer.Pro.Core.Interfaces;
using HL.Optimizer.Pro.Core.Models;
using System.Collections.ObjectModel;

namespace HL.Optimizer.Pro.ViewModels;

public partial class TweaksViewModel : BaseViewModel
{
    private readonly IOptimizationService _optimization;
    private readonly IRegistryService _registry;

    [ObservableProperty] private ObservableCollection<TweakItem> _allTweaks = new();
    [ObservableProperty] private ObservableCollection<TweakItem> _filteredTweaks = new();
    [ObservableProperty] private string _selectedCategory = "Todos";
    [ObservableProperty] private string _searchQuery = "";

    public List<string> Categories { get; } = new() { "Todos", "Windows", "Privacidade", "Interface", "Desempenho", "Rede", "Input", "Explorer", "Sistema" };

    public TweaksViewModel(IOptimizationService optimization, IRegistryService registry)
    {
        _optimization = optimization;
        _registry = registry;
    }

    public override async Task InitializeAsync()
    {
        IsLoading = true;
        try
        {
            var tweaks = await LoadTweaksAsync();
            AllTweaks = new ObservableCollection<TweakItem>(tweaks);
            Filter();
        }
        finally { IsLoading = false; }
    }

    private async Task<List<TweakItem>> LoadTweaksAsync()
    {
        var list = new List<TweakItem>();

        // Windows
        list.Add(new TweakItem { Id = "tweak_anim", Name = "Desativar animações desnecessárias", Description = "Remove animações do Windows para interface mais rápida", Category = OptimizationCategory.Interface, Impact = "Baixo", Reversible = true, RecommendedState = "Desativado", CurrentState = "Ativo", Risk = OptimizationRisk.Seguro });
        list.Add(new TweakItem { Id = "tweak_transparency", Name = "Desativar transparência", Description = "Desativa efeitos de transparência do Windows 10/11", Category = OptimizationCategory.Interface, Impact = "Baixo", Reversible = true, RecommendedState = "Desativado", CurrentState = "Ativo", Risk = OptimizationRisk.Seguro });
        list.Add(new TweakItem { Id = "tweak_menudelay", Name = "Acelerar exibição de menus", Description = "MenuShowDelay de 400ms para 0ms", Category = OptimizationCategory.Interface, Impact = "Baixo", Reversible = true, RecommendedState = "0ms", CurrentState = "400ms", Risk = OptimizationRisk.Seguro });
        list.Add(new TweakItem { Id = "tweak_startupdelay", Name = "Remover delay de inicialização", Description = "Remove delay de 10s após login", Category = OptimizationCategory.Sistema, Impact = "Médio", Reversible = true, RecommendedState = "Sem delay", CurrentState = "Com delay", Risk = OptimizationRisk.Seguro });

        // Privacidade
        list.Add(new TweakItem { Id = "tweak_telemetry", Name = "Desativar telemetria", Description = "Reduz envio de dados de diagnóstico", Category = OptimizationCategory.Privacidade, Impact = "Baixo", Reversible = true, RecommendedState = "Desativado", CurrentState = "Ativo", Risk = OptimizationRisk.Seguro });
        list.Add(new TweakItem { Id = "tweak_adid", Name = "Desativar ID de publicidade", Description = "Desativa rastreamento para anúncios", Category = OptimizationCategory.Privacidade, Impact = "Baixo", Reversible = true, RecommendedState = "Desativado", CurrentState = "Ativo", Risk = OptimizationRisk.Seguro });
        list.Add(new TweakItem { Id = "tweak_feedback", Name = "Desativar pedidos de feedback", Description = "Para de pedir feedback sobre o Windows", Category = OptimizationCategory.Privacidade, Impact = "Baixo", Reversible = true, RecommendedState = "Desativado", CurrentState = "Ativo", Risk = OptimizationRisk.Seguro });

        // Desempenho
        list.Add(new TweakItem { Id = "tweak_prefetch", Name = "Otimizar Prefetch para SSD", Description = "Desativa prefetch se SSD detectado", Category = OptimizationCategory.Desempenho, Impact = "Médio", Reversible = true, RecommendedState = "Otimizado", CurrentState = "Padrão", Risk = OptimizationRisk.Moderado });
        list.Add(new TweakItem { Id = "tweak_large_cache", Name = "Priorizar programas em cache", Description = "System cache otimizado para programas", Category = OptimizationCategory.Desempenho, Impact = "Médio", Reversible = true, RecommendedState = "Programas", CurrentState = "Sistema", Risk = OptimizationRisk.Seguro });
        list.Add(new TweakItem { Id = "tweak_priority", Name = "Ajustar separação de prioridade", Description = "Win32PrioritySeparation para melhor responsividade", Category = OptimizationCategory.Desempenho, Impact = "Médio", Reversible = true, RecommendedState = "38", CurrentState = "2", Risk = OptimizationRisk.Seguro });

        // Rede
        list.Add(new TweakItem { Id = "tweak_nagle", Name = "Desativar Nagle para jogos", Description = "Reduz latência em jogos online", Category = OptimizationCategory.Rede, Impact = "Médio", Reversible = true, RecommendedState = "Desativado", CurrentState = "Ativo", Risk = OptimizationRisk.Moderado, RequiresAdmin = true });
        list.Add(new TweakItem { Id = "tweak_autotuning", Name = "Otimizar Auto-Tuning", Description = "Melhora throughput de rede", Category = OptimizationCategory.Rede, Impact = "Baixo", Reversible = true, RecommendedState = "Normal", CurrentState = "Normal", Risk = OptimizationRisk.Seguro, RequiresAdmin = true });

        // Explorer
        list.Add(new TweakItem { Id = "tweak_show_ext", Name = "Mostrar extensões de arquivos", Description = "Mostra extensões no Explorer", Category = OptimizationCategory.Interface, Impact = "Baixo", Reversible = true, RecommendedState = "Visível", CurrentState = "Oculto", Risk = OptimizationRisk.Seguro });
        list.Add(new TweakItem { Id = "tweak_quickaccess", Name = "Abrir Explorer em Este Computador", Description = "Em vez de Acesso Rápido", Category = OptimizationCategory.Interface, Impact = "Baixo", Reversible = true, RecommendedState = "Este Computador", CurrentState = "Acesso Rápido", Risk = OptimizationRisk.Seguro });

        return list;
    }

    partial void OnSelectedCategoryChanged(string value) => Filter();
    partial void OnSearchQueryChanged(string value) => Filter();

    private void Filter()
    {
        var query = AllTweaks.AsEnumerable();
        if (SelectedCategory != "Todos")
        {
            if (Enum.TryParse<OptimizationCategory>(SelectedCategory, true, out var cat))
                query = query.Where(t => t.Category == cat);
            else
                query = query.Where(t => t.Category.ToString().Contains(SelectedCategory, StringComparison.OrdinalIgnoreCase));
        }
        if (!string.IsNullOrWhiteSpace(SearchQuery))
        {
            query = query.Where(t => t.Name.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase) || t.Description.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase));
        }
        FilteredTweaks = new ObservableCollection<TweakItem>(query);
    }

    [RelayCommand]
    private async Task ApplyTweak(TweakItem tweak)
    {
        if (tweak == null) return;
        try
        {
            IsLoading = true;
            StatusMessage = $"Aplicando {tweak.Name}...";
            // Simulate apply - in real would call registry
            await Task.Delay(500);
            tweak.CurrentState = tweak.RecommendedState;
            StatusMessage = $"{tweak.Name} aplicado com sucesso";
            Filter();
        }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    private async Task RevertTweak(TweakItem tweak)
    {
        if (tweak == null) return;
        try
        {
            IsLoading = true;
            StatusMessage = $"Revertendo {tweak.Name}...";
            await Task.Delay(500);
            tweak.CurrentState = "Padrão";
            StatusMessage = $"{tweak.Name} revertido";
            Filter();
        }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    private void SetCategory(string category)
    {
        SelectedCategory = category;
    }
}
