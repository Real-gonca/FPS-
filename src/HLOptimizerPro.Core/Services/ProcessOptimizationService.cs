using System.Diagnostics;
using System.Runtime.Versioning;
using HLOptimizerPro.Core.Helpers;
using HLOptimizerPro.Core.Models;
using Microsoft.Extensions.Logging;

namespace HLOptimizerPro.Core.Services;

/// <summary>
/// Altera prioridade e afinidade de processos — a base técnica dos módulos App Tuner e Game Mode.
///
/// Garantias de segurança:
///   • Processos protegidos são recusados (ver <see cref="ProcessProtectionService"/>).
///   • A afinidade nunca desce abaixo de 2 núcleos (menos que isso torna o sistema inutilizável).
///   • A prioridade nunca sobe acima de "High" (Realtime bloqueia todo o sistema).
///   • O estado original é devolvido ao chamador para ser guardado no diário (reversão 100 %).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class ProcessOptimizationService
{
    private readonly ILogger<ProcessOptimizationService> _logger;

    public ProcessOptimizationService(ILogger<ProcessOptimizationService> logger) => _logger = logger;

    /// <summary>Prioridades permitidas, da mais alta para a mais baixa (Realtime está EXCLUÍDA por segurança).</summary>
    public static IReadOnlyList<string> AllowedPriorities { get; } = new[]
    {
        "High", "AboveNormal", "Normal", "BelowNormal", "Idle",
    };

    /// <summary>Deteta o PID de uma app pelos nomes de processo possíveis (0 = não está em execução).</summary>
    public int FindProcessId(IEnumerable<string> processNames)
    {
        var names = processNames.Select(n => n.Replace(".exe", string.Empty, StringComparison.OrdinalIgnoreCase))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var process in Process.GetProcesses())
        {
            try
            {
                if (names.Contains(process.ProcessName)) return process.Id;
            }
            catch
            {
                // acesso negado — ignorar
            }
            finally
            {
                process.Dispose();
            }
        }

        return 0;
    }

    /// <summary>Lista de processos em execução cujo nome consta da lista indicada.</summary>
    public IReadOnlyList<(int Pid, string Name)> FindAllProcesses(IEnumerable<string> processNames)
    {
        var names = processNames.Select(n => n.Replace(".exe", string.Empty, StringComparison.OrdinalIgnoreCase))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var found = new List<(int, string)>();

        foreach (var process in Process.GetProcesses())
        {
            try
            {
                if (names.Contains(process.ProcessName)) found.Add((process.Id, process.ProcessName));
            }
            catch
            {
                // ignorar
            }
            finally
            {
                process.Dispose();
            }
        }

        return found;
    }

    /// <summary>
    /// Aplica prioridade e afinidade a um PID.
    /// Devolve as descrições do estado ORIGINAL (para o diário) — vazias em caso de falha total.
    /// </summary>
    public ProcessAdjustmentResult ApplyToProcess(int pid, string priorityName, int preferredCores)
    {
        var result = new ProcessAdjustmentResult();

        if (pid <= 4)
        {
            result.Error = "PID inválido (nunca alteramos processos do sistema).";
            return result;
        }

        try
        {
            using var process = Process.GetProcessById(pid);

            if (ProcessProtectionService.IsProtected(process.ProcessName))
            {
                result.Error = $"«{process.ProcessName}» é um processo protegido.";
                return result;
            }

            result.ProcessName = process.ProcessName;
            result.OriginalPriority = process.PriorityClass.ToString();

            // ---- Prioridade ----------------------------------------------------
            if (!string.IsNullOrWhiteSpace(priorityName) && AllowedPriorities.Contains(priorityName, StringComparer.OrdinalIgnoreCase))
            {
                var target = Enum.Parse<ProcessPriorityClass>(priorityName, ignoreCase: true);

                // Realtime é explicitamente recusado: pode bloquear entrada de teclado/rato.
                if (target == ProcessPriorityClass.RealTime)
                {
                    result.Error = "A prioridade «Realtime» não é permitida (pode tornar o sistema inutilizável).";
                    return result;
                }

                process.PriorityClass = target;
                result.PriorityApplied = true;
            }

            // ---- Afinidade -----------------------------------------------------
            if (preferredCores > 0)
            {
                var totalCores = Environment.ProcessorCount;
                var coresToUse = Math.Max(AppConstants.MinimumCpuCoresForAffinity, Math.Min(preferredCores, totalCores));

                if (coresToUse < totalCores)
                {
                    result.OriginalAffinity = process.ProcessorAffinity.ToInt64();

                    // Mantém os primeiros N núcleos (os jogos beneficiam de núcleos físicos consistentes).
                    var mask = (1L << coresToUse) - 1;
                    process.ProcessorAffinity = new IntPtr(mask);
                    result.AffinityApplied = true;
                }
            }

            _logger.LogInformation("Processo {Name} (PID {Pid}): prioridade {Priority}, afinidade {Cores} núcleos",
                process.ProcessName, pid, priorityName, preferredCores);

            return result;
        }
        catch (InvalidOperationException)
        {
            result.Error = "O processo terminou antes de podermos otimizá-lo.";
            return result;
        }
        catch (ArgumentException)
        {
            result.Error = "O processo já não está em execução.";
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Falha ao ajustar o processo {Pid}.", pid);
            result.Error = ex.Message;
            return result;
        }
    }

    /// <summary>Reverte prioridade e afinidade de um processo (usado ao fechar o jogo).</summary>
    public Result RevertProcess(int pid, string? originalPriority, long? originalAffinity)
    {
        try
        {
            using var process = Process.GetProcessById(pid);

            if (!string.IsNullOrWhiteSpace(originalPriority)
                && Enum.TryParse<ProcessPriorityClass>(originalPriority, ignoreCase: true, out var priority)
                && priority != ProcessPriorityClass.RealTime)
            {
                process.PriorityClass = priority;
            }

            if (originalAffinity is { } mask && mask > 0)
                process.ProcessorAffinity = new IntPtr(mask);

            return Result.Ok($"Processo {process.ProcessName} (PID {pid}) restaurado.");
        }
        catch (Exception ex)
        {
            // O processo pode já ter terminado — isso não é um erro.
            return Result.Ok($"Processo {pid} já não está em execução (nada a restaurar). [{ex.GetType().Name}]");
        }
    }

    /// <summary>Reduz a prioridade de processos de fundo conhecidos (usado pelo Game Mode).</summary>
    public int ThrottleBackgroundProcesses(IEnumerable<string> backgroundNames)
    {
        var throttled = 0;

        foreach (var (pid, name) in FindAllProcesses(backgroundNames))
        {
            try
            {
                using var process = Process.GetProcessById(pid);
                if (ProcessProtectionService.IsProtected(process.ProcessName)) continue;

                if (process.PriorityClass > ProcessPriorityClass.BelowNormal)
                {
                    process.PriorityClass = ProcessPriorityClass.BelowNormal;
                    throttled++;
                }
            }
            catch
            {
                // Ignorar processos que negam acesso.
            }
        }

        if (throttled > 0) _logger.LogInformation("{Count} processos de fundo reduzidos para BelowNormal.", throttled);
        return throttled;
    }

    /// <summary>Aplica a prioridade "High" a todos os processos de um jogo (jogos com vários processos).</summary>
    public int PrioritizeAll(IEnumerable<string> processNames, string priorityName)
    {
        var count = 0;

        foreach (var (pid, _) in FindAllProcesses(processNames))
        {
            var result = ApplyToProcess(pid, priorityName, 0);
            if (result.PriorityApplied) count++;
        }

        return count;
    }
}

/// <summary>Resultado de um ajuste de processo, com o estado original para o diário.</summary>
public sealed class ProcessAdjustmentResult
{
    public string ProcessName { get; set; } = string.Empty;
    public string? OriginalPriority { get; set; }
    public long? OriginalAffinity { get; set; }
    public bool PriorityApplied { get; set; }
    public bool AffinityApplied { get; set; }
    public string? Error { get; set; }

    public bool Success => PriorityApplied || AffinityApplied;
}
