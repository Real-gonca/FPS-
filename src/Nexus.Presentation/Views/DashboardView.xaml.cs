using System.Windows;
using System.Windows.Controls;
using LiveChartsCore;

namespace Nexus.Presentation.Views;

public partial class DashboardView : UserControl
{
    private bool _axesConfigured;

    public DashboardView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_axesConfigured)
            return;
        _axesConfigured = true;

        // Eixos fixos (0–100 %); o X é "minutos desde o início da janela".
        // Os dados mudam via binding (ChartSeries) — os eixos não.
        TrendChart.XAxes = new[]
        {
            new ChartAxis { Unit = "min" },
        };
        TrendChart.YAxes = new[]
        {
            new ChartAxis { MinValue = 0, MaxValue = 100, Unit = "%" },
        };
    }
}
