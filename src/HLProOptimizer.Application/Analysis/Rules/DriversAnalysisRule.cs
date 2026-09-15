using HLProOptimizer.Core.Abstractions;
using HLProOptimizer.Core.Enums;
using HLProOptimizer.Core.Models;

namespace HLProOptimizer.Application.Analysis.Rules;

/// <summary>Detecta drivers desatualizados, genéricos ou em estado de erro.</summary>
public sealed class DriversAnalysisRule : IAnalysisRule
{
    private readonly ISystemInformationService _systemInformation;

    /// <summary>Cria a regra.</summary>
    /// <param name="systemInformation">Serviço de inventário do sistema.</param>
    public DriversAnalysisRule(ISystemInformationService systemInformation)
    {
        _systemInformation = systemInformation;
    }

    /// <inheritdoc />
    public string Id => "analysis.drivers";

    /// <inheritdoc />
    public string Name => "Verificar drivers";

    /// <inheritdoc />
    public IssueCategory Category => IssueCategory.Drivers;

    /// <inheritdoc />
    public int Order => 70;

    /// <inheritdoc />
    public bool RequiresAdmin => false;

    /// <inheritdoc />
    public double Weight => 0.9d;

    /// <inheritdoc />
    public async Task<IReadOnlyList<AnalysisIssue>> AnalyzeAsync(AnalysisContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var issues = new List<AnalysisIssue>();

        var problemDevices = context.ProblemDrivers
            ?? await _systemInformation.GetProblemDevicesAsync(cancellationToken).ConfigureAwait(false);

        if (problemDevices.Count > 0)
        {
            issues.Add(new AnalysisIssue(
                Id: "analysis.drivers.problems",
                Title: "Dispositivos com driver em erro",
                Description: $"{problemDevices.Count} dispositivo(s) reportam problema de driver: {string.Join(", ", problemDevices.Take(3).Select(d => $"{d.DeviceName} ({d.Status})"))}.",
                Category: Category,
                Severity: Severity.High,
                RecommendedAction: "Atualizar drivers",
                CanAutoFix: false,
                RequiresAdmin: true,
                ItemCount: problemDevices.Count));
        }

        // GPU com driver genérico da Microsoft = desempenho drasticamente reduzido.
        var genericGpu = context.Profile.Gpus.FirstOrDefault(g => g.IsGenericDriver);

        if (genericGpu is not null)
        {
            issues.Add(new AnalysisIssue(
                Id: "analysis.drivers.genericgpu",
                Title: "Driver de vídeo genérico detectado",
                Description: $"O adaptador '{genericGpu.Name}' está usando o driver básico da Microsoft. Instale o driver do fabricante (NVIDIA/AMD/Intel) para habilitar aceleração 3D.",
                Category: Category,
                Severity: Severity.Critical,
                RecommendedAction: "Instalar driver da GPU",
                CanAutoFix: false,
                RequiresAdmin: true,
                ItemCount: 1));
        }

        // Driver de GPU com mais de 12 meses tende a perder otimizações de jogos.
        var outdatedGpu = context.Profile.Gpus
            .Where(g => g.DriverDate is { } date && date < DateTime.Now.AddMonths(-12))
            .ToList();

        if (outdatedGpu.Count > 0)
        {
            issues.Add(new AnalysisIssue(
                Id: "analysis.drivers.outdatedgpu",
                Title: "Driver de vídeo desatualizado",
                Description: $"O driver de '{outdatedGpu[0].Name}' é de {outdatedGpu[0].DriverDate:MM/yyyy}. Drivers recentes trazem otimizações de FPS para jogos novos.",
                Category: Category,
                Severity: Severity.Medium,
                RecommendedAction: "Atualizar driver",
                CanAutoFix: false,
                RequiresAdmin: true,
                ItemCount: outdatedGpu.Count));
        }

        return issues;
    }
}
