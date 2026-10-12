using HLOptimizerPro.Core.Models;
using HLOptimizerPro.Core.Services.Abstractions;
using HLOptimizerPro.Core.Services.System;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace HLOptimizerPro.Core.Services;

/// <summary>
/// Reversão genérica a partir do diário.
///
/// Porque é necessária? Porque uma otimização aplicada hoje pode ter de ser revertida depois de
/// a aplicação ser atualizada — e um catálogo novo pode já não ter a chave correspondente.
/// Como o diário guarda os valores originais de forma estruturada (ver <see cref="BackupKeys"/>),
/// conseguimos reverter SEM depender do catálogo.
/// </summary>
public sealed class JournalReverter
{
    private readonly IRegistryProvider _registry;
    private readonly IProcessRunner _runner;
    private readonly ILogger<JournalReverter> _logger;

    public JournalReverter(IRegistryProvider registry, IProcessRunner runner, ILogger<JournalReverter> logger)
    {
        _registry = registry;
        _runner = runner;
        _logger = logger;
    }

    /// <summary>Reverte uma entrada do diário usando apenas os dados guardados. Nunca lança exceção.</summary>
    public Result Revert(JournalEntry entry)
    {
        try
        {
            var backup = entry.Backup;

            if (backup.ContainsKey(BackupKeys.RegHive))
                return RevertRegistry(entry, backup);

            if (backup.ContainsKey(BackupKeys.ServiceName))
                return RevertService(entry, backup);

            if (backup.TryGetValue(BackupKeys.CommandRevert, out var command) && !string.IsNullOrWhiteSpace(command))
                return RunRevertCommand(entry, command);

            if (backup.TryGetValue(BackupKeys.PowerRevert, out var power) && !string.IsNullOrWhiteSpace(power))
                return RunRevertCommand(entry, power);

            return Result.Fail($"«{entry.Title}»: não há informação suficiente para reverter automaticamente. " +
                               "Consulte o histórico e reverta manualmente (o backup .reg está em %AppData%\\HLOptimizerPro\\backups).");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha ao reverter {Key}", entry.ActionKey);
            return Result.Fail($"Erro ao reverter «{entry.Title}»: {ex.Message}");
        }
    }

    private Result RevertRegistry(JournalEntry entry, Dictionary<string, string?> backup)
    {
        var hive = backup.GetValueOrDefault(BackupKeys.RegHive);
        var subKey = backup.GetValueOrDefault(BackupKeys.RegSubKey);
        if (string.IsNullOrWhiteSpace(hive) || string.IsNullOrWhiteSpace(subKey))
            return Result.Fail("Backup do registo incompleto.");

        var rawName = backup.GetValueOrDefault(BackupKeys.RegValueName);
        var valueName = string.IsNullOrEmpty(rawName) ? null : rawName;
        var kind = Enum.TryParse<RegistryValueKind>(backup.GetValueOrDefault(BackupKeys.RegKind), true, out var parsed)
            ? parsed
            : RegistryValueKind.DWord;

        var existed = string.Equals(backup.GetValueOrDefault(BackupKeys.RegExisted), "true", StringComparison.OrdinalIgnoreCase);

        if (!existed)
        {
            var removed = _registry.DeleteValue(hive, subKey, valueName);
            return removed
                ? Result.Ok($"«{entry.Title}» revertido (valor removido — não existia originalmente).")
                : Result.Fail($"«{entry.Title}»: não foi possível remover o valor criado.");
        }

        var raw = backup.GetValueOrDefault(BackupKeys.RegOriginal) ?? string.Empty;
        var value = kind switch
        {
            RegistryValueKind.DWord => int.TryParse(raw, out var i) ? i : (object)0,
            RegistryValueKind.QWord => long.TryParse(raw, out var l) ? l : (object)0L,
            _ => raw,
        };

        var ok = _registry.SetValue(hive, subKey, valueName, value, kind);
        return ok
            ? Result.Ok($"«{entry.Title}» revertido para o valor original.")
            : Result.Fail($"«{entry.Title}»: não foi possível restaurar o valor original.");
    }

    private Result RevertService(JournalEntry entry, Dictionary<string, string?> backup)
    {
        var name = backup.GetValueOrDefault(BackupKeys.ServiceName);
        if (string.IsNullOrWhiteSpace(name)) return Result.Fail("Backup do serviço incompleto.");

        var startText = backup.GetValueOrDefault(BackupKeys.ServiceOriginalStart);
        if (!int.TryParse(startText, out var start)) return Result.Fail($"Sem tipo de arranque original para «{name}».");

        _registry.SetValue("HKLM", $@"SYSTEM\CurrentControlSet\Services\{name}", "Start", start, RegistryValueKind.DWord);

        var keyword = start switch { 2 => "auto", 3 => "demand", 4 => "disabled", _ => "demand" };
        _runner.Run("sc.exe", $"config \"{name}\" start= {keyword}");

        return Result.Ok($"Serviço «{name}» restaurado para o tipo de arranque original.");
    }

    private Result RunRevertCommand(JournalEntry entry, string command)
    {
        var index = command.IndexOf(' ');
        var file = index < 0 ? command : command[..index];
        var args = index < 0 ? string.Empty : command[(index + 1)..];

        var result = _runner.Run(file, args);
        return result.Success
            ? Result.Ok($"«{entry.Title}» revertido.")
            : Result.Fail($"«{entry.Title}»: a reversão falhou ({result.Output}).");
    }
}
