using HLProOptimizer.Core.Abstractions;
using HLProOptimizer.Core.Enums;
using HLProOptimizer.Core.Models;

namespace HLProOptimizer.Application.Analysis.Rules;

/// <summary>Avalia a postura de segurança: antivírus, firewall, UAC e updates.</summary>
public sealed class SecurityAnalysisRule : IAnalysisRule
{
    private readonly ISystemInformationService _systemInformation;

    /// <summary>Cria a regra.</summary>
    /// <param name="systemInformation">Serviço de inventário do sistema.</param>
    public SecurityAnalysisRule(ISystemInformationService systemInformation)
    {
        _systemInformation = systemInformation;
    }

    /// <inheritdoc />
    public string Id => "analysis.security";

    /// <inheritdoc />
    public string Name => "Verificar segurança";

    /// <inheritdoc />
    public IssueCategory Category => IssueCategory.Security;

    /// <inheritdoc />
    public int Order => 100;

    /// <inheritdoc />
    public bool RequiresAdmin => false;

    /// <inheritdoc />
    public double Weight => 1.0d;

    /// <inheritdoc />
    public async Task<IReadOnlyList<AnalysisIssue>> AnalyzeAsync(AnalysisContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var issues = new List<AnalysisIssue>();

        var antivirus = await _systemInformation.IsAntivirusActiveAsync(cancellationToken).ConfigureAwait(false);
        var firewall = await _systemInformation.IsFirewallActiveAsync(cancellationToken).ConfigureAwait(false);
        var uac = await _systemInformation.IsUacEnabledAsync(cancellationToken).ConfigureAwait(false);

        if (!antivirus)
        {
            issues.Add(new AnalysisIssue(
                Id: "analysis.security.antivirus",
                Title: "Nenhum antivírus ativo",
                Description: "Não foi detectado antivírus em execução. Ative o Windows Defender ou instale uma solução de segurança.",
                Category: Category,
                Severity: Severity.Critical,
                RecommendedAction: "Ativar proteção",
                CanAutoFix: false,
                RequiresAdmin: true));
        }

        if (!firewall)
        {
            issues.Add(new AnalysisIssue(
                Id: "analysis.security.firewall",
                Title: "Firewall do Windows desativado",
                Description: "O firewall está desligado em ao menos um perfil de rede, expondo o computador a acessos não solicitados.",
                Category: Category,
                Severity: Severity.High,
                RecommendedAction: "Ativar firewall",
                CanAutoFix: false,
                RequiresAdmin: true));
        }

        if (!uac)
        {
            issues.Add(new AnalysisIssue(
                Id: "analysis.security.uac",
                Title: "UAC desativado",
                Description: "O Controle de Conta de Usuário está desligado: qualquer programa pode alterar o sistema sem aviso.",
                Category: Category,
                Severity: Severity.High,
                RecommendedAction: "Ativar UAC",
                CanAutoFix: false,
                RequiresAdmin: true));
        }

        // Registro remoto habilitado é uma porta de entrada clássica para malware.
        var remoteRegistry = (context.Services ?? []).FirstOrDefault(s =>
            string.Equals(s.ServiceName, "RemoteRegistry", StringComparison.OrdinalIgnoreCase));

        if (remoteRegistry is { IsRunning: true })
        {
            issues.Add(new AnalysisIssue(
                Id: "analysis.security.remoteregistry",
                Title: "Serviço de registro remoto em execução",
                Description: "O serviço RemoteRegistry permite alterar o registro pela rede. Ele deve permanecer desativado em computadores pessoais.",
                Category: Category,
                Severity: Severity.High,
                RecommendedAction: "Desativar serviço",
                CanAutoFix: true,
                RequiresAdmin: true,
                ItemCount: 1,
                FixIdentifier: Optimization.OptimizationStepIds.Services));
        }

        context.Data["Security.AntivirusActive"] = antivirus;
        context.Data["Security.FirewallActive"] = firewall;
        context.Data["Security.UacEnabled"] = uac;

        return issues;
    }
}
