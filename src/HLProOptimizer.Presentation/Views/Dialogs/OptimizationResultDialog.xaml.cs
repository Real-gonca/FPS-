using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using HLProOptimizer.Core.Abstractions;
using HLProOptimizer.Core.Models;

namespace HLProOptimizer.Presentation.Views.Dialogs;

/// <summary>
/// Resumo visual de uma otimização (ou limpeza) concluída.
/// </summary>
/// <remarks>
/// Quando <see cref="OptimizationResult.RequiresElevation"/> é verdadeiro, a janela
/// oferece "Reexecutar como administrador" — a elevação sob demanda do produto:
/// o usuário só vê o prompt de UAC se realmente quiser executar os passos protegidos.
/// </remarks>
public partial class OptimizationResultDialog : Window
{
    private readonly OptimizationResult _result;

    /// <summary>Cria o diálogo de resultado.</summary>
    /// <param name="result">Resultado da operação.</param>
    /// <param name="localization">Localização (título e subtítulo).</param>
    public OptimizationResultDialog(OptimizationResult result, ILocalizationService localization)
    {
        ArgumentNullException.ThrowIfNull(result);

        InitializeComponent();

        _result = result;

        TitleText.Text = result.IsFullySuccessful
            ? localization["Opt_Completed"]
            : localization["Msg_AppliedWithErrors"];

        SubtitleText.Text = $"{localization.GetEnumText(result.Mode)} · {localization.GetEnumText(result.Level)}";

        SuccessMetric.Text = result.SuccessCount.ToString();
        FreedMetric.Text = result.FreedFormatted;
        DurationMetric.Text = result.Duration.ToString(result.Duration.TotalMinutes >= 1 ? @"m\m\ s\s" : @"s\s");
        IssuesMetric.Text = (result.SkippedCount + result.FailedCount).ToString();

        StepList.ItemsSource = result.Steps;

        if (!result.IsFullySuccessful)
        {
            IconGlyph.Text = result.FailedCount > 0 ? "\u2716" : "\u26A0";
            IconGlyph.Foreground = (Brush)FindResource(result.FailedCount > 0 ? "Brush.Danger" : "Brush.Warning");
            IconHost.Background = (Brush)FindResource(result.FailedCount > 0 ? "Brush.Danger.Soft" : "Brush.Warning.Soft");
        }

        ElevationBanner.Visibility = result.RequiresElevation ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>True quando o usuário pediu a reexecução elevada.</summary>
    public bool RetryElevated { get; private set; }

    /// <summary>Resultado exibido (útil para o chamador registrar no histórico).</summary>
    public OptimizationResult Result => _result;

    private void OnRetryElevatedClick(object sender, RoutedEventArgs e)
    {
        RetryElevated = true;
        Close();
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    private void OnDragMove(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            try
            {
                DragMove();
            }
            catch (InvalidOperationException)
            {
                // Ignora chamadas fora do estado pressionado.
            }
        }
    }
}
