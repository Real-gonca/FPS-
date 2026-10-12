using HLOptimizerPro.Core.Helpers;
using HLOptimizerPro.Core.Models;
using HLOptimizerPro.Core.Services.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace HLOptimizerPro.Core.Services.System;

/// <summary>Especificação de um tweak do registo. Descreve valor "otimizado" e "original".</summary>
public sealed class RegistryTweakSpec
{
    public required string Key { get; init; }
    public required string Title { get; init; }
    public required string Description { get; init; }
    public required string Hive { get; init; }
    public required string SubKey { get; init; }
    public string? ValueName { get; init; }
    public RegistryValueKind Kind { get; init; } = RegistryValueKind.DWord;
    public object? OptimizedValue { get; init; }

    /// <summary>Valor considerado "não otimizado" (para detetar se já está aplicado).</summary>
    public object? BaseValue { get; init; }

    public OptimizationCategory Category { get; init; } = OptimizationCategory.Sistema;
    public OptimizationArea Area { get; init; } = OptimizationArea.DesempenhoSistema;
    public string Risk { get; init; } = "Baixo";
    public HealthLevel RiskLevel { get; init; } = HealthLevel.Otimo;
    public bool RequiresRestart { get; init; } = true;
    public bool RequiresConfirmation { get; init; }
    public bool RequiresElevation { get; init; } = true;

    /// <summary>Descrição adicional apresentada no modo Simular.</summary>
    public string? SimulationNote { get; init; }

    /// <summary>Predicado opcional: o tweak só aparece se devolver true (ex.: Windows 11 apenas).</summary>
    public Func<bool>? AvailabilityPredicate { get; init; }

    /// <summary>Chaves de registo a exportar para backup (.reg) antes de escrever.</summary>
    public string BackupFolder { get; init; } = "registo";
}

/// <summary>Especificação de um tweak sobre um serviço do Windows (valor <c>Start</c>).</summary>
public sealed class ServiceTweakSpec
{
    public required string Key { get; init; }
    public required string Title { get; init; }
    public required string Description { get; init; }
    public required string ServiceName { get; init; }

    /// <summary>3 = Manual (recomendado para serviços que só correm a pedido), 4 = Desativado.</summary>
    public int OptimizedStart { get; init; } = 3;

    public string Risk { get; init; } = "Médio — o serviço deixa de arrancar automaticamente";
    public HealthLevel RiskLevel { get; init; } = HealthLevel.Atencao;
    public OptimizationCategory Category { get; init; } = OptimizationCategory.Sistema;
    public OptimizationArea Area { get; init; } = OptimizationArea.DesempenhoSistema;
    public Func<bool>? AvailabilityPredicate { get; init; }
}

/// <summary>Especificação de um tweak aplicado por comando externo (netsh, powercfg, bcdedit...).</summary>
public sealed class CommandTweakSpec
{
    public required string Key { get; init; }
    public required string Title { get; init; }
    public required string Description { get; init; }
    public required string Target { get; init; }

    /// <summary>Ficheiro a executar (ex.: "powercfg").</summary>
    public required string FileName { get; init; }

    /// <summary>Argumentos de aplicação.</summary>
    public required string ApplyArguments { get; init; }

    /// <summary>Argumentos de reversão (executados no Revert).</summary>
    public string? RevertArguments { get; init; }

    /// <summary>Comando usado para ler o estado atual (opcional; guardado no backup como informação).</summary>
    public string? ReadArguments { get; init; }

    public string CurrentValueText { get; init; } = "—";
    public string NewValueText { get; init; } = "—";
    public OptimizationCategory Category { get; init; } = OptimizationCategory.Sistema;
    public OptimizationArea Area { get; init; } = OptimizationArea.DesempenhoSistema;
    public string Risk { get; init; } = "Baixo";
    public HealthLevel RiskLevel { get; init; } = HealthLevel.Otimo;
    public bool RequiresRestart { get; init; }
    public bool RequiresConfirmation { get; init; }
    public Func<bool>? AvailabilityPredicate { get; init; }
}

