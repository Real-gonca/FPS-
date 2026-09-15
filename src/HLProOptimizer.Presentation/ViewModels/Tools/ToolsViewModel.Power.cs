using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HLProOptimizer.Core.Enums;
using HLProOptimizer.Core.Models;

namespace HLProOptimizer.Presentation.ViewModels.Tools;

/// <summary>Seção "Energia" da tela de Ferramentas.</summary>
/// <remarks>
/// O "Desempenho Máximo" (<c>Ultimate Performance</c>) não existe por padrão em todas as
/// edições do Windows: <see cref="IPowerPlanService.EnableUltimatePerformanceAsync"/>
/// duplica o plano de alto desempenho quando necessário e devolve o plano criado — a UI
/// mostra o resultado real em vez de prometer um plano que pode não existir.
/// </remarks>
public sealed partial class ToolsViewModel
{
    /// <summary>Planos de energia disponíveis.</summary>
    public ObservableCollection<PowerPlanInfo> PowerPlansList { get; } = [];

    /// <summary>Plano selecionado.</summary>
    [ObservableProperty]
    private PowerPlanInfo? _selectedPowerPlan;

    /// <summary>Texto do plano ativo.</summary>
    [ObservableProperty]
    private string _activePowerPlanText = string.Empty;

    /// <summary>Indica se os ajustes gamer de energia estão aplicados.</summary>
    [ObservableProperty]
    private bool _gamerPowerTweaksApplied;

    /// <summary>Carrega os planos de energia.</summary>
    [RelayCommand]
    private async Task LoadPowerPlansAsync()
    {
        await RunBusyAsync(async () =>
        {
            var plans = await PowerPlans.GetPlansAsync().ConfigureAwait(true);
            var active = await PowerPlans.GetActivePlanAsync().ConfigureAwait(true);

            PowerPlansList.Clear();

            foreach (var plan in plans)
            {
                PowerPlansList.Add(plan);
            }

            ActivePowerPlanText = active is null
                ? L("Common_NotAvailable")
                : $"{active.Name} ({Localization.GetEnumText(active.Kind)})";

            SelectedPowerPlan = active ?? PowerPlansList.FirstOrDefault();
        }, L("Common_Loading")).ConfigureAwait(true);
    }

    /// <summary>Ativa o plano selecionado.</summary>
    /// <param name="plan">Plano (ou o selecionado).</param>
    [RelayCommand]
    private async Task ActivatePowerPlanAsync(PowerPlanInfo? plan)
    {
        var target = plan ?? SelectedPowerPlan;

        if (target is null)
        {
            return;
        }

        var applied = await ExecuteSimpleAsync(
            ct => PowerPlans.ActivateAsync(target.Guid, ct),
            L("Tools_Power"),
            ActionKind.Power).ConfigureAwait(true);

        if (applied)
        {
            await LoadPowerPlansAsync().ConfigureAwait(true);
        }
    }

    /// <summary>Habilita (ou cria) o plano de Desempenho Máximo e o ativa.</summary>
    [RelayCommand]
    private async Task EnableUltimatePerformanceAsync()
    {
        await RunBusyAsync(async () =>
        {
            var plan = await PowerPlans.EnableUltimatePerformanceAsync().ConfigureAwait(true);

            if (plan is null)
            {
                StatusMessage = L("Tools_UltimateUnavailable");
                AppendOutput($"{L("Tools_UltimatePerformance")}: {L("Tools_UltimateUnavailable")}");
                return;
            }

            await PowerPlans.ActivateAsync(plan.Guid).ConfigureAwait(true);

            await _history.RecordAsync(ActionKind.Power, L("Tools_UltimatePerformance"), plan.Name, success: true)
                .ConfigureAwait(true);

            StatusMessage = LF("Tools_UltimateEnabled", plan.Name);

            await LoadPowerPlansAsync().ConfigureAwait(true);
        }, L("Common_Loading")).ConfigureAwait(true);
    }

    /// <summary>Aplica os ajustes de energia do modo gamer.</summary>
    [RelayCommand]
    private async Task ApplyGamerPowerTweaksAsync()
    {
        var applied = await ExecuteSimpleAsync(
            ct => PowerPlans.ApplyGamerPowerTweaksAsync(ct),
            L("Tools_GamerPowerTweaks"),
            ActionKind.Power,
            requiresAdmin: true).ConfigureAwait(true);

        GamerPowerTweaksApplied = applied;
    }

    /// <summary>Reverte os ajustes de energia do modo gamer.</summary>
    [RelayCommand]
    private async Task RevertGamerPowerTweaksAsync()
    {
        var reverted = await ExecuteSimpleAsync(
            ct => PowerPlans.RevertGamerPowerTweaksAsync(ct),
            L("Tools_GamerPowerTweaks"),
            ActionKind.Power,
            requiresAdmin: true).ConfigureAwait(true);

        GamerPowerTweaksApplied = !reverted;
    }

    /// <summary>Restaura o plano de energia anterior.</summary>
    /// <param name="planGuid">GUID do plano (nulo = padrão do Windows).</param>
    [RelayCommand]
    private async Task RestorePowerPlanAsync(string? planGuid)
    {
        var applied = await ExecuteSimpleAsync(
            ct => PowerPlans.RestorePlanAsync(planGuid, ct),
            L("Tools_Power"),
            ActionKind.Power).ConfigureAwait(true);

        if (applied)
        {
            await LoadPowerPlansAsync().ConfigureAwait(true);
        }
    }

    /// <summary>Abre as opções de energia do Windows.</summary>
    [RelayCommand]
    private Task OpenPowerOptionsAsync() => OpenExternalAsync("powercfg.cpl");
}
