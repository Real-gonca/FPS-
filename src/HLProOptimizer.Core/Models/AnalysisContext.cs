namespace HLProOptimizer.Core.Models;

/// <summary>
/// Contexto fornecido às regras de análise. Contém o perfil do sistema já
/// coletado e caches compartilhados, evitando chamadas WMI duplicadas.
/// </summary>
public sealed class AnalysisContext
{
    /// <summary>Cria o contexto com o perfil do sistema e as configurações.</summary>
    public AnalysisContext(SystemProfile profile, AppSettings settings)
    {
        Profile = profile;
        Settings = settings;
    }

    /// <summary>Perfil de hardware/SO.</summary>
    public SystemProfile Profile { get; }

    /// <summary>Configurações do usuário (define o nível de agressividade).</summary>
    public AppSettings Settings { get; }

    /// <summary>Programas de inicialização (coletados uma vez, reutilizados por várias regras).</summary>
    public IReadOnlyList<StartupProgram>? StartupPrograms { get; set; }

    /// <summary>Serviços Windows (coletados uma vez).</summary>
    public IReadOnlyList<WindowsServiceInfo>? Services { get; set; }

    /// <summary>Alvos de limpeza encontrados (coletados uma vez).</summary>
    public IReadOnlyList<CleanupTarget>? CleanupTargets { get; set; }

    /// <summary>Itens de privacidade (coletados uma vez).</summary>
    public IReadOnlyList<PrivacyItem>? PrivacyItems { get; set; }

    /// <summary>Drivers com problema (coletados uma vez).</summary>
    public IReadOnlyList<DriverInfo>? ProblemDrivers { get; set; }

    /// <summary>Cache livre para dados adicionais coletados por regras específicas.</summary>
    public Dictionary<string, object?> Data { get; } = new(StringComparer.OrdinalIgnoreCase);
}