/// <summary>
/// Fábrica de <see cref="OptimizationAction"/>.
///
/// Toda a lógica de "guardar o original → escrever o novo → saber reverter" vive AQUI e em
/// nenhum outro sítio. Isso garante um comportamento uniforme em todos os módulos:
///   • simular (não escreve nada),
///   • aplicar (grava backup + escreve),
///   • reverter (repõe o backup exato, ou remove o que criámos).
/// </summary>
public sealed class TweakFactory
{
    private readonly IRegistryProvider _registry;
    private readonly IProcessRunner _runner;
    private readonly ILogger<TweakFactory> _logger;

    public TweakFactory(IRegistryProvider registry, IProcessRunner runner, ILogger<TweakFactory> logger)
    {
        _registry = registry;
        _runner = runner;
        _logger = logger;
    }

    /// <summary>Cria a ação correspondente a um tweak do registo (com disponibilidade verificada).</summary>
    public OptimizationAction Create(RegistryTweakSpec spec, bool simulationMode)
    {
        var available = spec.AvailabilityPredicate?.Invoke() ?? true;
        var current = _registry.GetValue(spec.Hive, spec.SubKey, spec.ValueName);
        var alreadyApplied = IsAlreadyApplied(current, spec.OptimizedValue, spec.Kind);

        return new OptimizationAction
        {
            Key = spec.Key,
            Title = spec.Title,
            Description = spec.Description,
            Target = $"{spec.Hive}\\{spec.SubKey}\\{spec.ValueName ?? "(Predefinido)"}",
            CurrentValue = current is null ? "não definido" : Describe(current, spec.Kind),
            NewValue = Describe(spec.OptimizedValue, spec.Kind),
            Category = spec.Category,
            Area = spec.Area,
            Risk = spec.Risk,
            RiskLevel = spec.RiskLevel,
            RequiresRestart = spec.RequiresRestart,
            RequiresConfirmation = spec.RequiresConfirmation,
            RequiresElevation = spec.RequiresElevation,
            IsAvailable = available,
            UnavailableReason = available ? null : "Não aplicável a esta versão do Windows.",
            IsApplied = alreadyApplied,

            IsAlreadyApplied = () => IsAlreadyApplied(_registry.GetValue(spec.Hive, spec.SubKey, spec.ValueName), spec.OptimizedValue, spec.Kind),

            Apply = action =>
            {
                if (simulationMode) return;

                var original = _registry.GetValue(spec.Hive, spec.SubKey, spec.ValueName, null);
                RecordRegistryBackup(action, spec, original);
                _registry.SetValue(spec.Hive, spec.SubKey, spec.ValueName, spec.OptimizedValue!, spec.Kind);
                _registry.ExportKey(spec.Hive, spec.SubKey, Path.Combine(AppPaths.Backups, spec.BackupFolder, $"{spec.Key}.reg"));
            },

            Revert = BuildRegistryRevert(spec),

            SimulateExplanation = () =>
                spec.SimulationNote
                ?? $"Seria escrito {spec.Hive}\\{spec.SubKey}\\{spec.ValueName} = {Describe(spec.OptimizedValue, spec.Kind)} " +
                   $"(valor atual: {Describe(_registry.GetValue(spec.Hive, spec.SubKey, spec.ValueName), spec.Kind)}).",
        };
    }

