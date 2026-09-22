namespace HL.Optimizer.Pro.Core.Models;

public class GameProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "";
    public string ExecutablePath { get; set; } = "";
    public string Arguments { get; set; } = "";
    public string IconPath { get; set; } = "";
    public bool IsDetected { get; set; }
    public DateTime? LastPlayed { get; set; }
    public GameBoosterSettings BoosterSettings { get; set; } = new();
    public GameType Type { get; set; }
}

public class GameBoosterSettings
{
    public bool EnableGameMode { get; set; } = true;
    public bool HighPriority { get; set; } = true;
    public bool SuspendNonEssentialProcesses { get; set; } = true;
    public bool HighPerformancePowerPlan { get; set; } = true;
    public bool DisableWindowsUpdates { get; set; } = false;
    public bool CloseBackgroundApps { get; set; } = true;
    public List<string> ProcessesToSuspend { get; set; } = new();
}

public enum GameType
{
    Custom,
    FreeFire,
    BlueStacks,
    Valorant,
    CS2,
    Fortnite,
    Minecraft,
    Emulator
}

public class EmulatorInfo
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "";
    public EmulatorType Type { get; set; }
    public string InstallPath { get; set; } = "";
    public string ExecutablePath { get; set; } = "";
    public bool IsRunning { get; set; }
    public int ProcessId { get; set; }
    public EmulatorPerformanceProfile PerformanceProfile { get; set; } = new();
    public double CpuUsage { get; set; }
    public double RamUsageMB { get; set; }
}

public class EmulatorPerformanceProfile
{
    public string Resolution { get; set; } = "1280x720";
    public int Dpi { get; set; } = 240;
    public string Renderer { get; set; } = "OpenGL";
    public int CpuCores { get; set; } = 2;
    public int RamMB { get; set; } = 2048;
    public string PerformanceMode { get; set; } = "Desempenho";
    public bool HighFpsMode { get; set; } = false;
}

public enum EmulatorType
{
    BlueStacks,
    LDPlayer,
    MSIAppPlayer,
    GameLoop,
    Nox,
    MuMu,
    Other
}
