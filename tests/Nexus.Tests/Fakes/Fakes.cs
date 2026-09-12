using Nexus.Application.Scoring;
using Nexus.Domain.Metrics;
using Nexus.Domain.Optimization;
using Nexus.Domain.Ports;
using Nexus.Infrastructure.Commands;

namespace Nexus.Tests.Fakes;

/// <summary>Snapshot canónico para testes (valores coerentes).</summary>
public static class SnapshotFacts
{
    /// <summary>
    /// disco: 307,2 GB livres de 512 GB (60%) · RAM: 6 553,6 MB livres de 16 384 MB (40%)
    /// </summary>
    public static SystemTelemetrySnapshot Standard(
        double? cpu = 30,
        double? temperature = 45,
        double diskFreeGb = 307.2,
        double ramFreeMb = 6553.6)
    {
        return new SystemTelemetrySnapshot(
            DateTimeOffset.UtcNow,
            MetricValue.Of(cpu),
            MetricValue.Of(ramFreeMb is { } f ? 16384.0 - f : null),
            MetricValue.Of(ramFreeMb),
            MetricValue.Of(16384.0),
            MetricValue.Of(diskFreeGb),
            MetricValue.Of(512.0),
            MetricValue.Missing(),
            MetricValue.Of(temperature),
            TimeSpan.FromHours(10),
            214);
    }
}

public sealed class FakeHistoryStore : IHistoryStore
{
    public List<OptimizationAction> Items { get; } = new();

    public Task AddAsync(OptimizationAction action, CancellationToken ct = default)
    {
        Items.Add(action);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(OptimizationAction action, CancellationToken ct = default)
    {
        // A mesma instância vive na lista (como num store real por id).
        return Task.CompletedTask;
    }

    public Task<OptimizationAction?> FindAsync(Guid id, CancellationToken ct = default)
    {
        var found = Items.FirstOrDefault(a => a.Id == id);
        return Task.FromResult(found);
    }

    public Task<IReadOnlyList<OptimizationAction>> QueryAsync(int take = 100, CancellationToken ct = default)
    {
        IReadOnlyList<OptimizationAction> items = Items.TakeLast(take).Reverse().ToList();
        return Task.FromResult(items);
    }
}

public sealed class FakeChangeApplier : IChangeApplier
{
    public List<ChangeDescriptor> BackedUp { get; } = new();
    public List<Guid> RestoredIds { get; } = new();
    public bool FailBackup { get; set; }
    public bool FailRestore { get; set; }

    public async Task<IReadOnlyList<Guid>> BackupAsync(IEnumerable<ChangeDescriptor> changes, CancellationToken ct = default)
    {
        await Task.Yield();
        if (FailBackup)
            throw new InvalidOperationException("Falha simulada de backup.");

        var ids = new List<Guid>();
        foreach (var c in changes)
        {
            BackedUp.Add(c);
            ids.Add(Guid.NewGuid());
        }
        return ids;
    }

    public Task RestoreAsync(IEnumerable<Guid> backupIds, CancellationToken ct = default)
    {
        if (FailRestore)
            throw new InvalidOperationException("Falha simulada de restauro.");

        // Guarda em ordem INVERSA (contrato do orquestrator/aplicador).
        RestoredIds.AddRange(backupIds.Reverse());
        return Task.CompletedTask;
    }
}

public sealed class FakeTelemetry : ISystemTelemetry
{
    public SystemTelemetrySnapshot Snapshot { get; set; } = SnapshotFacts.Standard();

    public Task<SystemTelemetrySnapshot> GetSnapshotAsync(CancellationToken ct = default) =>
        Task.FromResult(Snapshot);
}

public sealed class FakeSettings : ISettingsService
{
    public AppSettings Current { get; } = new();

    public Task<AppSettings> LoadAsync(CancellationToken ct = default) =>
        Task.FromResult(Current);

    public Task SaveAsync(AppSettings settings, CancellationToken ct = default)
    {
        Current = settings;
        return Task.CompletedTask;
    }
}

public sealed class FakeProcessRunner : IProcessRunner
{
    public List<(string FileName, string Arguments, TimeSpan Timeout)> Calls { get; } = new();

    public ProcessRunResult Result { get; set; } = new(0, "ok", string.Empty, false);

    public Task<ProcessRunResult> RunAsync(string fileName, string arguments, TimeSpan timeout, CancellationToken ct)
    {
        Calls.Add((fileName, arguments, timeout));
        return Task.FromResult(Result);
    }
}

public sealed class FakeStorage : IStorageProbe
{
    public StorageProbeResult Result { get; set; } = new(null, false, @"C:\Temp");

    public Task<StorageProbeResult> MeasureAsync(string rootPath, TimeSpan timeBox, CancellationToken ct = default) =>
        Task.FromResult(Result);
}

public sealed class FakeHub : INotificationHub
{
    public List<NotificationEmittedEventArgs> EmittedLog { get; } = new();

    public event EventHandler<NotificationEmittedEventArgs>? Emitted;

    public Task EmitAsync(NotificationKind kind, string title, string message, CancellationToken ct = default)
    {
        var args = new NotificationEmittedEventArgs(kind, title, message, DateTimeOffset.UtcNow);
        EmittedLog.Add(args);
        Emitted?.Invoke(this, args);
        return Task.CompletedTask;
    }
}

/// <summary>Tarefa de teste controlável (aplicar com sucesso/falha).</summary>
public sealed class FakeTask : IOptimizationTask
{
    public FakeTask(string key = "fake-task", bool failApply = false, bool requiresElevation = false)
    {
        Key = key;
        FailApply = failApply;
        RequiresElevation = requiresElevation;
    }

    public bool FailApply { get; set; }

    public string Key { get; }
    public string Name => "Tarefa de teste";
    public string Description => "Tarefa de teste para unitários.";
    public string? DocumentationUrl => null;
    public RiskLevel Risk => RiskLevel.Low;
    public bool Reversible => true;
    public bool RequiresElevation { get; }
    public FeatureVisibility Visibility => FeatureVisibility.Simple;
    public TimeSpan EstimatedDuration => TimeSpan.FromSeconds(1);

    public IReadOnlyList<ChangeDescriptor> DescribeChanges() => new[]
    {
        new ChangeDescriptor(ChangeKind.Registry, @"HKLM\SOFTWARE\Test", "Value1", "backup de teste"),
    };

    public Task ApplyAsync(CancellationToken ct = default)
    {
        if (FailApply)
            throw new InvalidOperationException("Falha simulada no apply.");
        return Task.CompletedTask;
    }
}

public sealed class FakeElevation : IElevationService
{
    public bool IsElevated { get; set; } = true;
    public Task<bool> RequestElevationAsync() => Task.FromResult(IsElevated);
}

/// <summary>Catálogo manual para testes.</summary>
public sealed class FakeCatalog : IOptimizationTaskCatalog
{
    private readonly Dictionary<string, IOptimizationTask> _byKey;

    public FakeCatalog(params IOptimizationTask[] tasks)
    {
        _byKey = tasks.ToDictionary(t => t.Key, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyCollection<IOptimizationTask> All => _byKey.Values;
    public IOptimizationTask? Find(string key) => _byKey.TryGetValue(key, out var t) ? t : null;
    public bool Has(string key) => _byKey.ContainsKey(key);
}
