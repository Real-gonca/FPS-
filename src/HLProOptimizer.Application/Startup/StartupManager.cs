using HLProOptimizer.Core.Abstractions;
using HLProOptimizer.Core.Enums;
using HLProOptimizer.Core.Models;
using Microsoft.Extensions.Logging;

namespace HLProOptimizer.Application.Startup;

/// <summary>
/// Gerenciador de programas de inicialização.
/// </summary>
/// <remarks>
/// Fontes enumeradas:
/// <list type="bullet">
///   <item><description><c>HKCU/HKLM\...\CurrentVersion\Run</c> e <c>RunOnce</c> (incluindo WOW6432Node).</description></item>
///   <item><description>Pasta "Inicializar" do usuário e a pasta comum (All Users).</description></item>
///   <item><description>Tarefas agendadas com gatilho de logon/boot.</description></item>
/// </list>
/// O estado ativo/inativo é lido e gravado em
/// <c>...\Explorer\StartupApproved\*</c> (REG_BINARY), exatamente como o
/// Gerenciador de Tarefas do Windows faz - assim as alterações são refletidas
/// imediatamente na UI nativa do sistema.
/// </remarks>
public sealed class StartupManager : IStartupManager
{
    private const string RunKeyCurrentUser = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunKeyLocalMachine = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string RunKeyLocalMachineWow = @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run";
    private const string RunOnceKeyCurrentUser = @"Software\Microsoft\Windows\CurrentVersion\RunOnce";
    private const string RunOnceKeyLocalMachine = @"SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce";

    private const string StartupApprovedUserRun = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    private const string StartupApprovedUserFolder = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\StartupFolder";
    private const string StartupApprovedMachineRun = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    private const string StartupApprovedMachineFolder = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\StartupFolder";

    /// <summary>Tempo estimado (s) por nível de impacto.</summary>
    private static readonly Dictionary<StartupImpact, double> ImpactSeconds = new()
    {
        [StartupImpact.High] = 3.0d,
        [StartupImpact.Medium] = 1.4d,
        [StartupImpact.Low] = 0.4d,
        [StartupImpact.Unknown] = 0.8d
    };

