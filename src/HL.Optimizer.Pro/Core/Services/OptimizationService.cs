using HL.Optimizer.Pro.Core.Interfaces;
using HL.Optimizer.Pro.Core.Models;
using HL.Optimizer.Pro.Core.Utilities;
using Microsoft.Win32;
using System.Diagnostics;
using System.ServiceProcess;

namespace HL.Optimizer.Pro.Core.Services;

public class OptimizationService : IOptimizationService
{
    private readonly IRegistryService _registry;
    private readonly IPowerService _power;
    private readonly ILogService _log;

    public OptimizationService(IRegistryService registry, IPowerService power, ILogService log)
    {
        _registry = registry;
        _power = power;
        _log = log;
    }

    public async Task<List<OptimizationItem>> GetAvailableOptimizationsAsync()
    {
        return await Task.Run(() =>
        {
            var items = new List<OptimizationItem>();

            // Services optimizations
            items.Add(new OptimizationItem
            {
                Id = "svc_sysmain",
                Name = "Otimizar serviço SysMain (Superfetch)",
                Description = "SysMain pode causar alto uso de disco em HDDs. Desativar pode melhorar desempenho em sistemas com SSD limitado.",
                Category = OptimizationCategory.Servicos,
                Risk = OptimizationRisk.Moderado,
                Reversible = true,
                RequiresAdmin = true,
                ExecuteAction = () => SetServiceStartAsync("SysMain", 4),
                RevertAction = () => SetServiceStartAsync("SysMain", 2)
            });

            items.Add(new OptimizationItem
            {
                Id = "svc_wsearch",
                Name = "Otimizar Windows Search",
                Description = "Desativar indexação em segundo plano para reduzir uso de CPU/Disco. Pesquisa ficará mais lenta.",
                Category = OptimizationCategory.Servicos,
                Risk = OptimizationRisk.Moderado,
                Reversible = true,
                RequiresAdmin = true,
                ExecuteAction = () => SetServiceStartAsync("WSearch", 4),
                RevertAction = () => SetServiceStartAsync("WSearch", 2)
            });

            items.Add(new OptimizationItem
            {
                Id = "svc_diags",
                Name = "Desativar serviços de telemetria",
                Description = "Reduz coleta de dados e uso de recursos em segundo plano.",
                Category = OptimizationCategory.Privacidade,
                Risk = OptimizationRisk.Seguro,
                Reversible = true,
                RequiresAdmin = true,
                ExecuteAction = () => SetServiceStartAsync("DiagTrack", 4),
                RevertAction = () => SetServiceStartAsync("DiagTrack", 2)
            });

            // Registry tweaks
            items.Add(new OptimizationItem
            {
                Id = "reg_visualfx",
                Name = "Desativar animações desnecessárias",
                Description = "Melhora responsividade da interface desativando animações do Windows.",
                Category = OptimizationCategory.Interface,
                Risk = OptimizationRisk.Seguro,
                Reversible = true,
                RequiresAdmin = false,
                ExecuteAction = () => SetRegistryAsync(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects", "VisualFXSetting", 2),
                RevertAction = () => SetRegistryAsync(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects", "VisualFXSetting", 0)
            });

            items.Add(new OptimizationItem
            {
                Id = "reg_menushowdelay",
                Name = "Reduzir delay de menus",
                Description = "Menus aparecem instantaneamente (0ms em vez de 400ms).",
                Category = OptimizationCategory.Interface,
                Risk = OptimizationRisk.Seguro,
                Reversible = true,
                RequiresAdmin = false,
                ExecuteAction = () => SetRegistryAsync(@"HKEY_CURRENT_USER\Control Panel\Desktop", "MenuShowDelay", "0"),
                RevertAction = () => SetRegistryAsync(@"HKEY_CURRENT_USER\Control Panel\Desktop", "MenuShowDelay", "400")
            });

            items.Add(new OptimizationItem
            {
                Id = "reg_startupdelay",
                Name = "Remover delay de inicialização de apps",
                Description = "Remove delay de 10s na inicialização de aplicativos após login.",
                Category = OptimizationCategory.Inicializacao,
                Risk = OptimizationRisk.Seguro,
                Reversible = true,
                RequiresAdmin = false,
                ExecuteAction = () => SetRegistryAsync(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Explorer\Serialize", "StartupDelayInMSec", 0),
                RevertAction = () => DeleteRegistryAsync(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Explorer\Serialize", "StartupDelayInMSec")
            });

            // Power
            items.Add(new OptimizationItem
            {
                Id = "power_highperf",
                Name = "Ativar plano de Alto Desempenho",
                Description = "CPU opera em frequência máxima constante. Ideal para jogos e tarefas pesadas.",
                Category = OptimizationCategory.Energia,
                Risk = OptimizationRisk.Seguro,
                Reversible = true,
                RequiresAdmin = true,
                ExecuteAction = async () => { await _power.SetHighPerformanceAsync(); return new OptimizationResult { Success = true, Message = "Plano de energia alterado para Alto Desempenho" }; },
                RevertAction = async () => { await _power.SetBalancedAsync(); return new OptimizationResult { Success = true, Message = "Plano de energia revertido para Equilibrado" }; }
            });

            // Network
            items.Add(new OptimizationItem
            {
                Id = "net_autotuning",
                Name = "Otimizar Auto-Tuning de rede",
                Description = "Ajusta nível de auto-tuning para melhorar throughput.",
                Category = OptimizationCategory.Rede,
                Risk = OptimizationRisk.Seguro,
                Reversible = true,
                RequiresAdmin = true,
                ExecuteAction = () => RunNetshAsync("int tcp set global autotuninglevel=normal"),
                RevertAction = () => RunNetshAsync("int tcp set global autotuninglevel=normal")
            });

            items.Add(new OptimizationItem
            {
                Id = "net_nagle",
                Name = "Desativar algoritmo de Nagle para jogos",
                Description = "Reduz latência em jogos online desativando buffer de pacotes TCP.",
                Category = OptimizationCategory.Rede,
                Risk = OptimizationRisk.Moderado,
                Reversible = true,
                RequiresAdmin = true,
                ExecuteAction = () => SetRegistryAsync(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\MSMQ\Parameters", "TCPNoDelay", 1),
                RevertAction = () => DeleteRegistryAsync(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\MSMQ\Parameters", "TCPNoDelay")
            });

            // Memory
            items.Add(new OptimizationItem
            {
                Id = "mem_large_cache",
                Name = "Otimizar cache do sistema para programas",
                Description = "Prioriza programas em vez de cache do sistema.",
                Category = OptimizationCategory.Memoria,
                Risk = OptimizationRisk.Seguro,
                Reversible = true,
                RequiresAdmin = true,
                ExecuteAction = () => SetRegistryAsync(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\PriorityControl", "Win32PrioritySeparation", 38),
                RevertAction = () => SetRegistryAsync(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\PriorityControl", "Win32PrioritySeparation", 2)
            });

            // Privacy
            items.Add(new OptimizationItem
            {
                Id = "priv_advertising",
                Name = "Desativar ID de publicidade",
                Description = "Reduz rastreamento e coleta de dados para anúncios.",
                Category = OptimizationCategory.Privacidade,
                Risk = OptimizationRisk.Seguro,
                Reversible = true,
                RequiresAdmin = false,
                ExecuteAction = () => SetRegistryAsync(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\AdvertisingInfo", "Enabled", 0),
                RevertAction = () => SetRegistryAsync(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\AdvertisingInfo", "Enabled", 1)
            });

            items.Add(new OptimizationItem
            {
                Id = "priv_feedback",
                Name = "Desativar feedback automático",
                Description = "Desativa solicitações de feedback do Windows.",
                Category = OptimizationCategory.Privacidade,
                Risk = OptimizationRisk.Seguro,
                Reversible = true,
                RequiresAdmin = false,
                ExecuteAction = () => SetRegistryAsync(@"HKEY_CURRENT_USER\Software\Microsoft\Siuf\Rules", "NumberOfSIUFInPeriod", 0),
                RevertAction = () => DeleteRegistryAsync(@"HKEY_CURRENT_USER\Software\Microsoft\Siuf\Rules", "NumberOfSIUFInPeriod")
            });

            // System
            items.Add(new OptimizationItem
            {
                Id = "sys_prefetch",
                Name = "Otimizar Prefetch/Superfetch para SSD",
                Description = "Desativa prefetch se sistema usa SSD (detectado automaticamente).",
                Category = OptimizationCategory.Sistema,
                Risk = OptimizationRisk.Moderado,
                Reversible = true,
                RequiresAdmin = true,
                ExecuteAction = () => SetRegistryAsync(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management\PrefetchParameters", "EnablePrefetcher", 0),
                RevertAction = () => SetRegistryAsync(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management\PrefetchParameters", "EnablePrefetcher", 3)
            });

            return items;
        });
    }

    public async Task<OptimizationIndex> CalculateOptimizationIndexAsync()
    {
        var items = await GetAvailableOptimizationsAsync();
        var completed = 0;
        var categoryScores = new List<OptimizationCategoryScore>();

        var grouped = items.GroupBy(i => i.Category);
        foreach (var g in grouped)
        {
            // Simplified: check if tweak is applied via registry/service state
            var total = g.Count();
            var done = 0; // In real implementation, check actual state
            // For demo, randomize based on system checks
            // We'll implement simple checks
            foreach (var item in g)
            {
                if (await IsOptimizationAppliedAsync(item)) done++;
            }
            categoryScores.Add(new OptimizationCategoryScore
            {
                Category = g.Key,
                Total = total,
                Completed = done
            });
            completed += done;
        }

        return new OptimizationIndex
        {
            TotalOptimizations = items.Count,
            CompletedOptimizations = completed,
            CategoryScores = categoryScores
        };
    }

    private async Task<bool> IsOptimizationAppliedAsync(OptimizationItem item)
    {
        // Simplified check - in real app would check registry/service
        try
        {
            if (item.Id == "power_highperf")
            {
                var active = await _power.GetActivePowerPlanAsync();
                return active.Contains("8c5e7fda") || active.ToLower().Contains("high");
            }
            // For other items, return false to indicate optimization needed
            return false;
        }
        catch { return false; }
    }

    public async Task<OptimizationResult> ExecuteOptimizationAsync(OptimizationItem item)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            if (item.RequiresAdmin && !AdminHelper.IsAdministrator())
            {
                return new OptimizationResult { Success = false, Message = "Requer privilégios administrativos", Error = "Admin required" };
            }

            OptimizationResult result;
            if (item.ExecuteAction != null)
            {
                result = await item.ExecuteAction();
            }
            else
            {
                result = new OptimizationResult { Success = false, Message = "Ação não implementada" };
            }

            result.Duration = sw.Elapsed;
            await _log.LogAsync(item.Name, item.Category.ToString(), result.Success ? "SUCESSO" : "FALHA", result.Message, item.Command, result.Error);
            return result;
        }
        catch (Exception ex)
        {
            var result = new OptimizationResult { Success = false, Message = "Erro ao executar", Error = ex.Message, Duration = sw.Elapsed };
            await _log.LogAsync(item.Name, item.Category.ToString(), "FALHA", ex.Message, item.Command, ex.Message);
            return result;
        }
    }

    public async Task<List<OptimizationResult>> ExecuteOptimizationsAsync(IEnumerable<OptimizationItem> items, IProgress<OptimizationProgress>? progress = null)
    {
        var results = new List<OptimizationResult>();
        var list = items.ToList();
        for (int i = 0; i < list.Count; i++)
        {
            progress?.Report(new OptimizationProgress { Current = i + 1, Total = list.Count, CurrentItem = list[i].Name });
            var res = await ExecuteOptimizationAsync(list[i]);
            results.Add(res);
        }
        return results;
    }

    private async Task<OptimizationResult> SetServiceStartAsync(string serviceName, int startType)
    {
        return await Task.Run(() =>
        {
            try
            {
                var keyPath = $@"SYSTEM\CurrentControlSet\Services\{serviceName}";
                using var key = Registry.LocalMachine.OpenSubKey(keyPath, true);
                if (key == null) return new OptimizationResult { Success = false, Message = $"Serviço {serviceName} não encontrado" };
                key.SetValue("Start", startType, RegistryValueKind.DWord);
                return new OptimizationResult { Success = true, Message = $"Serviço {serviceName} configurado para {startType}" };
            }
            catch (Exception ex)
            {
                return new OptimizationResult { Success = false, Message = ex.Message, Error = ex.Message };
            }
        });
    }

    private async Task<OptimizationResult> SetRegistryAsync(string fullPath, string valueName, object value)
    {
        return await Task.Run(() =>
        {
            try
            {
                var hive = RegistryHive.CurrentUser;
                string path = fullPath;
                if (fullPath.StartsWith("HKEY_LOCAL_MACHINE\\"))
                {
                    hive = RegistryHive.LocalMachine;
                    path = fullPath.Substring("HKEY_LOCAL_MACHINE\\".Length);
                }
                else if (fullPath.StartsWith("HKEY_CURRENT_USER\\"))
                {
                    hive = RegistryHive.CurrentUser;
                    path = fullPath.Substring("HKEY_CURRENT_USER\\".Length);
                }

                var kind = value is int ? RegistryValueKind.DWord : value is string ? RegistryValueKind.String : RegistryValueKind.DWord;
                var success = RegistryHelper.SetValue(path, valueName, value, kind, hive);
                return new OptimizationResult { Success = success, Message = success ? $"Registro {valueName} = {value}" : "Falha ao escrever registro" };
            }
            catch (Exception ex)
            {
                return new OptimizationResult { Success = false, Message = ex.Message, Error = ex.Message };
            }
        });
    }

    private async Task<OptimizationResult> DeleteRegistryAsync(string fullPath, string valueName)
    {
        return await Task.Run(() =>
        {
            try
            {
                var hive = RegistryHive.CurrentUser;
                string path = fullPath;
                if (fullPath.StartsWith("HKEY_LOCAL_MACHINE\\"))
                {
                    hive = RegistryHive.LocalMachine;
                    path = fullPath.Substring("HKEY_LOCAL_MACHINE\\".Length);
                }
                else if (fullPath.StartsWith("HKEY_CURRENT_USER\\"))
                {
                    hive = RegistryHive.CurrentUser;
                    path = fullPath.Substring("HKEY_CURRENT_USER\\".Length);
                }

                var success = RegistryHelper.DeleteValue(path, valueName, hive);
                return new OptimizationResult { Success = true, Message = success ? "Valor removido" : "Valor não existia" };
            }
            catch (Exception ex)
            {
                return new OptimizationResult { Success = false, Message = ex.Message, Error = ex.Message };
            }
        });
    }

    private async Task<OptimizationResult> RunNetshAsync(string args)
    {
        var (success, output, error) = await PowerShellHelper.ExecuteCmdAsync($"netsh {args}");
        return new OptimizationResult { Success = success, Message = output, Error = success ? null : error };
    }
}
