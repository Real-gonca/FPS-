namespace HLProOptimizer.Application.Elevation;

/// <summary>
/// <see cref="IProgress{T}"/> que anexa cada mensagem a um arquivo, permitindo
/// que o processo não elevado acompanhe em tempo real o log de uma operação
/// executada em um processo elevado (elevação sob demanda).
/// </summary>
public sealed class FileProgressWriter : IProgress<string>, IDisposable
{
    private readonly string? _filePath;
    private readonly object _sync = new();
    private bool _disposed;

    /// <summary>Cria o writer.</summary>
    /// <param name="filePath">Arquivo de progresso (null desativa a escrita).</param>
    public FileProgressWriter(string? filePath)
    {
        _filePath = filePath;
    }

    /// <inheritdoc />
    public void Report(string value)
    {
        if (_disposed || string.IsNullOrEmpty(_filePath))
        {
            return;
        }

        lock (_sync)
        {
            try
            {
                File.AppendAllText(_filePath, $"[{DateTime.Now:HH:mm:ss}] {value}{Environment.NewLine}");
            }
            catch (IOException)
            {
                // O progresso é "melhor esforço": nunca deve derrubar a operação elevada.
            }
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
