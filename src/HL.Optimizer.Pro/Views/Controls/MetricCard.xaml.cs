using System.Windows;
using System.Windows.Controls;

namespace HL.Optimizer.Pro.Views.Controls;

public partial class MetricCard : UserControl
{
    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register("Title", typeof(string), typeof(MetricCard), new PropertyMetadata(""));
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register("Value", typeof(string), typeof(MetricCard), new PropertyMetadata(""));
    public static readonly DependencyProperty IconProperty = DependencyProperty.Register("Icon", typeof(string), typeof(MetricCard), new PropertyMetadata(""));
    public static readonly DependencyProperty PercentageProperty = DependencyProperty.Register("Percentage", typeof(double), typeof(MetricCard), new PropertyMetadata(0.0));

    public string Title { get => (string)GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public string Value { get => (string)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public string Icon { get => (string)GetValue(IconProperty); set => SetValue(IconProperty, value); }
    public double Percentage { get => (double)GetValue(PercentageProperty); set => SetValue(PercentageProperty, value); }

    public MetricCard()
    {
        InitializeComponent();
    }
}
