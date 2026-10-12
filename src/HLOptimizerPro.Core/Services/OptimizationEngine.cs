using HLOptimizerPro.Core.Models;
using Microsoft.Extensions.Logging;

namespace HLOptimizerPro.Core.Services;

/// <summary>Opções de uma execução de otimização.</summary>
public sealed class OptimizationRunOptions
{
    /// <summary>Nome do lote (aparece no histórico e no log).</summary>
    public string BatchName { get; init; } = "Otimização";

    /// <summary>Modo Simular: mostra o que seria alterado, sem escrever nada no sistema.</summary>
    public bool Simulation { get; init; }

    /// <summary>Criar ponto de restauro antes do lote (só para lotes com ações de risco).</summary>
    public bool CreateRestorePoint { get; init; } = true;

    /// <summary>Continuar mesmo quando uma ação falha (as seguintes podem ser independentes).</summary>
    public bool ContinueOnError { get; init; } = true;

    /// <summary>Categoria mostrada no histórico de atividades.</summary>
    public string ActivityCategory { get; init; } = "Otimização";
}

/// <summary>Progresso de um lote — ligado diretamente à barra de progresso da UI.</summary>
public sealed class OptimizationProgress
{
    public required string ActionTitle { get; init; }
    public required int Index { get; init; }
    public required int Total { get; init; }
    public required bool Success { get; init; }
    public string Message { get; init; } = string.Empty;

    public double Percentage => Total <= 0 ? 0 : Index * 100.0 / Total;
}

/// <summary>
/// Motor de execução de otimizações.
///
/// Responsabilidades (e o que o mantém seguro):
///   1. Verificar privilégios (nada de falhas silenciosas).
///   2. Criar ponto de restauro quando o lote tem ações de risco.
///   3. Respeitar o "modo Simular" (nenhuma escrita).
///   4. Registar TUDO no diário antes de escrever (a reversão é garantida).
///   5. Isolar falhas: uma ação que falha não impede as restantes nem deixa o sistema a meio.
///
/// A execução é assíncrona e reporta progresso — a UI nunca bloqueia (requisito de aceitação).
/// </summary>
public sealed class OptimizationEngine
{
    private readonly ILogger<OptimizationEngine> _logger;
    private readonly OptimizationJournal _journal;
    private readonly JournalReverter _reverter;
    private readonly RestorePointService _restorePoints;
    private readonly PrivilegeService _privileges;
    private readonly ActivityLogService _activity;

    public OptimizationEngine(
        ILogger<OptimizationEngine> logger,
        OptimizationJournal journal,
        JournalReverter reverter,
        RestorePointService restorePoints,
        PrivilegeService privileges,
        ActivityLogService activity)
    {
        _logger = logger;
        _journal = journal;
        _reverter = reverter;
        _restorePoints = restorePoints;
        _privileges = privileges;
        _activity = activity;
    }

    /// <summary>Progresso por ação (completado no dispatcher pelo ViewModel).</summary>
    public event EventHandler<OptimizationProgress>? ProgressChanged;

    /// <summary>Aplica um conjunto de otimizações.</summary>
    public async Task<Result> ApplyAsync(
        IReadOnlyList<OptimizationAction> actions,
        OptimizationRunOptions options,
        CancellationToken cancellationToken = default)
    {
        if (actions.Count == 0)
            return Result.Fail("Não há otimizações selecionadas para aplicar.");

        var applicable = actions.Where(a => a.IsAvailable).ToList();
        if (applicable.Count == 0)
            return Result.Fail("Nenhuma das otimizações selecionadas é aplicável a este computador.");

        var needsElevation = applicable.Any(a => a.RequiresElevation);
        if (needsElevation && !options.Simulation && !_privileges.IsElevated)
        {
            var check = _privileges.RequireElevation(options.BatchName);
            if (!check.Success) return check;
        }

        _logger.LogInformation("A aplicar «{Batch}» ({Count} ações) — simulação={Simulation}",
            options.BatchName, applicable.Count, options.Simulation);

        // ---- 1. Ponto de restauro (nunca no modo simulado) --------------------
        Result? restoreResult = null;
        if (!options.Simulation && options.CreateRestorePoint && applicable.Any(a => a.RequiresConfirmation || a.RiskLevel != HealthLevel.Otimo))
        {
            restoreResult = await _restorePoints.CreateAsync($"{Helpers.AppConstants.RestorePointDescription} — {options.BatchName}", cancellationToken)
                .ConfigureAwait(false);

            _activity.Log("Segurança", $"Ponto de restauro: {restoreResult.Message}", restoreResult.Success, restoreResult.TechnicalDetails ?? string.Empty);
        }

        // ---- 2. Execução -----------------------------------------------------
        var failures = new List<string>();
        var applied = 0;
        var total = applicable.Count;

        for (var index = 0; index < total; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var action = applicable[index];
            var order = index + 1;

            try
            {
                if (action.IsAlreadyApplied?.Invoke() == true)
                {
                    action.IsApplied = true;
                    Report(action, order, total, true, "Já estava aplicada.");
                    continue;
                }

                if (options.Simulation)
                {
                    // Mostra o que mudaria, sem tocar no sistema.
                    Report(action, order, total, true, action.SimulationText);
                    _activity.Log("Simulação", action.Title, details: action.SimulationText, simulated: true);
                    continue;
                }

                // Registar ANTES de escrever: se a escrita falhar a meio, o backup já está guardado.
                action.Apply?.Invoke(action);
                _journal.Record(action);
                action.IsApplied = true;
                applied++;

                Report(action, order, total, true, "Aplicado com sucesso.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Falha ao aplicar {Key}", action.Key);
                failures.Add($"{action.Title}: {ex.Message}");
                Report(action, order, total, false, ex.Message);

                if (!options.ContinueOnError) break;
            }
        }

        // ---- 3. Resumo -------------------------------------------------------
        var summary = BuildSummary(options, applied, total, failures.Count);

        _activity.Log(options.ActivityCategory, summary, success: failures.Count == 0,
            details: failures.Count > 0 ? string.Join(" | ", failures) : $"{applied} alterações reais",
            simulated: options.Simulation);

        if (restoreResult is { Success: false } && failures.Count == 0)
            summary += $" Aviso: {restoreResult.Message}";

        return failures.Count == 0 ? Result.Ok(summary) : Result.Fail(summary, string.Join("\n", failures));
    }