    /// <summary>Cria a ação para alterar o tipo de arranque de um serviço do Windows.</summary>
    public OptimizationAction Create(ServiceTweakSpec spec, bool simulationMode)
    {
        const string servicesRoot = @"SYSTEM\CurrentControlSet\Services";
        var subKey = $@"{servicesRoot}\{spec.ServiceName}";
        var available = spec.AvailabilityPredicate?.Invoke() ?? _registry.KeyExists("HKLM", subKey);
        var originalStart = _registry.GetValue("HKLM", subKey, "Start");
        var startText = DescribeStart(originalStart);

        return new OptimizationAction
        {
            Key = spec.Key,
            Title = spec.Title,
            Description = spec.Description,
            Target = $@"Serviço «{spec.ServiceName}» (HKLM\{subKey}\Start)",
            CurrentValue = startText,
            NewValue = DescribeStart(spec.OptimizedStart),
            Category = spec.Category,
            Area = spec.Area,
            Risk = spec.Risk,
            RiskLevel = spec.RiskLevel,
            RequiresRestart = false,
            IsAvailable = available,
            UnavailableReason = available ? null : "Serviço não encontrado neste computador.",
            IsApplied = originalStart is int start && start == spec.OptimizedStart,

            IsAlreadyApplied = () => _registry.GetValue("HKLM", subKey, "Start") is int value && value == spec.OptimizedStart,

            Apply = action =>
            {
                if (simulationMode) return;

                action.Backup[BackupKeys.ServiceName] = spec.ServiceName;
                action.Backup[BackupKeys.ServiceOriginalStart] = originalStart?.ToString();
                action.Backup[BackupKeys.ServiceOriginalStartType] = DescribeStart(originalStart);

                // Backup adicional do registo (para inspeção manual pelo utilizador).
                _registry.ExportKey("HKLM", subKey, Path.Combine(AppPaths.Backups, "servicos", $"{spec.ServiceName}.reg"));

                _registry.SetValue("HKLM", subKey, "Start", spec.OptimizedStart, RegistryValueKind.DWord);

                // "sc config" mantém o Gestor de Serviços coerente com o registo.
                _runner.Run("sc.exe", $"config \"{spec.ServiceName}\" start= {MapStartKeyword(spec.OptimizedStart)}");
            },

            Revert = action =>
            {
                if (!action.Backup.TryGetValue(BackupKeys.ServiceOriginalStart, out var original) || !int.TryParse(original, out var start))
                    return Result.Fail("Sem valor original guardado para este serviço — reversão manual necessária.");

                _registry.SetValue("HKLM", subKey, "Start", start, RegistryValueKind.DWord);
                _runner.Run("sc.exe", $"config \"{spec.ServiceName}\" start= {MapStartKeyword(start)}");
                return Result.Ok($"Serviço «{spec.ServiceName}» restaurado para {DescribeStart(start)}.");
            },

            SimulateExplanation = () =>
                $"O serviço «{spec.ServiceName}» passaria de «{DescribeStart(originalStart)}» para «{DescribeStart(spec.OptimizedStart)}». " +
                "O serviço continua disponível: será iniciado apenas quando for necessário.",
        };
    }

