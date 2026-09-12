using Microsoft.Extensions.Logging.Abstractions;
using Nexus.Application.Optimization;
using Nexus.Application.UseCases;
using Nexus.Domain.Optimization;
using Nexus.Domain.Ports;
using Nexus.Tests.Fakes;
using Xunit;

namespace Nexus.Tests.Optimization;

/// <summary>
/// Pipeline Backup → Apply → History → Rollback (spec §2.2): ordem de
/// operações, rollback automático em falha, e rollback manual do histórico.
/// </summary>
public class OrchestratorTests
{
    private readonly FakeHistoryStore _history = new();
    private readonly FakeChangeApplier _applier = new();
    private readonly FakeHub _hub = new();
    private readonly OptimizationOrchestrator _orchestrator =
        new(_applier, _history, _hub, NullLogger<OptimizationOrchestrator>.Instance);

    [Fact]
    public async Task HappyPath_BackupBeforeApply_HistorySuccess()
    {
        var task = new FakeTask();

        var result = await _orchestrator.ExecuteAsync(task);

        Assert.True(result.Success);
        Assert.Equal(OptimizationStatus.Success, result.Status);
        Assert.Single(_history.Items);
        var action = _history.Items[0];
        Assert.Equal(OptimizationStatus.Success, action.Status);
        Assert.Single(action.BackupIds);
        Assert.Single(_applier.BackedUp);
        Assert.Empty(_applier.RestoredIds);
        // Notificação tipada de sucesso emitida.
        var note = Assert.Single(_hub.EmittedLog);
        Assert.Equal(NotificationKind.Success, note.Kind);
    }

    [Fact]
    public async Task ApplyFailure_RollsBackAutomatically()
    {
        var task = new FakeTask(failApply: true);

        var result = await _orchestrator.ExecuteAsync(task);

        Assert.False(result.Success);
        Assert.Equal(OptimizationStatus.RolledBack, result.Status);
        Assert.Contains("revertidas", result.Message);

        var action = Assert.Single(_history.Items);
        Assert.Equal(OptimizationStatus.RolledBack, action.Status);

        // Restauro foi pedido (uma vez, pelo único backup criado).
        Assert.Single(_applier.RestoredIds);
        Assert.NotEqual(OptimizationStatus.Success, action.Status);

        var note = _hub.EmittedLog.Last();
        Assert.Equal(NotificationKind.Warning, note.Kind);
    }

    [Fact]
    public async Task BackupFailure_FailsWithoutApplyingAnything()
    {
        _applier.FailBackup = true;
        var task = new FakeTask();

        var result = await _orchestrator.ExecuteAsync(task);

        Assert.False(result.Success);
        Assert.Equal(OptimizationStatus.Failed, result.Status);
        Assert.Contains("NÃO foi aplicada", result.Message);
        Assert.Empty(_applier.RestoredIds);

        var action = Assert.Single(_history.Items);
        Assert.Empty(action.BackupIds);
    }

    [Fact]
    public async Task RollbackFailure_IsReportedWithoutSilence()
    {
        var task = new FakeTask(failApply: true);
        // O apply falha E o rollback automático também falha.
        _applier.FailRestore = true;

        var result = await _orchestrator.ExecuteAsync(task);

        Assert.False(result.Success);
        Assert.Equal(OptimizationStatus.Failed, result.Status);
        Assert.Contains("rollback automático não concluiu", result.Message);

        var note = _hub.EmittedLog.Last();
        Assert.Equal(NotificationKind.Error, note.Kind);
    }

    public class RollbackUseCaseTests
    {
        private readonly FakeHistoryStore _history = new();
        private readonly FakeChangeApplier _applier = new();
        private readonly FakeHub _hub = new();
        private readonly RollbackUseCase _useCase =
            new(_history, _applier, _hub, NullLogger<RollbackUseCase>.Instance);

        [Fact]
        public async Task Rollback_RestoresAndMarksAction()
        {
            var orchestrator = new OptimizationOrchestrator(
                _applier, _history, _hub, NullLogger<OptimizationOrchestrator>.Instance);
            var result = await orchestrator.ExecuteAsync(new FakeTask());
            var actionId = result.ActionId!.Value;

            var rollback = await _useCase.RollbackAsync(actionId);

            Assert.True(rollback.Success);
            Assert.Single(_applier.RestoredIds);
            var action = Assert.Single(_history.Items);
            Assert.Equal(OptimizationStatus.RolledBack, action.Status);
        }

        [Fact]
        public async Task Rollback_UnknownAction_FailsGracefully()
        {
            var rollback = await _useCase.RollbackAsync(Guid.NewGuid());

            Assert.False(rollback.Success);
            Assert.Contains("não encontrada", rollback.Message);
        }

        [Fact]
        public async Task Rollback_ActionWithoutBackups_FailsGracefully()
        {
            var action = new OptimizationAction
            {
                TaskKey = "x",
                Name = "x",
                Status = OptimizationStatus.Success,
            };
            await _history.AddAsync(action);

            var rollback = await _useCase.RollbackAsync(action.Id);

            Assert.False(rollback.Success);
            Assert.Contains("não tem backups", rollback.Message);
        }
    }

    public class RunOptimizationUseCaseTests
    {
        private readonly FakeHub _hub = new();
        private readonly FakeChangeApplier _applier = new();
        private readonly FakeHistoryStore _history = new();

        [Fact]
        public async Task UnknownTaskKey_ReturnsFail_WithoutSideEffects()
        {
            var useCase = Build(elevated: true, Array.Empty<IOptimizationTask>());

            var result = await useCase.ExecuteAsync("não-existe");

            Assert.False(result.Success);
            Assert.Equal(OptimizationStatus.Failed, result.Status);
        }

        [Fact]
        public async Task ElevationRequired_NotElevated_IsBlocked()
        {
            var task = new FakeTask(requiresElevation: true);
            var useCase = Build(elevated: false, new[] { task });

            var result = await useCase.ExecuteAsync(task.Key);

            Assert.False(result.Success);
            Assert.Equal(OptimizationStatus.Blocked, result.Status);
            Assert.Empty(_applier.BackedUp); // nada foi feito
            var note = Assert.Single(_hub.EmittedLog);
            Assert.Equal(NotificationKind.Warning, note.Kind);
        }

        [Fact]
        public async Task ElevationRequired_Elevated_RunsNormally()
        {
            var task = new FakeTask(requiresElevation: true);
            var useCase = Build(elevated: true, new[] { task });

            var result = await useCase.ExecuteAsync(task.Key);

            Assert.True(result.Success);
        }

        private RunOptimizationUseCase Build(bool elevated, IOptimizationTask[] tasks)
        {
            var elevation = new FakeElevation { IsElevated = elevated };
            var catalog = new FakeCatalog(tasks);
            var orchestrator = new OptimizationOrchestrator(
                _applier, _history, _hub, NullLogger<OptimizationOrchestrator>.Instance);

            return new RunOptimizationUseCase(
                catalog,
                elevation,
                orchestrator,
                _hub,
                NullLogger<RunOptimizationUseCase>.Instance);
        }
    }
}
