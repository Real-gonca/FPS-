using Microsoft.Extensions.Logging;
using Nexus.Domain.Ports;

namespace Nexus.Infrastructure.Storage;

/// <summary>
/// Varrimento de tamanho de pasta com limite de tempo (spec §2.3:
/// "varrimento de pastas com limite de tempo (time-boxed), etiquetado como
/// 'aprox.' quando estimado").
///
/// Ficheiros com acesso negacado/instáveis são pulados (o valor é parcial
/// por natureza — por isso a UI sempre apresenta "recuperáveis ~X").
/// </summary>
public sealed class TempFolderStorageProbe : IStorageProbe
{
    private readonly ILogger<TempFolderStorageProbe> _log;

    public TempFolderStorageProbe(ILogger<TempFolderStorageProbe> log) => _log = log;

    public Task<StorageProbeResult> MeasureAsync(string rootPath, TimeSpan timeBox, CancellationToken ct = default)
    {
        if (!OperatingSystem.IsWindows() || !Directory.Exists(rootPath))
            return Task.FromResult(new StorageProbeResult(null, false, rootPath));

        return Task.Run(() =>
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeBox);

            double totalBytes = 0;
            bool timedOut = false;

            try
            {
                Summarize(rootPath, ref totalBytes, cts.Token);
            }
            catch (OperationCanceledException)
            {
                timedOut = true;
                _log.LogDebug("Varrimento de {Path} atingiu o limite de {Box}s → resultado 'aprox.').",
                    rootPath, timeBox.TotalSeconds);
            }

            return new StorageProbeResult(totalBytes, timedOut, rootPath);
        }, ct);
    }

    private static void Summarize(string directory, ref double totalBytes, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();

        foreach (string file in SafeGetFiles(directory))
        {
            token.ThrowIfCancellationRequested();
            try
            {
                totalBytes += new FileInfo(file).Length;
            }
            catch
            {
                // ficheiro protegido ou removido a meio do scan — ignorar
            }
        }

        foreach (string sub in SafeGetDirectories(directory))
        {
            token.ThrowIfCancellationRequested();
            Summarize(sub, ref totalBytes, token);
        }
    }

    private static string[] SafeGetFiles(string directory)
    {
        try
        {
            return Directory.GetFiles(directory);
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    private static string[] SafeGetDirectories(string directory)
    {
        try
        {
            return Directory.GetDirectories(directory);
        }
        catch
        {
            return Array.Empty<string>();
        }
    }
}
