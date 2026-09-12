namespace Nexus.Infrastructure.Persistence;

/// <summary>Linhas de persistência (SQLite). O mapeamento Domain ↔ Row vive nos stores.</summary>

public sealed class OptimizationActionRow
{
    public Guid Id { get; set; }
    public string TaskKey { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Risk { get; set; } = "Low";
    public string Status { get; set; } = "Pending";
    public string ResultSummary { get; set; } = string.Empty;
    public DateTimeOffset StartedUtc { get; set; }
    public DateTimeOffset? FinishedUtc { get; set; }
    public long ElapsedMs { get; set; }

    /// <summary>JSON: lista de Guids dos backups associados.</summary>
    public string BackupIds { get; set; } = "[]";
}

public sealed class BackupRow
{
    public Guid Id { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string Target { get; set; } = string.Empty;
    public string Detail { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    /// <summary>Payload serializado do estado anterior (ex.: RegistryBackupPayload).</summary>
    public string PayloadJson { get; set; } = "{}";
    public DateTimeOffset CreatedUtc { get; set; }
    public DateTimeOffset? RestoredUtc { get; set; }
}

public sealed class TelemetrySampleRow
{
    public long Id { get; set; }
    public DateTimeOffset TimestampUtc { get; set; }
    public double? CpuPercent { get; set; }
    public double? RamUsedPercent { get; set; }
    public double? TemperatureC { get; set; }
    public double? DiskFreePercent { get; set; }
}

public sealed class SettingsRow
{
    public int Id { get; set; } = 1;
    public bool AdvancedMode { get; set; }
    public int ChartWindowHours { get; set; } = 12;
    public int NotificationAutoDismissSeconds { get; set; } = 6;
}

public sealed class BenchmarkRow
{
    public long Id { get; set; }
    public DateTimeOffset TimestampUtc { get; set; }
    public double? CpuIndex { get; set; }
    public double? MemAllocMbPerSec { get; set; }
    public double? DiskReadMbPerSec { get; set; }
    public double? DiskWriteMbPerSec { get; set; }
    public double DurationMs { get; set; }

    /// <summary>Ex.: "quick-before", "quick-after" — contexto da medição.</summary>
    public string Context { get; set; } = string.Empty;
}
