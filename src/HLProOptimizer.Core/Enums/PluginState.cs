namespace HLProOptimizer.Core.Enums;

/// <summary>Estado de um plugin instalado.</summary>
public enum PluginState
{
    /// <summary>Disponível mas não carregado.</summary>
    Installed = 0,

    /// <summary>Carregado e ativo.</summary>
    Enabled = 1,

    /// <summary>Desativado pelo usuário.</summary>
    Disabled = 2,

    /// <summary>Falha ao carregar (versão incompatível, exceção).</summary>
    Failed = 3
}
