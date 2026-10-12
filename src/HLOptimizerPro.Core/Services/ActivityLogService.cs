using System.Collections.ObjectModel;
using System.Text.Json;
using HLOptimizerPro.Core.Helpers;
using HLOptimizerPro.Core.Models;
using Microsoft.Extensions.Logging;

namespace HLOptimizerPro.Core.Services;

/// <summary>
/// Histórico de atividades apresentado no ecrã Configuração e no atalho "Logs do Sistema".
/// Guardado em JSONL (uma linha JSON por evento) para permitir escrita em append sem reescrever
/// o ficheiro todo — importante porque a aplicação pode correr horas seguidas.
/// </summary>
public sealed class ActivityLogService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    private readonly ILogger<ActivityLogService> _logger;
    private readonly ObservableCollection<ActivityEntry> _entries = new();
    private readonly object _gate = new();
    private const int MaxEntriesInMemory = 500;

    public ActivityLogService(ILogger<ActivityLogService> logger)
    {
        _logger = logger;
        Load();
    }

    /// <summary>Lista observável ligada diretamente à UI (thread-safe via dispatcher no ViewModel).</summary>
    public ReadOnlyObservableCollection<ActivityEntry> Entries { get; private set; } = null!;

    /// <summary>Evento disparado quando uma entrada nova é registada (a UI faz scroll/atualização).</summary>
    public event EventHandler<ActivityEntry>? EntryAdded;

    /// <summary>
    /// Objeto de sincronização do histórico.
    ///
    /// É público porque o histórico é escrito por serviços que correm em threads de fundo
    /// (limpeza automática, watchdog do Modo Jogo). A camada de apresentação chama
    /// <c>BindingOperations.EnableCollectionSynchronization(Entries, SyncRoot)</c> no arranque para
    /// que a lista possa ser atualizada de qualquer thread sem provocar exceções de acesso cruzado.
    /// </summary>
    public object SyncRoot => _gate;

    public void Log(string category, string description, bool success = true, string details = "", bool simulated = false)
    {
        var entry = new ActivityEntry
        {
            Timestamp = DateTime.Now,
            Category = category,
            Description = description,
            Details = details,
            Success = success,
            Simulated = simulated,
        };

        Append(entry);
        _logger.LogInformation("[{Category}] {Description} (sucesso={Success}, simulado={Simulated}) {Details}",
            category, description, success, simulated, details);
    }

    public void LogResult(string category, string description, Result result, bool simulated = false)
        => Log(category, description, result.Success, result.Message, simulated);

    private void Append(ActivityEntry entry)
    {
        lock (_gate)
        {
            _entries.Insert(0, entry);
            Trim();
            Persist(entry);
        }

        EntryAdded?.Invoke(this, entry);
    }

    /// <summary>Devolve as últimas entradas (mais recentes primeiro).</summary>
    public IReadOnlyList<ActivityEntry> Recent(int count = 50)
    {
        lock (_gate)
        {
            return _entries.Take(count).ToList();
        }
    }

    /// <summary>Apaga o histórico em memória e em disco (ação explícita do utilizador na Configuração).</summary>
    public Result Clear()
    {
        try
        {
            lock (_gate)
            {
                _entries.Clear();
                if (File.Exists(AppPaths.ActivityFile)) File.Delete(AppPaths.ActivityFile);
            }

            Log("Configuração", "Histórico de atividades apagado");
            return Result.Ok("Histórico de atividades apagado.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha ao apagar o histórico de atividades.");
            return Result.Fail("Não foi possível apagar o histórico.", ex.Message);
        }
    }

    /// <summary>Exporta o histórico para CSV (usado no botão "Exportar" da Configuração).</summary>
    public Result Export(string destinationFile)
    {
        try
        {
            lock (_gate)
            {
                var lines = new List<string> { "DataHora;Categoria;Descricao;Detalhes;Estado" };
                lines.AddRange(_entries.Select(e =>
                    $"{e.Timestamp:yyyy-MM-dd HH:mm:ss};{Escape(e.Category)};{Escape(e.Description)};{Escape(e.Details)};{e.StatusText}"));

                File.WriteAllLines(destinationFile, lines, System.Text.Encoding.UTF8);
            }

            return Result.Ok($"Histórico exportado para {destinationFile}");
        }
        catch (Exception ex)
        {
            return Result.Fail("Não foi possível exportar o histórico.", ex.Message);
        }
    }

    private void Trim()
    {
        while (_entries.Count > MaxEntriesInMemory)
            _entries.RemoveAt(_entries.Count - 1);
    }

    private void Persist(ActivityEntry entry)
    {
        try
        {
            AppPaths.EnsureCreated();
            var line = JsonSerializer.Serialize(entry, JsonOptions);
            File.AppendAllText(AppPaths.ActivityFile, line + Environment.NewLine);
        }
        catch (Exception ex)
        {
            // Falha de escrita não deve quebrar a operação em curso.
            _logger.LogDebug(ex, "Não foi possível persistir a entrada de atividade.");
        }
    }

    private void Load()
    {
        try
        {
            AppPaths.EnsureCreated();
            Entries = new ReadOnlyObservableCollection<ActivityEntry>(_entries);

            if (!File.Exists(AppPaths.ActivityFile)) return;

            // Lê apenas a cauda do ficheiro (o histórico pode ter meses).
            var lines = File.ReadLines(AppPaths.ActivityFile).TakeLast(MaxEntriesInMemory).ToList();
            foreach (var line in lines.AsEnumerable().Reverse())
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                try
                {
                    var entry = JsonSerializer.Deserialize<ActivityEntry>(line, JsonOptions);
                    if (entry is not null) _entries.Add(entry);
                }
                catch
                {
                    // Linha corrompida (escrita interrompida) — ignorar sem perder o resto.
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Não foi possível carregar o histórico de atividades.");
            Entries ??= new ReadOnlyObservableCollection<ActivityEntry>(_entries);
        }

        Entries = new ReadOnlyObservableCollection<ActivityEntry>(_entries);
    }

    private static string Escape(string value) => value.Replace(';', ',').Replace('\n', ' ').Replace('\r', ' ');
}
