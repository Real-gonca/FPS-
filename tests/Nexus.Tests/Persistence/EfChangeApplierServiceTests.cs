using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Nexus.Domain.Optimization;
using Nexus.Domain.Ports;
using Nexus.Infrastructure.Commands;
using Nexus.Infrastructure.Persistence;
using Nexus.Tests.Fakes;
using Xunit;

namespace Nexus.Tests.Persistence;

/// <summary>
/// EfChangeApplier para ChangeKind.Service (Patch 2): backup guarda o estado
/// real (WMI probe fake) e o restauro executa EXATAMENTE os comandos sc.exe
/// corretos (start mode + realinhamento de estado) — base de EF SQLite real
/// (in-memory), validando também o mapeamento.
/// </summary>
public class EfChangeApplierServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ServiceProvider _provider;
    private readonly FakeProcessRunner _runner = new();
    private readonly WhitelistedCommandExecutor _executor =
        new(_runner, NullLogger<WhitelistedCommandExecutor>.Instance);
    private readonly FakeRegistry _registry = new();
    private readonly FakeServiceInspector _inspector = new();

    public EfChangeApplierServiceTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        var services = new ServiceCollection();
        services.AddDbContext<NexusDbContext>(o => o.UseSqlite(_connection));
        services.AddSingleton<IChangeApplier, EfChangeApplier>();
        services.AddSingleton<IRegistryAccess>(_registry);
        services.AddSingleton<IServiceInspector>(_inspector);
        services.AddSingleton<ISystemCommandExecutor>(_executor);

        _provider = services.BuildServiceProvider();
        _provider.GetRequiredService<NexusDbContext>().Database.EnsureCreated();
    }

    public void Dispose()
    {
        _provider.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public async Task ServiceBackup_StoresRealState_InPayload()
    {
        _inspector.Probe = new ServiceProbe("DiagTrack", "Connected User Experiences", true, "Manual", "Running");
        var applier = _provider.GetRequiredService<IChangeApplier>();

        var ids = await applier.BackupAsync(new[]
        {
            new ChangeDescriptor(ChangeKind.Service, "DiagTrack", "start=disabled", "teste"),
        });

        var id = Assert.Single(ids);
        var db = _provider.CreateScope().ServiceProvider.GetRequiredService<NexusDbContext>();
        var row = await db.Backups.FindAsync(new object[] { id });

        Assert.NotNull(row);
        Assert.Equal("Service", row!.Kind);
        Assert.Equal("DiagTrack", row.Target);
        Assert.Contains("\"StartMode\":\"Manual\"", row.PayloadJson);
        Assert.Contains("\"State\":\"Running\"", row.PayloadJson);
    }

    [Fact]
    public async Task ServiceRestore_ReappliesStartMode_AndState()
    {
        // Estado ANTERIOR: Manual + Running.
        _inspector.Probe = new ServiceProbe("DiagTrack", "Connected User Experiences", true, "Manual", "Running");
        var applier = _provider.GetRequiredService<IChangeApplier>();
        var ids = await applier.BackupAsync(new[]
        {
            new ChangeDescriptor(ChangeKind.Service, "DiagTrack", "start=disabled", "teste"),
        });

        // Após o (fictício) apply, o serviço está parado: Manual→Disabled, Stopped.
        _inspector.Probe = new ServiceProbe("DiagTrack", "Connected User Experiences", true, "Disabled", "Stopped");

        await applier.RestoreAsync(ids);

        // 1) devolve o start mode; 2) realinha o estado (Running → sc start).
        var calls = _runner.Calls.Select(c => c.Arguments).ToList();
        Assert.Contains("change DiagTrack start=demand", calls);
        Assert.Contains("start DiagTrack", calls);
        Assert.Equal(2, calls.Count);
        Assert.Equal(0, calls.IndexOf("change DiagTrack start=demand")); // start mode antes do estado
    }

    [Fact]
    public async Task ServiceRestore_WasStopped_StopsIfNowRunning()
    {
        _inspector.Probe = new ServiceProbe("Spooler", "Agente de Impressão", true, "Auto", "Stopped");
        var applier = _provider.GetRequiredService<IChangeApplier>();
        var ids = await applier.BackupAsync(new[]
        {
            new ChangeDescriptor(ChangeKind.Service, "Spooler", "start=auto", "teste"),
        });

        // Agora está em execução (divergiu) → restauro tem de parar.
        _inspector.Probe = new ServiceProbe("Spooler", "Agente de Impressão", true, "Auto", "Running");

        await applier.RestoreAsync(ids);

        var calls = _runner.Calls.Select(c => c.Arguments).ToList();
        Assert.Contains("change Spooler start=auto", calls);
        Assert.Contains("stop Spooler", calls);
    }

    [Fact]
    public async Task ServiceBackup_ServiceMissing_Throws_BeforeAnyWrite()
    {
        _inspector.Probe = new ServiceProbe("NãoExiste", string.Empty, false, null, null);
        var applier = _provider.GetRequiredService<IChangeApplier>();

        await Assert.ThrowsAsync<InvalidOperationException>(() => applier.BackupAsync(new[]
        {
            new ChangeDescriptor(ChangeKind.Service, "NãoExiste", "start=disabled", "teste"),
        }));
        Assert.Empty(_runner.Calls);
    }
}

internal sealed class FakeRegistry : IRegistryAccess
{
    public Task<RegistryProbe> ProbeAsync(string path, string valueName, CancellationToken ct = default) =>
        Task.FromResult(new RegistryProbe(false, null, null));

    public Task SetAsync(string path, string valueName, RegistryValueKind kind, object value, CancellationToken ct = default) =>
        Task.CompletedTask;

    public Task DeleteAsync(string path, string valueName, CancellationToken ct = default) =>
        Task.CompletedTask;
}

internal sealed class FakeServiceInspector : IServiceInspector
{
    public ServiceProbe? Probe { get; set; }

    public Task<ServiceProbe?> ProbeServiceAsync(string name, CancellationToken ct = default) =>
        Task.FromResult(Probe);
}