    /// <summary>Reverte uma otimização aplicada (usa o catálogo em memória; senão, o diário).</summary>
    public Result Revert(OptimizationAction action)
    {
        try
        {
            if (!_privileges.IsElevated && action.RequiresElevation)
            {
                var check = _privileges.RequireElevation($"Reverter «{action.Title}»");
                if (!check.Success) return check;
            }

            var entry = _journal.FindActive(action.Key);
            if (entry is not null)
            {
                // O diário sabe qual era o valor original — a fonte de verdade da reversão.
                var result = _reverter.Revert(entry);
                if (result.Success)
                {
                    if (action.Revert is not null)
                    {
                        var specific = action.Revert(action);
                        if (!specific.Success) _logger.LogDebug("Reversão específica devolveu: {Message}", specific.Message);
                    }

                    _journal.MarkReverted(action.Key);
                    action.IsApplied = false;
                }

                _activity.Log("Reversão", $"«{action.Title}»: {result.Message}", result.Success);
                return result;
            }

            // Sem registo no diário: usa a reversão declarada pela própria ação.
            if (action.Revert is null)
                return Result.Fail($"«{action.Title}» não tem reversão automática.");

            var fallback = action.Revert(action);
            if (fallback.Success) action.IsApplied = false;

            _activity.Log("Reversão", $"«{action.Title}»: {fallback.Message}", fallback.Success);
            return fallback;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha ao reverter {Key}", action.Key);
            return Result.Fail($"Erro ao reverter «{action.Title}»: {ex.Message}");
        }
    }

    /// <summary>Reverte todas as alterações ativas registadas no diário (funciona entre sessões).</summary>
    public async Task<Result> RevertAllAsync(CancellationToken cancellationToken = default)
    {
        var active = _journal.ActiveEntries;
        if (active.Count == 0)
            return Result.Fail("Não há alterações para reverter — o sistema está no estado original.");

        if (!_privileges.IsElevated)
        {
            var check = _privileges.RequireElevation("Reverter todas as otimizações");
            if (!check.Success) return check;
        }

        var reverted = 0;
        var failures = new List<string>();

        await Task.Run(() =>
        {
            for (var index = 0; index < active.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var entry = active[index];

                var result = _reverter.Revert(entry);
                if (result.Success)
                {
                    _journal.MarkReverted(entry.ActionKey);
                    reverted++;
                }
                else
                {
                    failures.Add($"{entry.Title}: {result.Message}");
                }

                ProgressChanged?.Invoke(this, new OptimizationProgress
                {
                    ActionTitle = entry.Title,
                    Index = index + 1,
                    Total = active.Count,
                    Success = result.Success,
                    Message = result.Message,
                });
            }
        }, cancellationToken).ConfigureAwait(false);

        var message = failures.Count == 0
            ? $"Foram revertidas {reverted} de {active.Count} alterações. Reinicie o computador para que tudo volte ao normal."
            : $"Revertidas {reverted} de {active.Count}. Falhas: {string.Join(" | ", failures)}";

        _activity.Log("Reversão", message, failures.Count == 0);
        return failures.Count == 0 ? Result.Ok(message) : Result.Fail(message, string.Join("\n", failures));
    }

    /// <summary>Pré-visualização do que um lote faria (usado pelo modo Simular e pelo passo de confirmação).</summary>
    public IReadOnlyList<string> Preview(IReadOnlyList<OptimizationAction> actions)
        => actions.Where(a => a.IsAvailable)
            .Select(a => $"{a.Title} → {a.Target}: «{a.CurrentValue}» → «{a.NewValue}»{(a.RequiresRestart ? " (requer reinício)" : string.Empty)}")
            .ToList();

    /// <summary>Nº de ações do lote que exigem confirmação explícita.</summary>
    public static bool RequiresConfirmation(IReadOnlyList<OptimizationAction> actions)
        => actions.Any(a => a.RequiresConfirmation || a.RiskLevel == HealthLevel.Critico);

    private static string BuildSummary(OptimizationRunOptions options, int applied, int total, int failureCount)
    {
        if (options.Simulation)
            return $"Simulação concluída: {total} alterações seriam aplicadas. Nada foi modificado no sistema.";

        var message = $"{options.BatchName}: {applied} de {total} otimizações aplicadas.";
        if (failureCount > 0) message += $" {failureCount} falharam (ver detalhes no histórico).";
        message += " Pode reverter tudo a qualquer momento em «Otimizações → Reverter».";
        return message;
    }

    private void Report(OptimizationAction action, int index, int total, bool success, string message)
        => ProgressChanged?.Invoke(this, new OptimizationProgress
        {
            ActionTitle = action.Title,
            Index = index,
            Total = total,
            Success = success,
            Message = message,
        });
}
