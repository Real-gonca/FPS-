namespace HL.Optimizer.Pro.Core.Models;

public class LogEntry
{
    public int Id { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public string Action { get; set; } = "";
    public string Category { get; set; } = "";
    public string Result { get; set; } = ""; // SUCESSO, FALHA, AVISO
    public string Details { get; set; } = "";
    public string Command { get; set; } = "";
    public string User { get; set; } = Environment.UserName;
    public string? Error { get; set; }
    public long? FreedBytes { get; set; }
}

public class RestorePointInfo
{
    public int SequenceNumber { get; set; }
    public string Description { get; set; } = "";
    public DateTime CreationTime { get; set; }
    public string Type { get; set; } = "";
}

public class DiagnosisResult
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Title { get; set; } = "";
    public string Category { get; set; } = "";
    public DiagnosisSeverity Severity { get; set; } = DiagnosisSeverity.Info;
    public string Problem { get; set; } = "";
    public string ProbableCause { get; set; } = "";
    public string Impact { get; set; } = "";
    public string Recommendation { get; set; } = "";
    public bool HasFix { get; set; }
    public Func<Task<OptimizationResult>>? FixAction { get; set; }
    public bool IsFixed { get; set; }
}

public enum DiagnosisSeverity
{
    Info,
    Warning,
    Error,
    Critical
}

public class NetworkInfo
{
    public string IpAddress { get; set; } = "";
    public string Gateway { get; set; } = "";
    public string DnsPrimary { get; set; } = "";
    public string DnsSecondary { get; set; } = "";
    public string AdapterName { get; set; } = "";
    public string Speed { get; set; } = "";
    public double LatencyMs { get; set; }
    public double PacketLoss { get; set; }
    public bool IsConnected { get; set; }
}