    /// <summary>Processos reconhecidamente pesados no boot.</summary>
    private static readonly IReadOnlySet<string> HeavyNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "AdobeGCInvoker", "Adobe Desktop Service", "Creative Cloud", "CCLibrary",
        "Steam", "steamclient", "EpicGamesLauncher", "EpicWebHelper", "RiotClientServices",
        "Battle.net", "Discord", "DiscordPTB", "Slack", "Teams", "MicrosoftTeams",
        "Spotify", "SpotifyWebHelper", "OneDrive", "iCloudServices", "Dropbox",
        "GoogleDriveFS", "Sync", "Skype", "Zoom", "AnyDesk", "TeamViewer",
        "CCleaner", "CCleaner64", "AvastUI", "AvastAvUI", "AVGUI", "avgui",
        "OfficeClickToRun", "Lync", "Outlook", "JavaUpdate", "jusched", "SunJavaUpdateSched",
        "Updater", "AutoUpdater", "NVIDIA Backend", "ShadowPlay", "nvcontainer"
    };

    /// <summary>Processos leves (baixo impacto).</summary>
    private static readonly IReadOnlySet<string> LightNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "ctfmon", "SecurityHealth", "RtkAudUService", "RTHDVCPL", "RzSynapse",
        "LogiOptionsPlus", "SetPoint", "KHALMNPR", "MouseKeyboardCenter", "igfxTray"
    };

    /// <summary>
    /// Publicadores/nomes essenciais (drivers, áudio, segurança). Nunca são
    /// desativados automaticamente.
    /// </summary>
    private static readonly IReadOnlySet<string> EssentialKeywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "intel", "nvidia", "amd", "ati", "realtek", "synaptics", "elan", "logitech",
        "defender", "securityhealth", "antivirus", "audio", "sound", "touchpad",
        "graphics", "chipset", "driver", "razer", "corsair", "steelseries", "asus",
        "msi", "gigabyte", "lenovo", "dell", "hp", "acer", "microsoft edge update"
    };

    private readonly IRegistryService _registry;
    private readonly IScheduledTaskService _scheduledTasks;
    private readonly IFileMetadataService _fileMetadata;
    private readonly IActionHistoryService _history;
    private readonly ILogger<StartupManager> _logger;

    /// <summary>Cria o gerenciador de inicialização.</summary>
    public StartupManager(
        IRegistryService registry,
        IScheduledTaskService scheduledTasks,
        IFileMetadataService fileMetadata,
        IActionHistoryService history,
        ILogger<StartupManager> logger)
    {
        _registry = registry;
        _scheduledTasks = scheduledTasks;
        _fileMetadata = fileMetadata;
        _history = history;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<StartupProgram>> GetStartupProgramsAsync(CancellationToken cancellationToken = default)
    {
        var programs = new List<StartupProgram>();

        try
        {
            programs.AddRange(EnumerateRegistry(RegistryHiveKind.CurrentUser, RunKeyCurrentUser, StartupLocation.RegistryCurrentUser, StartupApprovedUserRun));
            programs.AddRange(EnumerateRegistry(RegistryHiveKind.LocalMachine, RunKeyLocalMachine, StartupLocation.RegistryLocalMachine, StartupApprovedMachineRun));
            programs.AddRange(EnumerateRegistry(RegistryHiveKind.LocalMachine, RunKeyLocalMachineWow, StartupLocation.RegistryLocalMachine, StartupApprovedMachineRun));
            programs.AddRange(EnumerateRegistry(RegistryHiveKind.CurrentUser, RunOnceKeyCurrentUser, StartupLocation.RegistryCurrentUserOnce, StartupApprovedUserRun));
            programs.AddRange(EnumerateRegistry(RegistryHiveKind.LocalMachine, RunOnceKeyLocalMachine, StartupLocation.RegistryLocalMachineOnce, StartupApprovedMachineRun));
            programs.AddRange(EnumerateStartupFolder(Environment.SpecialFolder.Startup, StartupLocation.StartupFolderUser, StartupApprovedUserFolder));
            programs.AddRange(EnumerateStartupFolder(Environment.SpecialFolder.CommonStartup, StartupLocation.StartupFolderCommon, StartupApprovedMachineFolder));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Falha ao enumerar entradas de inicialização do registro/pastas.");
        }

        try
        {
            var tasks = await _scheduledTasks.GetStartupTasksAsync(cancellationToken).ConfigureAwait(false);

            programs.AddRange(tasks.Select(FromScheduledTask));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Não foi possível enumerar as tarefas agendadas de inicialização.");
        }

        var result = programs
            .GroupBy(p => p.Id, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderByDescending(p => p.Impact)
            .ThenBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        _logger.LogInformation("{Count} programa(s) de inicialização enumerado(s).", result.Count);

        return result;
    }

    /// <inheritdoc />
    public async Task<bool> SetEnabledAsync(StartupProgram program, bool enabled, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(program);

        try
        {
            var success = program.Location switch
            {
                StartupLocation.TaskScheduler =>
                    await _scheduledTasks.SetEnabledAsync(program.ResolvedLocationPath ?? program.Command, enabled, cancellationToken).ConfigureAwait(false),

                StartupLocation.StartupFolderUser =>
                    SetFolderState(StartupApprovedUserFolder, RegistryHiveKind.CurrentUser, program.Name, enabled),

                StartupLocation.StartupFolderCommon =>
                    SetFolderState(StartupApprovedMachineFolder, RegistryHiveKind.LocalMachine, program.Name, enabled),

                StartupLocation.RegistryCurrentUser or StartupLocation.RegistryCurrentUserOnce =>
                    SetRunState(StartupApprovedUserRun, RegistryHiveKind.CurrentUser, program.Name, enabled),

                StartupLocation.RegistryLocalMachine or StartupLocation.RegistryLocalMachineOnce =>
                    SetRunState(StartupApprovedMachineRun, RegistryHiveKind.LocalMachine, program.Name, enabled),

                _ => false
            };

            if (success)
            {
                await _history.RecordAsync(
                    ActionKind.Startup,
                    enabled ? "Item de inicialização ativado" : "Item de inicialização desativado",
                    $"{program.Name} ({program.Location})",
                    success: true,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
            }

            return success;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Falha ao alterar o estado de '{Program}'.", program.Name);
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<bool> RemoveAsync(StartupProgram program, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(program);

        try
        {
            var success = program.Location switch
            {
                StartupLocation.RegistryCurrentUser =>
                    _registry.DeleteValue(RegistryHiveKind.CurrentUser, RunKeyCurrentUser, program.Name),

                StartupLocation.RegistryCurrentUserOnce =>
                    _registry.DeleteValue(RegistryHiveKind.CurrentUser, RunOnceKeyCurrentUser, program.Name),

                StartupLocation.RegistryLocalMachine =>
                    _registry.DeleteValue(RegistryHiveKind.LocalMachine, RunKeyLocalMachine, program.Name) ||
                    _registry.DeleteValue(RegistryHiveKind.LocalMachine, RunKeyLocalMachineWow, program.Name),

                StartupLocation.RegistryLocalMachineOnce =>
                    _registry.DeleteValue(RegistryHiveKind.LocalMachine, RunOnceKeyLocalMachine, program.Name),

                StartupLocation.StartupFolderUser =>
                    DeleteStartupFile(Environment.SpecialFolder.Startup, program.Name),

                StartupLocation.StartupFolderCommon =>
                    DeleteStartupFile(Environment.SpecialFolder.CommonStartup, program.Name),

                StartupLocation.TaskScheduler =>
                    await _scheduledTasks.SetEnabledAsync(program.ResolvedLocationPath ?? program.Command, false, cancellationToken).ConfigureAwait(false),

                _ => false
            };

            if (success)
            {
                await _history.RecordAsync(
                    ActionKind.Startup,
                    "Item de inicialização removido",
                    $"{program.Name} ({program.Location})",
                    success: true,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
            }

            return success;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Falha ao remover '{Program}'.", program.Name);
            return false;
        }
    }

    /// <inheritdoc />
    public StartupSummary Summarize(IReadOnlyList<StartupProgram> programs)
    {
        ArgumentNullException.ThrowIfNull(programs);

        var enabled = programs.Where(p => p.IsEnabled).ToList();
        var currentBoot = enabled.Sum(p => p.EstimatedSeconds);

        // Cenário otimizado: apenas os itens essenciais permanecem ativos.
        var optimizedBoot = enabled.Where(p => !p.IsSafeToDisable).Sum(p => p.EstimatedSeconds);

        return new StartupSummary(
            TotalPrograms: programs.Count,
            EnabledCount: enabled.Count,
            DisabledCount: programs.Count - enabled.Count,
            HighImpactCount: enabled.Count(p => p.Impact == StartupImpact.High),
            OrphanedCount: programs.Count(p => !p.FileExists),
            CurrentBootSeconds: Math.Round(currentBoot, 1),
            OptimizedBootSeconds: Math.Round(optimizedBoot, 1));
    }

    /// <inheritdoc />
    public string BuildSearchUrl(StartupProgram program)
    {
        ArgumentNullException.ThrowIfNull(program);

        var fileName = Path.GetFileName(program.ExecutablePath ?? program.Name);
        var query = $"{fileName} {program.Publisher}".Trim();

        return $"https://www.google.com/search?q={Uri.EscapeDataString(query)}";
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> DisableNonEssentialAsync(
        IReadOnlyList<StartupProgram> programs,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(programs);

        var candidates = programs
            .Where(p => p.IsEnabled && p.IsSafeToDisable && p.Impact is StartupImpact.High or StartupImpact.Medium)
            .ToList();

        var disabled = new List<string>();

        foreach (var program in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (await SetEnabledAsync(program, false, cancellationToken).ConfigureAwait(false))
            {
                disabled.Add(program.Name);
            }
        }

        _logger.LogInformation("Inicialização otimizada: {Count} item(ns) desativado(s).", disabled.Count);

        return disabled;
    }

    /// <summary>Enumera uma chave Run/RunOnce e converte os valores em programas.</summary>
    private IEnumerable<StartupProgram> EnumerateRegistry(
        RegistryHiveKind hive,
        string keyPath,
        StartupLocation location,
        string approvedKeyPath)
    {
        if (!_registry.KeyExists(hive, keyPath))
        {
            yield break;
        }

        string[] valueNames;

        try
        {
            valueNames = [.. _registry.GetValueNames(hive, keyPath)];
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Não foi possível ler os valores de {Hive}\\{Key}.", hive, keyPath);
            yield break;
        }

        foreach (var valueName in valueNames)
        {
            var command = _registry.GetString(hive, keyPath, valueName);

            if (string.IsNullOrWhiteSpace(command))
            {
                continue;
            }

            var executablePath = _fileMetadata.ResolveExecutablePath(command);
            var metadata = executablePath is null ? null : SafeGetMetadata(executablePath);
            var exists = executablePath is not null && File.Exists(executablePath);
            var isEnabled = IsEnabledFromApproved(SafeGetBinary(hive, approvedKeyPath, valueName));

            yield return Build(
                name: valueName,
                command: command,
                executablePath: executablePath,
                publisher: metadata?.Publisher ?? "Desconhecido",
                location: location,
                isEnabled: isEnabled,
                fileExists: exists,
                resolvedLocationPath: keyPath);
        }
    }

    /// <summary>Enumera a pasta Inicializar (atalhos .lnk e executáveis).</summary>
    private IEnumerable<StartupProgram> EnumerateStartupFolder(
        Environment.SpecialFolder folderKind,
        StartupLocation location,
        string approvedKeyPath)
    {
        string folderPath;

        try
        {
            folderPath = Environment.GetFolderPath(folderKind);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Não foi possível resolver a pasta de inicialização {Folder}.", folderKind);
            yield break;
        }

        if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
        {
            yield break;
        }

        string[] files;

        try
        {
            files = Directory.GetFiles(folderPath)
                .Where(f => !Path.GetFileName(f).StartsWith('.', StringComparison.Ordinal))
                .ToArray();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Falha ao listar a pasta de inicialização {Folder}.", folderPath);
            yield break;
        }

        var hive = location == StartupLocation.StartupFolderCommon
            ? RegistryHiveKind.LocalMachine
            : RegistryHiveKind.CurrentUser;

        foreach (var file in files)
        {
            var name = Path.GetFileNameWithoutExtension(file);
            var metadata = SafeGetMetadata(file);
            var isEnabled = IsEnabledFromApproved(SafeGetBinary(hive, approvedKeyPath, Path.GetFileName(file)));

            yield return Build(
                name: name,
                command: file,
                executablePath: string.Equals(Path.GetExtension(file), ".lnk", StringComparison.OrdinalIgnoreCase)
                    ? _fileMetadata.ResolveExecutablePath(file)
                    : file,
                publisher: metadata?.Publisher ?? "Desconhecido",
                location: location,
                isEnabled: isEnabled,
                fileExists: File.Exists(file),
                resolvedLocationPath: file);
        }
    }

    /// <summary>Converte uma tarefa agendada em um programa de inicialização.</summary>
    private StartupProgram FromScheduledTask(ScheduledTaskInfo task)
    {
        var executablePath = task.ExecutablePath;
        var metadata = executablePath is null ? null : SafeGetMetadata(executablePath);

        return Build(
            name: task.Name,
            command: string.IsNullOrWhiteSpace(task.Arguments)
                ? executablePath ?? task.TaskPath
                : $"\"{executablePath}\" {task.Arguments}",
            executablePath: executablePath,
            publisher: metadata?.Publisher ?? (string.IsNullOrWhiteSpace(task.Author) ? "Desconhecido" : task.Author),
            location: StartupLocation.TaskScheduler,
            isEnabled: task.IsEnabled,
            fileExists: executablePath is null || File.Exists(executablePath),
            resolvedLocationPath: task.TaskPath);
    }

    /// <summary>Monta um <see cref="StartupProgram"/> aplicando as heurísticas de impacto.</summary>
    private StartupProgram Build(
        string name,
        string command,
        string? executablePath,
        string publisher,
        StartupLocation location,
        bool isEnabled,
        bool fileExists,
        string? resolvedLocationPath)
    {
        var impact = EvaluateImpact(name, executablePath, publisher);
        var isEssential = IsEssential(name, executablePath, publisher);

        return new StartupProgram
        {
            Id = $"{location}:{name}".GetHashCode(StringComparison.OrdinalIgnoreCase).ToString("X8") + "-" + Sanitize(name),
            Name = name,
            Command = command,
            ExecutablePath = executablePath,
            Arguments = ExtractArguments(command, executablePath),
            Publisher = publisher,
            Location = location,
            Impact = impact,
            EstimatedSeconds = ImpactSeconds.TryGetValue(impact, out var seconds) ? seconds : ImpactSeconds[StartupImpact.Unknown],
            IsEnabled = isEnabled,
            FileExists = fileExists,
            IsSafeToDisable = !isEssential && fileExists,
            ResolvedLocationPath = resolvedLocationPath
        };
    }

    /// <summary>Heurística de impacto no boot.</summary>
    private static StartupImpact EvaluateImpact(string name, string? executablePath, string publisher)
    {
        var fileName = executablePath is null ? name : Path.GetFileNameWithoutExtension(executablePath);

        if (HeavyNames.Contains(name) || HeavyNames.Contains(fileName))
        {
            return StartupImpact.High;
        }

        if (LightNames.Contains(name) || LightNames.Contains(fileName))
        {
            return StartupImpact.Low;
        }

        // Binários grandes tendem a custar mais I/O no boot.
        try
        {
            if (executablePath is not null && File.Exists(executablePath))
            {
                var size = new FileInfo(executablePath).Length;

                if (size > 60L * 1024 * 1024)
                {
                    return StartupImpact.High;
                }

                if (size > 15L * 1024 * 1024)
                {
                    return StartupImpact.Medium;
                }
            }
        }
        catch (Exception)
        {
            // Falha ao medir o arquivo não deve impedir a listagem.
        }

        // Updaters/sync tools são tipicamente de impacto médio.
        if (name.Contains("Update", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Sync", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Helper", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Launcher", StringComparison.OrdinalIgnoreCase))
        {
            return StartupImpact.Medium;
        }

        if (EssentialKeywords.Any(k => publisher.Contains(k, StringComparison.OrdinalIgnoreCase)))
        {
            return StartupImpact.Low;
        }

        return StartupImpact.Unknown;
    }

    /// <summary>Identifica itens essenciais (drivers, áudio, segurança) que não devem ser desativados.</summary>
    private static bool IsEssential(string name, string? executablePath, string publisher)
    {
        var fileName = executablePath is null ? name : Path.GetFileNameWithoutExtension(executablePath);
        var haystack = $"{name} {fileName} {publisher}";

        return EssentialKeywords.Any(keyword => haystack.Contains(keyword, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Extrai os argumentos de um comando, removendo o executável.</summary>
    private static string ExtractArguments(string command, string? executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            return string.Empty;
        }

        var index = command.IndexOf(executablePath, StringComparison.OrdinalIgnoreCase);

        if (index < 0)
        {
            return string.Empty;
        }

        return command[(index + executablePath.Length)..].Trim().Trim('"').Trim();
    }

    /// <summary>Interpreta o blob REG_BINARY do StartupApproved.</summary>
    private static bool IsEnabledFromApproved(byte[]? value)
        => value is null || value.Length == 0 || value[0] == 0x02;

    /// <summary>Grava o estado ativo/inativo de um item Run.</summary>
    private bool SetRunState(string approvedKeyPath, RegistryHiveKind hive, string name, bool enabled)
    {
        _registry.SetBinary(hive, approvedKeyPath, name, BuildApprovedValue(enabled));
        return true;
    }

    /// <summary>Grava o estado ativo/inativo de um item da pasta Inicializar.</summary>
    private bool SetFolderState(string approvedKeyPath, RegistryHiveKind hive, string name, bool enabled)
    {
        // Itens de pasta são armazenados com a extensão original (.lnk/.exe).
        var valueName = Path.HasExtension(name) ? name : name + ".lnk";
        _registry.SetBinary(hive, approvedKeyPath, valueName, BuildApprovedValue(enabled));
        return true;
    }

    /// <summary>Constrói o blob de 12 bytes usado pelo Explorer.</summary>
    private static byte[] BuildApprovedValue(bool enabled)
    {
        var value = new byte[12];
        value[0] = enabled ? (byte)0x02 : (byte)0x03;

        if (!enabled)
        {
            // Bytes 4-11: FILETIME da desativação (o Explorer exibe a data na UI).
            BitConverter.GetBytes(DateTime.Now.ToFileTimeUtc()).CopyTo(value, 4);
        }

        return value;
    }

    /// <summary>Remove um atalho/arquivo da pasta Inicializar.</summary>
    private bool DeleteStartupFile(Environment.SpecialFolder folderKind, string name)
    {
        var folder = Environment.GetFolderPath(folderKind);

        if (string.IsNullOrWhiteSpace(folder))
        {
            return false;
        }

        var candidates = new[] { name, name + ".lnk", name + ".exe", name + ".url" };

        foreach (var candidate in candidates)
        {
            var path = Path.Combine(folder, candidate);

            if (File.Exists(path))
            {
                File.Delete(path);
                return true;
            }
        }

        return false;
    }

    /// <summary>Lê metadados de arquivo sem propagar exceções.</summary>
    private FileMetadata? SafeGetMetadata(string path)
    {
        try
        {
            return _fileMetadata.GetMetadata(path);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Não foi possível ler metadados de {Path}.", path);
            return null;
        }
    }

    /// <summary>Lê um valor binário sem propagar exceções.</summary>
    private byte[]? SafeGetBinary(RegistryHiveKind hive, string keyPath, string valueName)
    {
        try
        {
            return _registry.GetBinary(hive, keyPath, valueName);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Não foi possível ler {Hive}\\{Key}\\{Value}.", hive, keyPath, valueName);
            return null;
        }
    }

    /// <summary>Remove caracteres inválidos de um nome para composição de Id.</summary>
    private static string Sanitize(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = value.Select(c => invalid.Contains(c) ? '_' : c).ToArray();

        return new string(chars);
    }
}
