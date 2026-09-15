using HLProOptimizer.Core.Enums;

namespace HLProOptimizer.Core.Models;

/// <summary>Plano de energia disponível no sistema.</summary>
/// <param name="Guid">GUID do esquema (powercfg).</param>
/// <param name="Name">Nome exibido.</param>
/// <param name="Kind">Classificação do plano.</param>
/// <param name="IsActive">Se é o plano atualmente aplicado.</param>
public sealed record PowerPlanInfo(string Guid, string Name, PowerPlanKind Kind, bool IsActive)
{
    /// <summary>GUIDs canônicos dos planos padrão do Windows.</summary>
    public static class KnownGuids
    {
        /// <summary>Economia de energia.</summary>
        public const string PowerSaver = "a1841308-3541-4fab-bc81-f71556f20b4a";

        /// <summary>Equilibrado.</summary>
        public const string Balanced = "381b4222-f694-41f0-9685-ff5bb260df2e";

        /// <summary>Alto desempenho.</summary>
        public const string HighPerformance = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";

        /// <summary>Desempenho máximo (pode precisar ser duplicado).</summary>
        public const string UltimatePerformance = "e9a42b02-d5df-448d-aa66-ad3f9c11368b";
    }
}
