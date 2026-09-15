using System.Diagnostics;
using System.Security.Cryptography.X509Certificates;
using HLProOptimizer.Core.Abstractions;
using HLProOptimizer.Core.Models;
using Microsoft.Extensions.Logging;

namespace HLProOptimizer.Infrastructure.Platform;

/// <summary>
/// Metadados de executáveis (fabricante, versão, assinatura) e resolução de
/// comandos de inicialização.
/// </summary>
public sealed class FileMetadataService : IFileMetadataService
{
    private readonly ILogger<FileMetadataService> _logger;

    /// <summary>Cria o serviço.</summary>
    /// <param name="logger">Logger.</param>
    public FileMetadataService(ILogger<FileMetadataService> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public FileMetadata? GetMetadata(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            return null;
        }

        try
        {
            var versionInfo = FileVersionInfo.GetVersionInfo(filePath);
            var isSigned = IsSigned(filePath);
            var publisher = ResolvePublisher(versionInfo, filePath);

            return new FileMetadata(
                filePath,
                publisher,
                versionInfo.ProductName,
                versionInfo.FileVersion,
                versionInfo.FileDescription,
                isSigned,
                new FileInfo(filePath).Length);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Não foi possível ler os metadados de {Path}.", filePath);
            return null;
        }
    }

    /// <inheritdoc />
    public string? ResolveExecutablePath(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return null;
        }

        var text = Environment.ExpandEnvironmentVariables(command).Trim();

        // Comandos interpretados (rundll32, regsvr32, cmd, powershell, wscript)
        // não têm um "executável do programa" significativo.
        var candidate = ExtractFirstToken(text);

        if (candidate is null)
        {
            return null;
        }

        if (Path.IsPathRooted(candidate))
        {
            return candidate;
        }

        // Resolve contra o PATH e as pastas comuns do Windows.
        var searchDirectories = new List<string>
        {
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            Environment.GetFolderPath(Environment.SpecialFolder.SystemX86),
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            Environment.CurrentDirectory
        };

        var pathVariable = Environment.GetEnvironmentVariable("PATH");

        if (!string.IsNullOrEmpty(pathVariable))
        {
            searchDirectories.AddRange(pathVariable.Split(';', StringSplitOptions.RemoveEmptyEntries));
        }

        foreach (var directory in searchDirectories)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(directory))
                {
                    continue;
                }

                var fullPath = Path.Combine(directory.Trim(), candidate);

                if (File.Exists(fullPath))
                {
                    return fullPath;
                }

                if (!Path.HasExtension(candidate) && File.Exists(fullPath + ".exe"))
                {
                    return fullPath + ".exe";
                }
            }
            catch (Exception)
            {
                // Caminho inválido no PATH: ignora.
            }
        }

        return null;
    }

    /// <inheritdoc />
    public void RevealInExplorer(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            using var process = new Process();

            process.StartInfo.FileName = "explorer.exe";
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.CreateNoWindow = true;

            process.StartInfo.Arguments = File.Exists(path)
                ? $"/select,\"{path}\""
                : $"\"{(Directory.Exists(path) ? path : Path.GetDirectoryName(path))}\"";

            process.Start();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Não foi possível abrir o Explorer em {Path}.", path);
        }
    }

    /// <inheritdoc />
    public bool IsSigned(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            return false;
        }

        try
        {
            var certificate = X509Certificate.CreateFromSignedFile(filePath);

            return certificate is not null && !string.IsNullOrEmpty(certificate.Subject);
        }
        catch (Exception)
        {
            // Arquivo não assinado lança CryptographicException - caso normal.
            return false;
        }
    }

    /// <summary>
    /// Determina o fabricante: prefere o signatário da assinatura digital
    /// (mais confiável) e cai para o CompanyName do recurso de versão.
    /// </summary>
    private string ResolvePublisher(FileVersionInfo versionInfo, string filePath)
    {
        try
        {
            var certificate = X509Certificate.CreateFromSignedFile(filePath);

            if (certificate is not null)
            {
                var subject = certificate.Subject;
                var commonName = subject
                    .Split(',')
                    .Select(part => part.Trim())
                    .FirstOrDefault(part => part.StartsWith("CN=", StringComparison.OrdinalIgnoreCase));

                if (!string.IsNullOrEmpty(commonName))
                {
                    return commonName[3..].Trim();
                }
            }
        }
        catch (Exception)
        {
            // Sem assinatura: segue para o CompanyName.
        }

        return string.IsNullOrWhiteSpace(versionInfo.CompanyName) ? "Desconhecido" : versionInfo.CompanyName;
    }

    /// <summary>
    /// Extrai o primeiro token de um comando, respeitando aspas
    /// (ex.: <c>"C:\Program Files\App\a.exe" --min</c> → <c>C:\Program Files\App\a.exe</c>).
    /// </summary>
    private static string? ExtractFirstToken(string command)
    {
        if (command.StartsWith('"'))
        {
            var end = command.IndexOf('"', 1);

            if (end > 1)
            {
                return command[1..end];
            }

            return null;
        }

        // Sem aspas: pega até o primeiro espaço que produza um caminho existente.
        var parts = command.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length == 0)
        {
            return null;
        }

        if (File.Exists(parts[0]))
        {
            return parts[0];
        }

        // Comandos como "C:\Arquivos de Programas\App.exe --arg" sem aspas:
        // reconstrói progressivamente até encontrar um arquivo existente.
        for (var i = parts.Length - 1; i >= 1; i--)
        {
            var candidate = string.Join(' ', parts.Take(i));

            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return parts[0];
    }
}
