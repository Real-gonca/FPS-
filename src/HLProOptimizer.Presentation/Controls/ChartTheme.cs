using System.Collections.ObjectModel;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;

namespace HLProOptimizer.Presentation.Controls;

/// <summary>
/// Fábrica de séries/eixos do LiveCharts2 já estilizados com o tema dark do produto.
/// </summary>
/// <remarks>
/// <para>
/// <b>DRY:</b> Dashboard e Monitoring precisam dos mesmos gráficos de linha
/// (gradiente azul → ciano, sem geometria, eixos discretos). Centralizar o estilo
/// aqui evita repetir <c>SolidColorPaint</c> em cada ViewModel e garante que uma
/// mudança de tema reflita em todos os gráficos.
/// </para>
/// <para>
/// As séries aceitam <see cref="ObservableCollection{T}"/>: o LiveCharts2 observa a
/// coleção e anima as atualizações (usamos janelas deslizantes de N pontos).
/// </para>
/// </remarks>
public static class ChartTheme
{
    /// <summary>Cor primária (azul).</summary>
    public const string Primary = "#3B82F6";

    /// <summary>Cor de destaque (ciano).</summary>
    public const string Accent = "#22D3EE";

    /// <summary>Cor de sucesso (verde).</summary>
    public const string Success = "#22C55E";

    /// <summary>Cor de aviso (âmbar).</summary>
    public const string Warning = "#F59E0B";

    /// <summary>Cor de perigo (vermelho).</summary>
    public const string Danger = "#EF4444";

    /// <summary>Cor de roxo (rede/GPU).</summary>
    public const string Purple = "#A78BFA";

    private static readonly SKColor GridColor = SKColor.Parse("#1B2537");
    private static readonly SKColor LabelColor = SKColor.Parse("#94A3B8");

    /// <summary>Cria uma série de linha para valores numéricos.</summary>
    /// <param name="values">Valores (tipicamente uma janela deslizante).</param>
    /// <param name="colorHex">Cor da linha (hexadecimal).</param>
    /// <param name="name">Nome exibido na legenda.</param>
    /// <param name="fillArea">Se preenche a área abaixo da linha com a cor em 20% de opacidade.</param>
    /// <returns>Série pronta para binding.</returns>
    public static ISeries Line(
        ObservableCollection<double> values,
        string colorHex,
        string? name = null,
        bool fillArea = true)
    {
        var color = SKColor.Parse(colorHex);

        return new LineSeries<double>
        {
            Name = name,
            Values = values,
            GeometrySize = 0,
            GeometryStrokeThickness = 0,
            LineSmoothness = 0.35,
            Fill = fillArea ? new SolidColorPaint(color.WithAlpha(46)) : null,
            Stroke = new SolidColorPaint(color) { StrokeThickness = 2f },
            ScalesYAt = 0
        };
    }

    /// <summary>Cria uma série de coluna (usada em breakdowns e comparações).</summary>
    /// <param name="values">Valores.</param>
    /// <param name="colorHex">Cor.</param>
    /// <param name="name">Nome da série.</param>
    /// <returns>Série de colunas.</returns>
    public static ISeries Column(ObservableCollection<double> values, string colorHex, string? name = null) =>
        new ColumnSeries<double>
        {
            Name = name,
            Values = values,
            Fill = new SolidColorPaint(SKColor.Parse(colorHex)),
            MaxBarWidth = 18,
            Rx = 4,
            Ry = 4
        };

    /// <summary>Eixo Y de percentual (0-100) com grade discreta.</summary>
    /// <param name="maxLimit">Limite superior (padrão 100).</param>
    /// <returns>Array com um eixo.</returns>
    public static Axis[] PercentAxes(double maxLimit = 100) =>
        [
            new Axis
            {
                MinLimit = 0,
                MaxLimit = maxLimit,
                ShowSeparatorLines = true,
                SeparatorsPaint = new SolidColorPaint(GridColor) { StrokeThickness = 1f },
                LabelsPaint = new SolidColorPaint(LabelColor),
                TextSize = 11,
                MinStep = 25,
                ForceStepToMin = false
            }
        ];

