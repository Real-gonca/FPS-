using System.Windows.Controls;

namespace HLOptimizerPro.App.Views.Pages;

/// <summary>
/// Vista do Dashboard. Não tem lógica: todos os valores, timers e comandos estão no
/// <see cref="ViewModels.DashboardViewModel"/> (MVVM sem código escondido na View).
/// </summary>
public partial class DashboardView : UserControl
{
    public DashboardView()
    {
        InitializeComponent();
    }
}
