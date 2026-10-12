using System.Windows;
using HLOptimizerPro.App.ViewModels;
using HLOptimizerPro.App.Views.Pages;

namespace HLOptimizerPro.App.Services;

/// <summary>
/// Liga cada ViewModel à sua View.
///
/// Porque não usamos DataTemplates implícitos no App.xaml? Porque um mapa explícito (um
/// <c>switch</c> em código) é fácil de ler, não falha silenciosamente e permite mostrar um erro
/// claro quando alguém cria uma página e se esquece de a registar aqui.
/// </summary>
public static class ViewLocator
{
    /// <summary>Constrói a View correspondente ao ViewModel indicado.</summary>
    public static FrameworkElement? Resolve(ViewModelBase? viewModel) => viewModel switch
    {
        null => null,
        DashboardViewModel vm => new DashboardView { DataContext = vm },
        AssessmentViewModel vm => new AssessmentView { DataContext = vm },
        MemoryViewModel vm => new MemoryView { DataContext = vm },
        NetworkViewModel vm => new NetworkView { DataContext = vm },
        AppTunerViewModel vm => new AppTunerView { DataContext = vm },
        TweaksViewModel vm => new TweaksView { DataContext = vm },
        GameModeViewModel vm => new GameModeView { DataContext = vm },
        LatencyViewModel vm => new LatencyView { DataContext = vm },
        OptimizationsViewModel vm => new OptimizationsView { DataContext = vm },
        PcBoostViewModel vm => new PcBoostView { DataContext = vm },
        RecommendationsViewModel vm => new RecommendationsView { DataContext = vm },
        SettingsViewModel vm => new SettingsView { DataContext = vm },
        HelpViewModel vm => new HelpView { DataContext = vm },
        AboutViewModel vm => new AboutView { DataContext = vm },
        _ => BuildMissingView(viewModel),
    };

    /// <summary>View de recurso: nunca deixamos o utilizador com um ecrã em branco.</summary>
    private static FrameworkElement BuildMissingView(ViewModelBase viewModel)
        => new MissingPageView
        {
            DataContext = viewModel,
            Message = $"A página «{viewModel.Title}» ainda não tem vista associada.",
        };
}
