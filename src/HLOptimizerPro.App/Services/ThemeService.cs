using System.Windows;
using HLOptimizerPro.Core.Models;
using HLOptimizerPro.Core.Services;
using Microsoft.Extensions.Logging;

namespace HLOptimizerPro.App.Services;

/// <summary>
/// Aplica as preferências visuais do utilizador ao design system em tempo de execução:
/// escala da interface (DPI lógico), animações e modo escuro.
///
/// Nota técnica sobre a escala: alterar o DPI da aplicação em runtime é instável em WPF, por isso
/// aplicamos um <c>LayoutTransform</c> na raiz da janela (ScaleTransform). O resultado prático é o
/// mesmo (interface maior/menor) e é reversível sem reiniciar.
/// </summary>
public sealed class ThemeService
{
    private readonly ILogger<ThemeService> _logger;
    private readonly SettingsService _settings;

    public ThemeService(ILogger<ThemeService> logger, SettingsService settings)
    {
        _logger = logger;
        _settings = settings;
    }

    /// <summary>Escala atual (1.0 = 100 %).</summary>
    public double Scale { get; private set; } = 1.0;

    /// <summary>True quando as animações estão ativas.</summary>
    public bool AnimationsEnabled => _settings.Current.AnimationsEnabled;

    /// <summary>Aplica a escala à janela indicada.</summary>
    public void ApplyScale(Window window, double scale)
    {
        Scale = Math.Clamp(scale, 0.8, 1.5);

        if (window.Content is FrameworkElement root)
        {
            root.LayoutTransform = Math.Abs(Scale - 1.0) < 0.001
                ? Transform.Identity
                : new System.Windows.Media.ScaleTransform(Scale, Scale);
        }

        _logger.LogInformation("Escala da interface aplicada: {Scale:P0}", Scale);
    }

    /// <summary>Resolução real do ecrã (mostrada na Configuração).</summary>
    public (int Width, int Height, double Dpi) GetScreenInfo()
    {
        try
        {
            var window = Application.Current?.MainWindow;
            var source = window is not null
                ? PresentationSource.FromVisual(window)
                : null;

            if (source?.CompositionTarget is { } target)
            {
                var dpi = target.TransformToDevice.M11 * 96.0;
                return ((int)(SystemParameters.PrimaryScreenWidth * target.TransformToDevice.M11 / 1.0),
                        (int)(SystemParameters.PrimaryScreenHeight * target.TransformToDevice.M22),
                        Math.Round(dpi));
            }

            return ((int)SystemParameters.PrimaryScreenWidth, (int)SystemParameters.PrimaryScreenHeight, 96);
        }
        catch
        {
            return (0, 0, 96);
        }
    }

    /// <summary>Lista de escalas suportadas apresentada na Configuração.</summary>
    public static IReadOnlyList<(double Value, string Label)> ScaleOptions { get; } = new List<(double, string)>
    {
        (0.9, "90 % — mais conteúdo no ecrã"),
        (1.0, "100 % — predefinido"),
        (1.1, "110 %"),
        (1.25, "125 % — mais legível"),
        (1.5, "150 % — acessibilidade"),
    };
}