    /// <summary>Eixo Y livre (sem limite superior), para taxas em MB/s etc.</summary>
    /// <returns>Array com um eixo.</returns>
    public static Axis[] AutoAxes() =>
        [
            new Axis
            {
                MinLimit = 0,
                ShowSeparatorLines = true,
                SeparatorsPaint = new SolidColorPaint(GridColor) { StrokeThickness = 1f },
                LabelsPaint = new SolidColorPaint(LabelColor),
                TextSize = 11
            }
        ];

    /// <summary>Eixo X com rótulos de horário (janela deslizante).</summary>
    /// <param name="labels">Rótulos sincronizados com os valores.</param>
    /// <returns>Array com um eixo.</returns>
    public static Axis[] TimeAxes(ObservableCollection<string> labels) =>
        [
            new Axis
            {
                Labels = labels,
                LabelsPaint = new SolidColorPaint(LabelColor),
                TextSize = 10,
                ShowSeparatorLines = false,
                MinStep = 1,
                ForceStepToMin = true,
                UnitWidth = 1
            }
        ];

    /// <summary>Eixo X sem rótulos (para gráficos compactos).</summary>
    /// <returns>Array com um eixo.</returns>
    public static Axis[] HiddenAxes() =>
        [
            new Axis
            {
                ShowSeparatorLines = false,
                IsVisible = false,
                MinStep = 1,
                ForceStepToMin = true,
                UnitWidth = 1
            }
        ];

    /// <summary>
    /// Cria um gauge circular (donut) de duas fatias: o valor e o complemento.
    /// </summary>
    /// <param name="values">Coleção com exatamente um valor [0-100] (arco colorido).</param>
    /// <param name="remainder">Coleção com o complemento (100 - valor), que forma a trilha.</param>
    /// <param name="colorHex">Cor do arco preenchido.</param>
    /// <param name="innerRadius">Raio interno (espessura do anel).</param>
    /// <param name="outerRadius">Raio externo.</param>
    /// <returns>Duas séries de pizza (valor + trilha).</returns>
    /// <remarks>
    /// O LiveCharts2 não tem gauge nativo; duas <see cref="PieSeries{T}"/> com
    /// <c>InnerRadius</c> produzem um anel limpo e animado — mais simples (e mais
    /// bonito) do que desenhar arcos com <c>Path</c> + conversores de ângulo.
    /// </remarks>
    public static ISeries[] Gauge(
        ObservableCollection<double> values,
        ObservableCollection<double> remainder,
        string colorHex,
        double innerRadius = 54,
        double outerRadius = 72)
    {
        var color = SKColor.Parse(colorHex);
        var track = SKColor.Parse("#16202F");

        return
        [
            new PieSeries<double>
            {
                Values = values,
                InnerRadius = innerRadius,
                OuterRadius = outerRadius,
                Fill = new SolidColorPaint(color),
                HoverPushout = 0,
                MaxRadialColumnWidth = 0,
                DataLabelsPosition = LiveChartsCore.Measure.PolarLabelsPosition.ChartCenter,
                DataLabelsSize = 0
            },
            new PieSeries<double>
            {
                Values = remainder,
                InnerRadius = innerRadius,
                OuterRadius = outerRadius,
                Fill = new SolidColorPaint(track),
                HoverPushout = 0,
                MaxRadialColumnWidth = 0,
                DataLabelsSize = 0
            }
        ];
    }

    /// <summary>Paleta padrão das séries.</summary>
    /// <returns>Cores em formato SKColor.</returns>
    public static SKColor[] Palette() =>
        [SKColor.Parse(Primary), SKColor.Parse(Accent), SKColor.Parse(Success), SKColor.Parse(Warning), SKColor.Parse(Purple)];
}
