using HL.Optimizer.Pro.Core.Models;
using Microsoft.Win32;

namespace HL.Optimizer.Pro.Optimization.Services;

public class ServiceOptimization
{
    public static List<OptimizationItem> GetServiceOptimizations()
    {
        return new List<OptimizationItem>
        {
            new() { Id="svc_wsearch", Name="Windows Search", Description="Desativa indexação", Category=OptimizationCategory.Servicos, Risk=OptimizationRisk.Moderado },
            new() { Id="svc_sysmain", Name="SysMain", Description="Otimiza Superfetch", Category=OptimizationCategory.Servicos, Risk=OptimizationRisk.Moderado },
            new() { Id="svc_diagtrack", Name="DiagTrack", Description="Telemetria", Category=OptimizationCategory.Privacidade, Risk=OptimizationRisk.Seguro }
        };
    }
}
