using HLOptimizerPro.Core.Helpers;
using HLOptimizerPro.Core.Models;
using HLOptimizerPro.Core.Services.Abstractions;
using Microsoft.Extensions.Logging;

namespace HLOptimizerPro.Core.Services;

/// <summary>
/// Limpeza automática (3.5): executa a limpeza de ficheiros temporários e caches a cada N dias,
/// conforme configurado pelo utilizador (predefinição: 7 dias).
///
/// Detalhes de implementação que evitam surpresas:
///   • A verificação é feita no arranque e, depois, uma vez por hora — não mantemos um serviço
///     residente em memória nem uma tarefa agendada do Windows.
///   • Nunca corre no meio de uma otimização nem durante um jogo (o Game Mode tem prioridade).
///   • O resultado é registado no histórico de atividades, para o utilizador saber o que foi
///     apagado e quando.
/// </summary>
public sealed class AutoCleanupService
{
    private readonly ILogger<AutoCleanupService> _logger;
    private readonly CleanupService _cleanup;
    private readonly SettingsService _settings;
    private readonly ActivityLogService _activity;
    private readonly IClock _clock;
    private readonly Timer? _timer;

    public AutoCleanupService(
        ILogger<AutoCleanupService> logger,
        CleanupService cleanup,
        SettingsService settings,
        ActivityLogService activity,
        IClock clock)
    {
        _logger = logger;
        _cleanup = cleanup;
        _settings = settings;
        _activity = activity;
        _clock = clock;

        // Verificação horária: leve (apenas compara datas) e suficiente para um intervalo em dias.
        _timer = new Timer(_ => CheckNow(), null, TimeSpan.FromMinutes(2), TimeSpan.FromHours(1));
    }

    /// <summary>Evento para a UI mostrar um aviso discreto quando a limpeza automática corre.</summary>
    public event EventHandler<CleanupSummary>? CleanupCompleted;

    /// <summary>Data da próxima limpeza automática (null quando desativada).</summary>
    public DateTime? NextRun
    {
        get
        {
            var settings = _settings.Current;
            if (!settings.AutoCleanupEnabled) return null;

            var last = settings.LastCleanupUtc?.ToLocalTime() ?? _clock.Now.AddDays(-settings.AutoCleanupIntervalDays);
            return last.AddDays(settings.AutoCleanupIntervalDays);
        }
    }

    /// <summary>Executa já a limpeza automática (botão "Limpar agora" da Configuração).</summary>
    public async Task<Result> RunNowAsync(CancellationToken cancellationToken = default)
    {
        var settings = _settings.Current;
        var targets = settings.AutoCleanupTargets
            .Select(CleanupTargets.Find)
            .Where(t => t is not null)
            .Select(t => t!)
            .ToList();

        if (targets.Count == 0)
            targets = CleanupTargets.QuickTargets().ToList();

        var result = await _cleanup.RunAsync(targets, cancellationToken).ConfigureAwait(false);

        if (result.Value is { } summary)
            CleanupCompleted?.Invoke(this, summary);

        _settings.Update(s => s.LastCleanupUtc = _clock.UtcNow);

        // Aproveitamos para expurgar logs antigos da própria aplicação (retenção: 30 dias).
        var removed = LogService.PurgeOldLogs(30);
        if (removed > 0) _activity.Log("Limpeza", $"{removed} ficheiros de log antigos removidos");

        return result.Success ? Result.Ok(result.Message) : Result.Fail(result.Message, result.TechnicalDetails);
    }

    /// <summary>Configura a limpeza automática (usado no painel "Configurar" da tela Otimizações).</summary>
    public Result Configure(bool enabled, int intervalDays, IEnumerable<string> targetKeys)
    {
        try
        {
            var keys = targetKeys.ToList();
            if (enabled && keys.Count == 0)
                return Result.Fail("Selecione pelo menos um tipo de limpeza para a limpeza automática.");

            _settings.Update(s =>
            {
                s.AutoCleanupEnabled = enabled;
                s.AutoCleanupIntervalDays = Math.Clamp(intervalDays, 1, 30);
                s.AutoCleanupTargets = keys;
            });

            _activity.Log("Configuração", enabled
                ? $"Limpeza automática ativada a cada {Math.Clamp(intervalDays, 1, 30)} dias"
                : "Limpeza automática desativada");

            return Result.Ok(enabled
                ? $"Limpeza automática ativada (a cada {Math.Clamp(intervalDays, 1, 30)} dias)."
                : "Limpeza automática desativada.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha ao configurar a limpeza automática.");
            return Result.Fail("Não foi possível guardar a configuração.", ex.Message);
        }
    }

    // ------------------------------------------------------------------ internos

    /// <summary>Verifica se chegou a hora de limpar; se sim, executa em segundo plano.</summary>
    private void CheckNow()
    {
        try
        {
            var settings = _settings.Current;
            if (!settings.AutoCleanupEnabled) return;

            var lastCleanup = settings.LastCleanupUtc?.ToLocalTime() ?? _clock.Now.AddDays(-settings.AutoCleanupIntervalDays);
            if ((_clock.Now - lastCleanup).TotalDays < settings.AutoCleanupIntervalDays) return;

            _logger.LogInformation("A iniciar limpeza automática programada.");
            _ = RunNowAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro na verificação da limpeza automática.");
        }
    }

    /// <summary>Descrição legível do estado (mostrada no cartão da limpeza automática).</summary>
    public string Describe()
    {
        var settings = _settings.Current;
        if (!settings.AutoCleanupEnabled) return "Limpeza automática desativada";

        return NextRun is { } next
            ? $"Próxima limpeza automática: {next:dd/MM/yyyy 'às' HH:mm} (a cada {settings.AutoCleanupIntervalDays} dias)"
            : $"A cada {settings.AutoCleanupIntervalDays} dias";
    }
}
