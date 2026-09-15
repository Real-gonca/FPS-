using HLProOptimizer.Core.Enums;

namespace HLProOptimizer.Core.Models;

/// <summary>Metadados de um plugin instalado (tela Configurações &gt; Plugins).</summary>
public sealed class PluginDescriptor
{
    /// <summary>Identificador único do plugin.</summary>
    public required string Id { get; init; }

    /// <summary>Nome exibido.</summary>
    public required string Name { get; init; }

    /// <summary>Versão.</summary>
    public string Version { get; init; } = "1.0.0";

    /// <summary>Autor.</summary>
    public string Author { get; init; } = "Desconhecido";

    /// <summary>Descrição curta.</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>Caminho da DLL carregada.</summary>
    public string FilePath { get; init; } = string.Empty;

    /// <summary>Estado atual.</summary>
    public PluginState State { get; init; } = PluginState.Installed;

    /// <summary>Mensagem de erro de carregamento, quando houver.</summary>
    public string? LoadError { get; init; }

    /// <summary>Se o plugin está ativo.</summary>
    public bool IsEnabled => State == PluginState.Enabled;
}
