using HL.Optimizer.Pro.Core.Models;

namespace HL.Optimizer.Pro.Gaming;

public class GameBoosterEngine
{
    public static GameBoosterSettings GetRecommendedSettings(GameType type)
    {
        return type switch
        {
            GameType.Valorant => new GameBoosterSettings { HighPriority = true, HighPerformancePowerPlan = true, SuspendNonEssentialProcesses = true },
            GameType.CS2 => new GameBoosterSettings { HighPriority = true, HighPerformancePowerPlan = true, SuspendNonEssentialProcesses = true },
            GameType.FreeFire => new GameBoosterSettings { HighPriority = true, HighPerformancePowerPlan = true, CloseBackgroundApps = true },
            GameType.BlueStacks => new GameBoosterSettings { HighPriority = true, HighPerformancePowerPlan = true },
            _ => new GameBoosterSettings()
        };
    }
}
