using System.Text.Json;
using HLOptimizerPro.Core.Helpers;
using HLOptimizerPro.Core.Models;
using Microsoft.Extensions.Logging;

namespace HLOptimizerPro.Core.Services;

/// <summary>
/// Diário de alterações: registo persistente de TODAS as otimizações aplicadas, com os valores
/// originais necessários para as reverter.
///
/// Vive em <c>%AppData%\HLOptimizerPro\journal.json</c> e sobrevive ao fecho da aplicação — é o
/// que permite ao utilizador abrir o programa uma semana depois e clicar em "Reverter tudo".
///
/// Regra: nunca gravamos dados sensíveis (caminhos de utilizador são aceitáveis; palavras-passe,
/// tokens e chaves de licença nunca).
/// </summary>
public sealed class OptimizationJournal
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    private readonly ILogger<OptimizationJournal> _logger;
    private readonly IClockHolder _clock;
    private readonly List<JournalEntry> _entries = new();
    private readonly object _gate = new();

    /// <summary>Interface mínima para injetar o relógio sem acoplar ao namespace de abstrações.</summary>
    public interface IClockHolder
    {
        DateTime Now { get; }
    }

    public OptimizationJournal(ILogger<OptimizationJournal> logger, Abstractions.IClock clock)
    {
        _logger = logger;
        _clock = new ClockAdapter(clock);
        Load();
    }

    /// <summary>Entradas ativas (aplicadas e não revertidas), mais recentes primeiro.</summary>
    public IReadOnlyList<JournalEntry> ActiveEntries
    {
        get
        {
            lock (_gate) return _entries.Where(e => e.IsActive).OrderByDescending(e => e.AppliedAt).ToList();
        }
    }

    /// <summary>Histórico completo.</summary>
    public IReadOnlyList<JournalEntry> AllEntries
    {
        get
        {
            lock (_gate) return _entries.OrderByDescending(e => e.AppliedAt).ToList();
        }
    }

    public int ActiveCount
    {
        get
        {
            lock (_gate) return _entries.Count(e => e.IsActive);
        }
    }

    /// <summary>Regista uma otimização aplicada (ou simulada — as simuladas não ficam no diário).</summary>
    public JournalEntry Record(OptimizationAction action, bool simulated = false)
    {
        var entry = new JournalEntry
        {
            ActionKey = action.Key,
            Title = action.Title,
            Category = action.Category.ToString(),
            AppliedAt = _clock.Now,
            RequiresRestart = action.RequiresRestart,
            Backup = new Dictionary<string, string?>(action.Backup, StringComparer.OrdinalIgnoreCase),
        };

        if (simulated)
        {
            // No modo simulado nada é alterado: não sujamos o diário.
            return entry;
        }

        lock (_gate)
        {
            // Uma otimização aplicada duas vezes substitui o registo anterior (o backup antigo
            // já não serve para reverter, porque o valor original foi sobreposto).
            var existing = _entries.FirstOrDefault(e => e.ActionKey == action.Key && e.IsActive);
            if (existing is not null)
            {
                existing.RevertedAt = _clock.Now;
                _logger.LogDebug("Entrada anterior de {Key} marcada como substituída.", action.Key);
            }

            _entries.Add(entry);
            Save();
        }

        return entry;
    }

    /// <summary>Marca uma entrada como revertida (a reversão real é feita pelo motor).</summary>
    public void MarkReverted(string actionKey)
    {
        lock (_gate)
        {
            foreach (var entry in _entries.Where(e => e.ActionKey == actionKey && e.IsActive))
            {
                entry.RevertedAt = _clock.Now;
            }

            Save();
        }
    }

    public JournalEntry? FindActive(string actionKey)
    {
        lock (_gate) return _entries.FirstOrDefault(e => e.ActionKey == actionKey && e.IsActive);
    }

    /// <summary>Apaga o diário (só depois de o utilizador reverter ou aceitar perder a reversão).</summary>
    public Result Clear()
    {
        try
        {
            lock (_gate)
            {
                _entries.Clear();
                Save();
            }

            return Result.Ok("Diário de alterações limpo.");
        }
        catch (Exception ex)
        {
            return Result.Fail("Não foi possível limpar o diário.", ex.Message);
        }
    }

    /// <summary>Exporta o diário para CSV (auditoria pelo utilizador).</summary>
    public Result Export(string destinationFile)
    {
        try
        {
            var lines = new List<string> { "AplicadoEm;RevertidoEm;Chave;Titulo;Categoria;RequerReinicio" };
            lines.AddRange(AllEntries.Select(e =>
                $"{e.AppliedAt:yyyy-MM-dd HH:mm:ss};{(e.RevertedAt?.ToString("yyyy-MM-dd HH:mm:ss") ?? string.Empty)};" +
                $"{e.ActionKey};{e.Title.Replace(';', ',')};{e.Category};{(e.RequiresRestart ? "Sim" : "Não")}"));

            File.WriteAllLines(destinationFile, lines, System.Text.Encoding.UTF8);
            return Result.Ok($"Diário exportado para {destinationFile}");
        }
        catch (Exception ex)
        {
            return Result.Fail("Não foi possível exportar o diário.", ex.Message);
        }
    }

    private void Save()
    {
        try
        {
            AppPaths.EnsureCreated();
            var json = JsonSerializer.Serialize(_entries, JsonOptions);
            var temp = AppPaths.JournalFile + ".tmp";
            File.WriteAllText(temp, json);
            File.Move(temp, AppPaths.JournalFile, overwrite: true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha ao guardar o diário de alterações.");
        }
    }

    private void Load()
    {
        try
        {
            AppPaths.EnsureCreated();
            if (!File.Exists(AppPaths.JournalFile)) return;

            var json = File.ReadAllText(AppPaths.JournalFile);
            var loaded = JsonSerializer.Deserialize<List<JournalEntry>>(json, JsonOptions);
            if (loaded is null) return;

            lock (_gate)
            {
                _entries.Clear();
                // Mantém 1 ano de histórico (evita ficheiro infinito).
                _entries.AddRange(loaded.Where(e => e.AppliedAt > _clock.Now.AddYears(-1)));
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Diário de alterações ilegível — será recriado.");
        }
    }

    /// <summary>Adapta <see cref="Abstractions.IClock"/> à interface local.</summary>
    private sealed class ClockAdapter : IClockHolder
    {
        private readonly Abstractions.IClock _inner;
        public ClockAdapter(Abstractions.IClock inner) => _inner = inner;
        public DateTime Now => _inner.Now;
    }
}
