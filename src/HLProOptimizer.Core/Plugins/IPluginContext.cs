using HLProOptimizer.Core.Abstractions;

namespace HLProOptimizer.Core.Plugins;

/// <summary>
/// Superfície controlada do host exposta aos plugins (evita que plugins recebam
/// o container de DI inteiro - Interface Segregation Principle).
/// </summary>
public interface IPluginContext
{
    /// <summary>Versão do host.</summary>
    string HostVersion { get; }

    /// <summary>Serviço de log.</summary>
    IPluginLogger Logger { get; }

    /// <summary>Acesso ao registro.</summary>
    IRegistryService Registry { get; }

    /// <summary>Acesso ao sistema de arquivos.</summary>
    IFileSystemService FileSystem { get; }

    /// <summary>Execução de comandos externos.</summary>
    ICommandRunner CommandRunner { get; }

    /// <summary>Gerenciador de serviços.</summary>
    IServiceManager Services { get; }

    /// <summary>Indica se o host está elevado.</summary>
    bool IsElevated { get; }
}

/// <summary>Log simples disponível para plugins.</summary>
public interface IPluginLogger
{
    /// <summary>Registra uma mensagem informativa.</summary>
    /// <param name="message">Mensagem.</param>
    void Info(string message);

    /// <summary>Registra um aviso.</summary>
    /// <param name="message">Mensagem.</param>
    void Warning(string message);

    /// <summary>Registra um erro.</summary>
    /// <param name="message">Mensagem.</param>
    /// <param name="exception">Exceção relacionada.</param>
    void Error(string message, Exception? exception = null);
}
