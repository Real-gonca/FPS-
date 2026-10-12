using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using HLOptimizerPro.Core.Helpers;
using HLOptimizerPro.Core.Models;
using HLOptimizerPro.Core.Services.Abstractions;
using Microsoft.Extensions.Logging;

namespace HLOptimizerPro.Core.Services;

/// <summary>
/// Módulo de otimização de memória RAM (3.4a).
///
/// As quatro ações do briefing, com a explicação técnica honesta do que fazem:
///
///  1. <b>Reduzir Uso de Memória</b> — <c>EmptyWorkingSet</c> nos processos seguros.
///     As páginas que não estão a ser usadas passam para a lista de reserva (standby), de onde
///     voltam instantaneamente se forem necessárias. Não "liberta" memória em uso ativo.
///
///  2. <b>Limpar Cache</b> — liberta a cache de ficheiros do sistema (SystemFileCacheInformation).
///     Útil quando um programa apagou/criou muitos ficheiros.
///
///  3. <b>Descarregar Páginas Modificadas</b> — força a escrita em disco das páginas sujas,
///     reduzindo o risco de bloqueio em picos de atividade de disco.
///
///  4. <b>Limpeza de Armazenamento em Espera</b> — esvazia a lista de reserva (standby list) via
///     <c>NtSetSystemInformation</c>. É a operação mais agressiva; o Windows reconstrói a lista,
///     mas as primeiras leituras de ficheiros podem ficar mais lentas.
///
/// SEGURANÇA: nunca tocamos em processos do sistema (<see cref="ProcessProtectionService"/>) e
/// nenhuma operação encerra processos — só ajustamos working sets.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class MemoryOptimizationService
{
    private readonly ILogger<MemoryOptimizationService> _logger;
    private readonly MonitoringService _monitoring;
    private readonly ActivityLogService _activity;

    // Valores documentados de SYSTEM_MEMORY_LIST_COMMAND (ntifs/ntddk).
    private const int SystemMemoryListInformation = 0x50;
    private const int MemoryEmptyWorkingSets = 2;
    private const int MemoryFlushModifiedList = 3;
    private const int MemoryPurgeStandbyList = 4;
    private const int MemoryPurgeLowPriorityStandbyList = 5;

    // SystemFileCacheInformation (SYSTEM_FILECACHE_INFORMATION = 0x15, classe = 0x15 | 0x80)
    private const int SystemFileCacheInformation = 0x15;

    public MemoryOptimizationService(
        ILogger<MemoryOptimizationService> logger,
        MonitoringService monitoring,
        ActivityLogService activity)
    {
        _logger = logger;
        _monitoring = monitoring;
        _activity = activity;
    }

    /// <summary>Memória total/em uso/cache/standby (a lista de reserva exige leitura do kernel).</summary>
    public MemorySnapshot GetSnapshot() => _monitoring.ReadMemory();

    /// <summary>Ações disponíveis, com descrição, risco e implementação — usado pela UI e pelo relatório.</summary>
    public IReadOnlyList<OptimizationAction> GetActions()
    {
        var memory = GetSnapshot();

        return new List<OptimizationAction>
        {
            new()
            {
                Key = "mem.reduce",
                Title = "Reduzir Uso de Memória",
                Description = "Move para a lista de reserva as páginas de memória que os processos não estão a usar. " +
                              "Liberta memória RAM de imediato sem encerrar nenhum programa.",
                Target = $"Working set de {CountCandidateProcesses()} processos seguros (nenhum processo do sistema é tocado)",
                CurrentValue = $"{Formatters.Bytes(memory.UsedBytes)} em uso",
                NewValue = $"Libertar o que estiver inativo (estimativa: {Formatters.Bytes(EstimateReclaimable(memory))})",
                Category = OptimizationCategory.Sistema,
                Area = OptimizationArea.DesempenhoSistema,
                Risk = "Baixo — não encerra processos; a memória volta a ser usada quando precisar.",
                RiskLevel = HealthLevel.Otimo,
            },
            new()
            {
                Key = "mem.cache",
                Title = "Limpar Cache do Sistema",
                Description = "Esvazia a cache de ficheiros do sistema (ficheiros lidos recentemente que já não são necessários).",
                Target = "SystemFileCacheInformation (kernel)",
                CurrentValue = Formatters.Bytes(memory.CachedBytes) + " em cache",
                NewValue = "Cache vazia; será reconstruída conforme necessário",
                Category = OptimizationCategory.Sistema,
                Area = OptimizationArea.DesempenhoSistema,
                Risk = "Baixo — apenas afeta desempenho momentâneo de leitura de ficheiros.",
                RiskLevel = HealthLevel.Otimo,
            },
            new()
            {
                Key = "mem.modified",
                Title = "Descarregar Páginas Modificadas",
                Description = "Força a escrita em disco das páginas alteradas em memória, aliviando picos de atividade de disco.",
                Target = "Lista de páginas modificadas (MM)",
                CurrentValue = Formatters.Bytes(memory.ModifiedBytes) + " por escrever",
                NewValue = "Páginas modificadas escritas em disco",
                Category = OptimizationCategory.Sistema,
                Area = OptimizationArea.DesempenhoSistema,
                Risk = "Baixo — o Windows já faz isto automaticamente em segundo plano.",
                RiskLevel = HealthLevel.Otimo,
            },
            new()
            {
                Key = "mem.standby",
                Title = "Limpeza de Armazenamento em Espera",
                Description = "Esvazia a lista de reserva (standby), onde o Windows guarda dados de ficheiros lidos recentemente. " +
                              "Liberta RAM de imediato, mas a próxima leitura desses ficheiros será mais lenta.",
                Target = "Lista de armazenamento em espera (standby list)",
                CurrentValue = memory.StandbyBytes > 0 ? Formatters.Bytes(memory.StandbyBytes) + " em espera" : "Sem dados (requer administrador)",
                NewValue = "Lista de espera esvaziada",
                Category = OptimizationCategory.Sistema,
                Area = OptimizationArea.DesempenhoSistema,
                Risk = "Médio — recomendado apenas quando a RAM está acima de 85 % de uso.",
                RiskLevel = HealthLevel.Atencao,
                RequiresConfirmation = true,
            },
        };
    }

    /// <summary>Executa uma das ações de memória pelo seu nome ("Reduzir Uso de Memória", etc.).</summary>
    public Result Execute(string actionKey)
    {
        var before = GetSnapshot();

        try
        {
            var result = actionKey switch
            {
                "mem.reduce" => EmptyWorkingSets(),
                "mem.cache" => ClearFileSystemCache(),
                "mem.modified" => FlushModifiedPages(),
                "mem.standby" => PurgeStandbyList(),
                _ => Result.Fail($"Ação de memória desconhecida: {actionKey}"),
            };

            StartBackgroundRefresh();

            var after = GetSnapshot();
            var freed = Math.Max(0, before.UsedBytes - after.UsedBytes);

            var message = result.Success && freed > 0
                ? $"{result.Message} Memória em uso: {Formatters.Bytes(before.UsedBytes)} → {Formatters.Bytes(after.UsedBytes)} ({Formatters.Bytes(freed)} libertados)."
                : result.Message;

            _activity.Log("Memória", message, result.Success);
            return result.Success ? Result.Ok(message) : Result.Fail(message, result.TechnicalDetails);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha ao executar {Action}", actionKey);
            _activity.Log("Memória", $"Falha em «{actionKey}»: {ex.Message}", success: false);
            return Result.Fail($"Não foi possível concluir a operação de memória: {ex.Message}", ex.ToString());
        }
    }

    /// <summary>
    /// Encerra (opcionalmente) processos selecionados pelo utilizador.
    /// Processos protegidos são recusados com explicação — requisito de segurança.
    /// </summary>
    public Result TerminateProcess(ProcessUsageInfo process, bool confirmed)
    {
        if (ProcessProtectionService.IsProtected(process.Name) || ProcessProtectionService.IsSystemCritical(process.Name))
        {
            return Result.Fail($"«{process.Name}» é um processo protegido do sistema e não será encerrado.");
        }

        if (ProcessProtectionService.IsInsideWindowsDirectory(process.ExecutablePath) && !confirmed)
            return Result.Fail($"«{process.Name}» está na pasta do Windows — confirme explicitamente para o encerrar.");

        try
        {
            using var target = Process.GetProcessById(process.Pid);
            target.CloseMainWindow();

            // Dá 2 s para fechar de forma graciosa (permite guardar trabalho do utilizador).
            if (!target.WaitForExit(2000))
                target.Kill();

            _activity.Log("Processos", $"Processo «{process.Name}» (PID {process.Pid}) encerrado");
            return Result.Ok($"«{process.Name}» encerrado. {Formatters.Bytes(process.WorkingSetBytes)} libertados.");
        }
        catch (ArgumentException)
        {
            return Result.Fail($"O processo «{process.Name}» já não está em execução.");
        }
        catch (Exception ex)
        {
            return Result.Fail($"Não foi possível encerrar «{process.Name}» (pode estar a ser usado).", ex.Message);
        }
    }

    // ------------------------------------------------------------------ implementações

    /// <summary>EmptyWorkingSet em todos os processos seguros do utilizador atual.</summary>
    private Result EmptyWorkingSets()
    {
        var count = 0;
        var skipped = 0;
        var failures = 0;

        foreach (var process in Process.GetProcesses())
        {
            try
            {
                // Nunca tocamos em processos de sistema nem em processos de outros utilizadores.
                if (ProcessProtectionService.IsProtected(process.ProcessName))
                {
                    skipped++;
                    continue;
                }

                if (process.Id <= 4 || process.SessionId != Process.GetCurrentProcess().SessionId)
                {
                    skipped++;
                    continue;
                }

                if (EmptyWorkingSet(process.Handle)) count++;
                else failures++;
            }
            catch
            {
                // Processo terminou entretanto ou acesso negado — esperado e inofensivo.
            }
            finally
            {
                process.Dispose();
            }
        }

        return count > 0
            ? Result.Ok($"{count} processos otimizados ({skipped} protegidos ignorados).")
            : Result.Fail($"Não foi possível otimizar nenhum processo ({failures} recusados pelo sistema). " +
                          "Execute a aplicação como administrador para melhores resultados.");
    }

    /// <summary>Limpa a cache de ficheiros do sistema.</summary>
    private Result ClearFileSystemCache()
    {
        if (!IsElevated())
            return Result.Fail("Limpar a cache do sistema requer privilégios de administrador.");

        // Estrutura SYSTEM_FILECACHE_INFORMATION com MinimumWorkingSet/MaximumWorkingSet = -1
        // faz com que o kernel reduza a cache ao mínimo.
        var info = new FileCacheInformation
        {
            CurrentSize = 0,
            PeakSize = 0,
            PageFaultCount = 0,
            MinimumWorkingSet = ulong.MaxValue,
            MaximumWorkingSet = ulong.MaxValue,
            CurrentSizeIncludingTransitionInPages = 0,
            PeakSizeIncludingTransitionInPages = 0,
            TransitionRePurposeCount = 0,
            Flags = 0,
        };

        var size = Marshal.SizeOf<FileCacheInformation>();
        var pointer = Marshal.AllocHGlobal(size);

        try
        {
            Marshal.StructureToPtr(info, pointer, false);
            var status = NtSetSystemInformation(SystemFileCacheInformation, pointer, size);

            return status == 0
                ? Result.Ok("Cache de ficheiros do sistema limpa.")
                : Result.Fail($"O kernel recusou a operação (NTSTATUS 0x{status:X8}).");
        }
        catch (Exception ex)
        {
            return Result.Fail("Não foi possível limpar a cache do sistema.", ex.Message);
        }
        finally
        {
            Marshal.FreeHGlobal(pointer);
        }
    }

    private Result FlushModifiedPages()
    {
        if (!IsElevated())
            return Result.Fail("Esta operação requer privilégios de administrador (SeProfileSingleProcessPrivilege).");

        var command = MemoryFlushModifiedList;
        var status = NtSetSystemInformation(SystemMemoryListInformation, ref command, sizeof(int));

        return status == 0
            ? Result.Ok("Páginas modificadas escritas em disco.")
            : Result.Fail($"Operação recusada pelo kernel (NTSTATUS 0x{status:X8}).");
    }

    private Result PurgeStandbyList()
    {
        if (!IsElevated())
            return Result.Fail("A limpeza da lista de espera requer privilégios de administrador.");

        var command = MemoryPurgeStandbyList;
        var status = NtSetSystemInformation(SystemMemoryListInformation, ref command, sizeof(int));

        if (status == 0) return Result.Ok("Lista de armazenamento em espera esvaziada.");

        // Em algumas versões do Windows apenas a lista de baixa prioridade é acessível.
        var lowPriority = MemoryPurgeLowPriorityStandbyList;
        var fallback = NtSetSystemInformation(SystemMemoryListInformation, ref lowPriority, sizeof(int));

        return fallback == 0
            ? Result.Ok("Lista de espera de baixa prioridade esvaziada.")
            : Result.Fail($"Operação recusada pelo kernel (NTSTATUS 0x{status:X8}).");
    }

    /// <summary>Dispara uma releitura em background para atualizar os gauges sem bloquear a UI.</summary>
    private void StartBackgroundRefresh() => Task.Run(() => _monitoring.Read());

    private int CountCandidateProcesses()
    {
        var count = 0;
        foreach (var process in Process.GetProcesses())
        {
            try
            {
                if (!ProcessProtectionService.IsProtected(process.ProcessName) && process.SessionId == Process.GetCurrentProcess().SessionId)
                    count++;
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

        return count;
    }

    /// <summary>Estimativa conservadora do que pode ser recuperado: metade das páginas em cache/standby.</summary>
    private static long EstimateReclaimable(MemorySnapshot memory)
        => (memory.CachedBytes + memory.StandbyBytes) / 2;

    private static bool IsElevated()
    {
        try
        {
            using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
            return new System.Security.Principal.WindowsPrincipal(identity)
                .IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    // ------------------------------------------------------------------ P/Invoke

    [DllImport("psapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EmptyWorkingSet(IntPtr process);

    [DllImport("ntdll.dll")]
    private static extern int NtSetSystemInformation(int systemInformationClass, IntPtr systemInformation, int systemInformationLength);

    [DllImport("ntdll.dll")]
    private static extern int NtSetSystemInformation(int systemInformationClass, ref int systemInformation, int systemInformationLength);

    [StructLayout(LayoutKind.Sequential)]
    private struct FileCacheInformation
    {
        public ulong CurrentSize;
        public ulong PeakSize;
        public uint PageFaultCount;
        public ulong MinimumWorkingSet;
        public ulong MaximumWorkingSet;
        public ulong CurrentSizeIncludingTransitionInPages;
        public ulong PeakSizeIncludingTransitionInPages;
        public uint TransitionRePurposeCount;
        public uint Flags;
    }
}