    /// <summary>Cria a ação para um comando externo (powercfg, netsh, bcdedit...).</summary>
    public OptimizationAction Create(CommandTweakSpec spec, bool simulationMode)
    {
        var available = spec.AvailabilityPredicate?.Invoke() ?? true;
        var currentText = spec.CurrentValueText;

        if (!string.IsNullOrWhiteSpace(spec.ReadArguments))
        {
            var read = _runner.Run(spec.FileName, spec.ReadArguments!);
            if (read.Success && !string.IsNullOrWhiteSpace(read.Output))
                currentText = Summarize(read.Output);
        }

        return new OptimizationAction
        {
            Key = spec.Key,
            Title = spec.Title,
            Description = spec.Description,
            Target = spec.Target,
            CurrentValue = currentText,
            NewValue = string.IsNullOrWhiteSpace(spec.NewValueText) ? spec.ApplyArguments : spec.NewValueText,
            Category = spec.Category,
            Area = spec.Area,
            Risk = spec.Risk,
            RiskLevel = spec.RiskLevel,
            RequiresRestart = spec.RequiresRestart,
            RequiresConfirmation = spec.RequiresConfirmation,
            IsAvailable = available,
            UnavailableReason = available ? null : "Recurso não disponível neste sistema.",

            Apply = action =>
            {
                if (simulationMode) return;

                if (!string.IsNullOrWhiteSpace(spec.ReadArguments))
                {
                    var before = _runner.Run(spec.FileName, spec.ReadArguments!);
                    action.Backup[BackupKeys.CommandOriginalOutput] = before.Output;
                }

                if (!string.IsNullOrWhiteSpace(spec.RevertArguments))
                    action.Backup[BackupKeys.CommandRevert] = $"{spec.FileName} {spec.RevertArguments}";

                var result = _runner.Run(spec.FileName, spec.ApplyArguments);
                if (!result.Success)
                    throw new InvalidOperationException($"«{spec.FileName} {spec.ApplyArguments}» falhou: {result.Output}");
            },

            Revert = action =>
            {
                if (!action.Backup.TryGetValue(BackupKeys.CommandRevert, out var revertCommand) || string.IsNullOrWhiteSpace(revertCommand))
                    return Result.Fail("Esta alteração não tem comando de reversão memorizado.");

                var parts = SplitCommand(revertCommand);
                var result = _runner.Run(parts.file, parts.args);

                return result.Success
                    ? Result.Ok($"Reversão executada: {revertCommand}")
                    : Result.Fail($"A reversão falhou ({result.Output}).", revertCommand);
            },

            SimulateExplanation = () =>
                $"Seria executado: «{spec.FileName} {spec.ApplyArguments}»\n" +
                $"Reversão disponível através de: «{spec.FileName} {spec.RevertArguments ?? "(sem reversão automática)"}»",
        };
    }

    /// <summary>
    /// Cria a ação para um valor de registo cujo conteúdo é texto/estrutura (ex.: telemetria,
    /// políticas de privacidade) — mesma mecânica, tipo diferente.
    /// </summary>
    public OptimizationAction CreateStringTweak(
        string key, string title, string description, string hive, string subKey, string? valueName,
        string optimizedValue, string risk, OptimizationCategory category, OptimizationArea area,
        bool simulationMode, bool requiresRestart = true, HealthLevel riskLevel = HealthLevel.Otimo)
        => Create(new RegistryTweakSpec
        {
            Key = key,
            Title = title,
            Description = description,
            Hive = hive,
            SubKey = subKey,
            ValueName = valueName,
            Kind = RegistryValueKind.String,
            OptimizedValue = optimizedValue,
            Category = category,
            Area = area,
            Risk = risk,
            RiskLevel = riskLevel,
            RequiresRestart = requiresRestart,
        }, simulationMode);

    // ------------------------------------------------------------------ internos

    /// <summary>
    /// Constrói a reversão de um tweak do registo.
    /// Repõe o valor exatamente como estava — ou, se o valor não existia, remove-o
    /// (nunca deixamos "lixo" de otimizações no registo do utilizador).
    /// </summary>
    private Func<OptimizationAction, Result> BuildRegistryRevert(RegistryTweakSpec spec) => action =>
    {
        if (!action.Backup.TryGetValue(BackupKeys.RegExisted, out var existedText))
            return Result.Fail("Sem backup para reverter esta alteração.");

        var hive = action.Backup.GetValueOrDefault(BackupKeys.RegHive) ?? spec.Hive;
        var subKey = action.Backup.GetValueOrDefault(BackupKeys.RegSubKey) ?? spec.SubKey;
        var rawName = action.Backup.GetValueOrDefault(BackupKeys.RegValueName);
        var valueName = string.IsNullOrEmpty(rawName) ? null : rawName;
        var kind = ParseKind(action.Backup.GetValueOrDefault(BackupKeys.RegKind));
        var existed = string.Equals(existedText, "true", StringComparison.OrdinalIgnoreCase);

        if (existed)
        {
            var raw = action.Backup.GetValueOrDefault(BackupKeys.RegOriginal);
            var value = ConvertForKind(raw ?? string.Empty, kind);
            var ok = _registry.SetValue(hive, subKey, valueName, value, kind);

            return ok
                ? Result.Ok($"Valor original restaurado em {hive}\\{subKey}\\{valueName ?? "(Predefinido)"}.")
                : Result.Fail("Não foi possível restaurar o valor original (verifique os privilégios de administrador).");
        }

        var deleted = _registry.DeleteValue(hive, subKey, valueName);
        return deleted
            ? Result.Ok($"Valor «{valueName ?? "(Predefinido)"}» removido — não existia antes da otimização.")
            : Result.Fail("Não foi possível remover o valor criado pela otimização.");
    };

