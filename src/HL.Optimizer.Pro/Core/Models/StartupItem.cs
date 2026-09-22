namespace HL.Optimizer.Pro.Core.Models;

public class StartupItem
{
    public string Name { get; set; } = "";
    public string Publisher { get; set; } = "";
    public string Path { get; set; } = "";
    public string Command { get; set; } = "";
    public StartupLocation Location { get; set; }
    public bool IsEnabled { get; set; }
    public StartupImpact Impact { get; set; } = StartupImpact.Baixo;
    public string RegistryKey { get; set; } = "";
}

public enum StartupLocation
{
    RegistryCurrentUser,
    RegistryLocalMachine,
    StartupFolder,
    TaskScheduler,
    Service
}

public enum StartupImpact
{
    Baixo,
    Medio,
    Alto,
    Desconhecido
}

public class ServiceInfo
{
    public string Name { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Description { get; set; } = "";
    public string Status { get; set; } = "";
    public string StartType { get; set; } = "";
    public string Path { get; set; } = "";
}
