using Microsoft.Extensions.Logging.Abstractions;
using Nexus.Application.Optimization;
using Nexus.Application.Profiles;
using Nexus.Application.UseCases;
using Nexus.Domain.Optimization;
using Nexus.Domain.Ports;
using Nexus.Infrastructure.Commands;
using Nexus.Tests.Fakes;
using Xunit;

namespace Nexus.Tests.UseCases;

/// <summary>
/// ServiceOperationUseCase: gatekeeping de elevação (nada roda "de rastejo")
/// e perfil interrompido em falha (as alterações restantes não correm).
/// </summary>
public class ServiceOperationUseCaseTests
{
    private readonly FakeHub _hub = new();
    private readonly FakeChangeApplier _applier = new();
    private readonly FakeHistoryStore _history = new();
    private readonly FakeProcessRunner _runner = new();
    private readonly WhitelistedCommandExecutor _executor =
        new(_runner, NullLogger<WhitelistedCommandExecutor>.Instance);

    private ServiceOperationUseCase Build(bool elevated)
    {
        var elevation = new FakeElevation { IsElevated = elevated };
        var orchestrator = new OptimizationOrchestrator(
            _applier, _history, _hub, NullLogger<OptimizationOrchestrator>.Instance);
        return new ServiceOperationUseCase(
            elevation, orchestrator, _hub, _executor,
            NullLogger<ServiceOperationUseCase>.Instance);
    }

    [Fact]
    public async Task NotElevated_OperationIsBlocked_NoSideEffects()
    {
        var useCase = Build(elevated: false);

        var result = await useCase.ChangeStartModeAsync("DiagTrack", "Telemetria", "disabled", "teste");

        Assert.False(result.Success);
        Assert.Equal(OptimizationStatus.Blocked, result.Status);
        Assert.Empty(_applier.BackedUp);
        var note = Assert.Single(_hub.EmittedLog);
        Assert.Equal(NotificationKind.Warning, note.Kind);
    }

    [Fact]
    public async Task Elevated_ChangeStartMode_RunsPipeline()
    {
        var useCase = Build(elevated: true);

        var result = await useCase.ChangeStartModeAsync("DiagTrack", "Telemetria", "disabled", "teste");

        Assert.True(result.Success);
        // O backup (ChangeKind.Service) foi criado antes do apply.
        var backup = Assert.Single(_applier.BackedUp);
        Assert.Equal(ChangeKind.Service, backup.Kind);
        Assert.Equal("DiagTrack", backup.Target);
        // E o sc.exe correu com o comando exacto.
        Assert.Contains("change DiagTrack start=disabled", _runner.Calls.Select(c => c.Arguments));
    }

    [Fact]
    public async Task Profile_FirstChangeFails_RemainingChangesDoNotRun()
    {
        // Faz a primeira alteração do perfil falhar (sc.exe exit 1058).
        _runner.Result = new(1058, string.Empty, "ERROR_SERVICE_DOES_NOT_EXIST", false);
        var useCase = Build(elevated: true);

        var profile = ServiceProfileCatalog.Find("privacy")!;
        var outcome = await useCase.ApplyProfileAsync(profile);

        Assert.False(outcome.FullyApplied);
        Assert.Equal(0, outcome.Applied);
        Assert.True(outcome.Failed >= 1);
        Assert.Equal(profile.Changes.Count - 1, outcome.Skipped);

        // Apenas a primeira alteração foi tentada (1 backup, 1 comando sc).
        Assert.Single(_applier.BackedUp);
        var changeCalls = _runner.Calls.Count(c => c.Arguments.StartsWith("change "));
        Assert.Equal(1, changeCalls);
    }

    [Fact]
    public async Task NotElevated_ProfileIsBlockedWithNotification()
    {
        var useCase = Build(elevated: false);
        var profile = ServiceProfileCatalog.Find("gaming")!;

        var outcome = await useCase.ApplyProfileAsync(profile);

        Assert.False(outcome.FullyApplied);
        Assert.Empty(_applier.BackedUp);
        Assert.Contains(_hub.EmittedLog, n => n.Kind == NotificationKind.Warning);
    }
}