    private static void RecordRegistryBackup(OptimizationAction action, RegistryTweakSpec spec, object? original)
    {
        action.Backup[BackupKeys.RegHive] = spec.Hive;
        action.Backup[BackupKeys.RegSubKey] = spec.SubKey;
        action.Backup[BackupKeys.RegValueName] = spec.ValueName ?? string.Empty;
        action.Backup[BackupKeys.RegKind] = spec.Kind.ToString();
        action.Backup[BackupKeys.RegExisted] = (original is not null).ToString().ToLowerInvariant();
        action.Backup[BackupKeys.RegOriginal] = original?.ToString();
    }

    private static bool IsAlreadyApplied(object? current, object? optimized, RegistryValueKind kind)
    {
        if (current is null || optimized is null) return false;

        try
        {
            if (kind is RegistryValueKind.DWord or RegistryValueKind.QWord)
                return Convert.ToInt64(current) == Convert.ToInt64(optimized);

            if (kind is RegistryValueKind.String or RegistryValueKind.ExpandString)
                return string.Equals(current.ToString(), optimized.ToString(), StringComparison.OrdinalIgnoreCase);

            return current.ToString() == optimized.ToString();
        }
        catch
        {
            return false;
        }
    }

    private static string Describe(object? value, RegistryValueKind kind)
    {
        if (value is null) return "não definido";

        if (kind == RegistryValueKind.DWord)
        {
            var number = Convert.ToInt64(value);
            return number switch { 0 => "0 (desativado)", 1 => "1 (ativado)", 2 => "2", _ => number.ToString() };
        }

        return value.ToString() ?? "—";
    }

    private static string DescribeStart(object? value) => value switch
    {
        int i => DescribeStart(i),
        string s when int.TryParse(s, out var parsed) => DescribeStart(parsed),
        _ => "desconhecido",
    };

    private static string DescribeStart(int start) => start switch
    {
        0 => "0 — Driver de arranque",
        1 => "1 — Driver de sistema",
        2 => "2 — Automático",
        3 => "3 — Manual",
        4 => "4 — Desativado",
        _ => start.ToString(),
    };

    /// <summary>Converte o valor numérico de <c>Start</c> na palavra-chave aceite por <c>sc config</c>.</summary>
    private static string MapStartKeyword(int start) => start switch
    {
        2 => "auto",
        3 => "demand",
        4 => "disabled",
        _ => "demand",
    };

    private static RegistryValueKind ParseKind(string? kind)
        => Enum.TryParse<RegistryValueKind>(kind, ignoreCase: true, out var parsed) ? parsed : RegistryValueKind.DWord;

    private static object ConvertForKind(string raw, RegistryValueKind kind) => kind switch
    {
        RegistryValueKind.DWord => int.TryParse(raw, out var i) ? i : 0,
        RegistryValueKind.QWord => long.TryParse(raw, out var l) ? l : 0L,
        _ => raw,
    };

    private static string Summarize(string output, int maxLength = 160)
    {
        var firstLine = output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim())
            .FirstOrDefault(l => l.Length > 0) ?? "—";

        return firstLine.Length > maxLength ? firstLine[..maxLength] + "…" : firstLine;
    }

    private static (string file, string args) SplitCommand(string command)
    {
        var index = command.IndexOf(' ');
        return index < 0 ? (command, string.Empty) : (command[..index], command[(index + 1)..]);
    }
}
