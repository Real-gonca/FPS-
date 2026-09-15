namespace HLProOptimizer.Core.Models;

/// <summary>
/// Contexto compartilhado entre os passos de uma otimização. Evita que cada
/// <c>IOptimizationStep</c> colete novamente o perfil do sistema e permite
/// acumular bytes liberados e logs em tempo real.
/// </summary>
public sealed class OptimizationContext
{
    private readonly List<string> _logLines = [];
    private long _totalBytesFreed;

    /// <summary>Cria o contexto com opções, configurações e perfil do sistema.</summary>
    /// <param name="options">Opções selecionadas na tela de Otimização.</param>
    /// <param name="settings">Configurações persistidas do usuário.</param>
    /// <param name="profile">Perfil de hardware/SO coletado no início da execução.</param>
    public OptimizationContext(OptimizationOptions options, AppSettings settings, SystemProfile profile)
    {
        Options = options;
        Settings = settings;
        Profile = profile;
    }

    /// <summary>Opções da execução.</summary>
    public OptimizationOptions Options { get; }

    /// <summary>Configurações persistidas do usuário.</summary>
    public AppSettings Settings { get; }

    /// <summary>Perfil do sistema coletado no início.</summary>
    public SystemProfile Profile { get; }

    /// <summary>Indica se o ponto de restauração foi criado.</summary>
    public bool RestorePointCreated { get; set; }

    /// <summary>Descrição do ponto de restauração criado.</summary>
    public string? RestorePointDescription { get; set; }

    /// <summary>Callback de log em tempo real (conectado à UI).</summary>
    public Action<string>? OnLog { get; set; }

    /// <summary>Valores arbitrários compartilhados entre passos (ex.: plano de energia anterior).</summary>
    public Dictionary<string, object?> State { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Total acumulado de bytes liberados pelos passos (leitura atômica).</summary>
    public long BytesFreed => Interlocked.Read(ref _totalBytesFreed);

    /// <summary>Linhas de log geradas durante a execução.</summary>
    public IReadOnlyList<string> LogLines
    {
        get
        {
            lock (_logLines)
            {
                return _logLines.ToArray();
            }
        }
    }

    /// <summary>Registra uma linha de log (com timestamp) e a notifica à UI.</summary>
    /// <param name="message">Mensagem a registrar.</param>
    public void Log(string message)
    {
        var line = $"[{DateTime.Now:HH:mm:ss}] {message}";

        lock (_logLines)
        {
            _logLines.Add(line);
        }

        OnLog?.Invoke(line);
    }

    /// <summary>Acumula bytes liberados por um passo (ignora valores negativos).</summary>
    /// <param name="bytes">Bytes liberados.</param>
    public void AddFreedBytes(long bytes)
    {
        if (bytes > 0)
        {
            Interlocked.Add(ref _totalBytesFreed, bytes);
        }
    }
}
