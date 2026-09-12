using Microsoft.Extensions.Logging.Abstractions;
using Nexus.Application.Tasks;
using Nexus.Domain.Optimization;
using Nexus.Domain.Ports;
using Nexus.Infrastructure.Commands;
using Nexus.Tests.Fakes;
using Xunit;

namespace Nexus.Tests.Tasks;

/// <summary>
/// ServiceTask: o comando gerado é EXATAMENTE o que a whitelist permite;
/// rejeições da whitelist propagam como falha (o orquestrador faz rollback);
/// exit codes idempotentes do sc.exe (1056/1062) não são erros.
/// </summary>
public class ServiceTaskTests
{
    private readonly FakeProcessRunner _runner = new();
    private readonly WhitelistedCommandExecutor _executor =
        new(_runner, NullLogger<WhitelistedCommandExecutor>.Instance);

    private ServiceTask Build(ServiceOperation op, string? mode = null) => new(
        key: "test",
        serviceKey: "DiagTrack",
        displayName: "Test Service",
        operation: op,
        desiredStartMode: mode,
        justification: "teste",
        executor: _executor);

    [Fact]
    public async Task ChangeStartMode_RunsExactWhitelistedCommand()
    {
        var task = Build(ServiceOperation.ChangeStartMode, "disabled");

        await task.ApplyAsync();

        Assert.Single(_runner.Calls);
        Assert.Equal("sc.exe", _runner.Calls[0].FileName);
        Assert.Equal("change DiagTrack start=disabled", _runner.Calls[0].Arguments);
    }

    [Fact]
    public async Task Start_RunsStartCommand()
    {
        var task = Build(ServiceOperation.Start);

        await task.ApplyAsync();

        Assert.Equal("start DiagTrack", _runner.Calls[0].Arguments);
    }

    [Fact]
    public async Task Stop_RunsStopCommand()
    {
        var task = Build(ServiceOperation.Stop);

        await task.ApplyAsync();

        Assert.Equal("stop DiagTrack", _runner.Calls[0].Arguments);
    }

    [Fact]
    public async Task DescribeChanges_UsesServiceKind_WithTargetServiceKey()
    {
        var task = Build(ServiceOperation.ChangeStartMode, "demand");

        var changes = task.DescribeChanges();

        var change = Assert.Single(changes);
        Assert.Equal(ChangeKind.Service, change.Kind);
        Assert.Equal("DiagTrack", change.Target);
        Assert.Equal("start=demand", change.Detail);
        Assert.False(string.IsNullOrWhiteSpace(change.Description));
    }

    [Fact]
    public async Task ServiceKeyWithShellMeta_IsRejected_AndThrows()
    {
        var task = new ServiceTask(
            key: "bad",
            serviceKey: "DiagTrack & x",
            displayName: "Bad",
            operation: ServiceOperation.ChangeStartMode,
            desiredStartMode: "disabled",
            justification: "teste",
            executor: _executor);

        await Assert.ThrowsAsync<InvalidOperationException>(() => task.ApplyAsync());
        Assert.Empty(_runner.Calls);
    }

    [Fact]
    public async Task NonZeroExitCode_Throws_WithScError()
    {
        _runner.Result = new(1058, string.Empty, "ERROR_SERVICE_DOES_NOT_EXIST", false);
        var task = Build(ServiceOperation.Start);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => task.ApplyAsync());
        Assert.Contains("1058", ex.Message);
    }

    [Fact]
    public async Task Start_AlreadyRunning_1056_IsSuccess_Idempotent()
    {
        _runner.Result = new(1056, string.Empty, "ERROR_SERVICE_ALREADY_RUNNING", false);
        var task = Build(ServiceOperation.Start);

        await task.ApplyAsync(); // não lança

        Assert.Single(_runner.Calls);
    }

    [Fact]
    public async Task Stop_AlreadyStopped_1062_IsSuccess_Idempotent()
    {
        _runner.Result = new(1062, string.Empty, "ERROR_SERVICE_NOT_ACTIVE", false);
        var task = Build(ServiceOperation.Stop);

        await task.ApplyAsync(); // não lança

        Assert.Single(_runner.Calls);
    }

    [Fact]
    public void Task_Metadata_IsReversible_AndRequiresElevation()
    {
        var task = Build(ServiceOperation.ChangeStartMode, "disabled");

        Assert.True(task.Reversible);
        Assert.True(task.RequiresElevation);
        Assert.Equal(RiskLevel.Low, task.Risk);
    }
}
