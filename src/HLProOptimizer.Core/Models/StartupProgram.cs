using HLProOptimizer.Core.Enums;

namespace HLProOptimizer.Core.Models;

/// <summary>Programa configurado para iniciar com o Windows.</summary>
public sealed class StartupProgram
{
    /// <summary>Identificador estável (hash de localização+nome).</summary>
    public required string Id { get; init; }

    /// <summary>Nome exibido (valor da chave ou nome do atalho).</summary>
    public required string Name { get; init; }

    /// <summary>Comando completo executado no boot.</summary>
    public required string Command { get; init; }

    /// <summary>Caminho do executável extraído do comando (quando resolvível).</summary>
    public string? ExecutablePath { get; init; }

    /// <summary>Argumentos de linha de comando.</summary>
    public string Arguments { get; init; } = string.Empty;

    /// <summary>Fabricante (do arquivo assinado ou heurística de nome).</summary>
    public string Publisher { get; init; } = "Desconhecido";

    /// <summary>Onde o item está registrado.</summary>
    public StartupLocation Location { get; init; }

    /// <summary>Impacto estimado no boot.</summary>
    public StartupImpact Impact { get; init; } = StartupImpact.Unknown;

    /// <summary>Tempo de inicialização estimado em segundos.</summary>
    public double EstimatedSeconds { get; init; }

    /// <summary>Se está ativo.</summary>
    public bool IsEnabled { get; init; } = true;

    /// <summary>Se o executável existe no disco (item órfão quando falso).</summary>
    public bool FileExists { get; init; } = true;

    /// <summary>Se desativar este item é considerado seguro.</summary>
    public bool IsSafeToDisable { get; init; } = true;

    /// <summary>Caminho real usado para "Abrir localização do arquivo".</summary>
    public string? ResolvedLocationPath { get; init; }
}
