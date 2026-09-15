namespace HLProOptimizer.Core.Abstractions;

/// <summary>
/// Caminhos de dados do aplicativo (AppData\Roaming\HLProOptimizer e derivados).
/// Centraliza a resolução para que nenhum serviço construa caminhos manualmente.
/// </summary>
public interface ISystemPaths
{
    /// <summary>Diretório raiz de dados do aplicativo.</summary>
    string ApplicationDataDirectory { get; }

    /// <summary>Diretório de logs (Serilog).</summary>
    string LogDirectory { get; }

    /// <summary>Caminho completo do banco SQLite.</summary>
    string DatabasePath { get; }

    /// <summary>Diretório de backups (registro, hosts, configurações).</summary>
    string BackupDirectory { get; }

    /// <summary>Diretório de plugins (.dll).</summary>
    string PluginDirectory { get; }

    /// <summary>Diretório temporário de trabalho do aplicativo.</summary>
    string TempDirectory { get; }

    /// <summary>Caminho do arquivo de configurações JSON.</summary>
    string SettingsFilePath { get; }

    /// <summary>Caminho do arquivo hosts do Windows.</summary>
    string HostsFilePath { get; }

    /// <summary>Caminho do instalador/executável atual.</summary>
    string ExecutablePath { get; }

    /// <summary>Garante que todos os diretórios acima existam.</summary>
    void EnsureCreated();
}
