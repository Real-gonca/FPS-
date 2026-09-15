using System.Globalization;
using System.Management;
using System.Text;
using HLProOptimizer.Core.Abstractions;
using HLProOptimizer.Core.Enums;
using HLProOptimizer.Core.Models;
using HLProOptimizer.Infrastructure.Interop;
using HLProOptimizer.Infrastructure.Platform;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace HLProOptimizer.Infrastructure.Backup;

/// <summary>
/// Backup e restauração: chaves de registro, arquivo hosts, configurações do
/// aplicativo, lista de inicialização e pontos de restauração do Windows.
/// </summary>
/// <remarks>
/// <para>
/// <b>Pontos de restauração</b> usam a classe WMI <c>SystemRestore</c>
/// (<c>root\default</c>) quando o processo é administrador; caso contrário,
/// delegamos ao PowerShell <c>Checkpoint-Computer</c> executado de forma elevada
/// (um único prompt de UAC). O Windows limita a criação a um ponto a cada 24 h —
/// quando isso acontece, o erro é registrado e o método devolve <c>null</c> sem
/// derrubar o fluxo de otimização.
/// </para>
/// <para>
/// <b>Backup completo</b> cria uma pasta datada em <see cref="ISystemPaths.BackupDirectory"/>
/// com um manifesto JSON, permitindo auditoria e restauração seletiva.
/// </para>
/// </remarks>
public sealed class BackupService : IBackupService
{
    private const string BackupFolderPrefix = "backup-";

    /// <summary>Tipo de ponto de restauração: alteração de configurações.</summary>
    private const uint RestorePointTypeModifySettings = 3;

    /// <summary>Evento: início de mudança no sistema.</summary>
    private const uint EventTypeBeginSystemChange = 100;

    private readonly IRegistryService _registry;
    private readonly ISettingsService _settings;
    private readonly ISystemPaths _paths;
    private readonly ICommandRunner _commands;
    private readonly ElevatedScriptRunner _runner;
    private readonly IStartupManager _startupManager;
    private readonly ILogger<BackupService> _logger;

    /// <summary>Cria o serviço de backup.</summary>
    /// <param name="registry">Registro do Windows.</param>
    /// <param name="settings">Configurações do aplicativo.</param>
    /// <param name="paths">Caminhos do aplicativo.</param>
    /// <param name="commands">Executor de comandos.</param>
    /// <param name="runner">Executor elevado com streaming.</param>
    /// <param name="startupManager">Lista de inicialização (exportada no backup completo).</param>
    /// <param name="logger">Logger.</param>
    public BackupService(
        IRegistryService registry,
        ISettingsService settings,
        ISystemPaths paths,
        ICommandRunner commands,
        ElevatedScriptRunner runner,
        IStartupManager startupManager,
        ILogger<BackupService> logger)
    {
        _registry = registry;
        _settings = settings;
        _paths = paths;
        _commands = commands;
        _runner = runner;
        _startupManager = startupManager;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<string> BackupRegistryAsync(
        string keyPath,
        string? destinationFile = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyPath);

        var (hive, relativePath) = SplitHive(keyPath);

        var target = string.IsNullOrWhiteSpace(destinationFile)
            ? BuildBackupPath($"registry-{SanitizeFileName(relativePath)}.reg")
            : destinationFile;

        _logger.LogInformation("Exportando a chave {Hive}\\{Key} para {Target}.", hive, relativePath, target);

        var ok = await _registry.ExportKeyAsync(hive, relativePath, target, cancellationToken).ConfigureAwait(false);

        return ok ? target : string.Empty;
    }

    /// <inheritdoc />
    public async Task<bool> RestoreRegistryAsync(string regFile, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(regFile);

        if (!File.Exists(regFile))
        {
            _logger.LogWarning("Arquivo .reg não encontrado para restauração: {File}.", regFile);
            return false;
        }

        _logger.LogInformation("Restaurando o arquivo de registro {File}.", regFile);

        return await _registry.ImportKeyAsync(regFile, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<RestorePointInfo?> CreateRestorePointAsync(string description, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(description);

        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        _logger.LogInformation("Criando o ponto de restauração '{Description}'.", description);

        if (ElevationHelper.IsProcessElevated())
        {
            var viaWmi = TryCreateRestorePointViaWmi(description);

            if (viaWmi is not null)
            {
                return viaWmi;
            }
        }

        // Sem elevação: PowerShell elevado (um único prompt de UAC).
        var escaped = description.Replace("'", "''");

        var result = await _runner.RunAsync(
            "powershell.exe",
            $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"Checkpoint-Computer -Description '{escaped}' -RestorePointType 'MODIFY_SETTINGS'\"",
            null,
            TimeSpan.FromMinutes(5),
            cancellationToken).ConfigureAwait(false);

        if (!result.IsSuccess)
        {
            _logger.LogWarning(
                "Não foi possível criar o ponto de restauração (exit {ExitCode}): {Output}",
                result.ExitCode,
                result.CombinedOutput);

            return null;
        }

        // O WMI não devolve o SequenceNumber no caminho elevado: procuramos o mais recente.
        var points = await GetRestorePointsAsync(cancellationToken).ConfigureAwait(false);

        return points
            .Where(p => string.Equals(p.Description, description, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(p => p.CreationTime)
            .FirstOrDefault()
            ?? points.OrderByDescending(p => p.CreationTime).FirstOrDefault();
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<RestorePointInfo>> GetRestorePointsAsync(CancellationToken cancellationToken = default)
    {
        return Task.Run<IReadOnlyList<RestorePointInfo>>(() =>
        {
            if (!OperatingSystem.IsWindows())
            {
                return [];
            }

            var points = new List<RestorePointInfo>();

            try
            {
                using var searcher = new ManagementObjectSearcher(
                    new ManagementScope(@"root\default"),
                    new ObjectQuery("SELECT SequenceNumber, Description, CreationTime, RestorePointType FROM SystemRestore"));

                foreach (var managementObject in searcher.Get())
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    using (managementObject)
                    {
                        points.Add(new RestorePointInfo(
                            SequenceNumber: Convert.ToInt64(managementObject["SequenceNumber"], CultureInfo.InvariantCulture),
                            Description: managementObject["Description"]?.ToString() ?? string.Empty,
                            CreationTime: ParseWmiDate(managementObject["CreationTime"]?.ToString()),
                            RestorePointType: TranslateRestorePointType(managementObject["RestorePointType"])));
                    }
                }
            }
            catch (UnauthorizedAccessException ex)
            {
                // A leitura de SystemRestore exige administrador.
                _logger.LogWarning(ex, "Sem privilégios para listar os pontos de restauração.");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Falha ao listar os pontos de restauração via WMI.");
            }

            _logger.LogDebug("{Count} ponto(s) de restauração encontrado(s).", points.Count);

            return points.OrderByDescending(p => p.CreationTime).ToList();
        }, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> RestoreToPointAsync(long sequenceNumber, CancellationToken cancellationToken = default)
    {
        if (sequenceNumber <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sequenceNumber), "O número sequencial deve ser positivo.");
        }

        _logger.LogInformation("Iniciando a restauração para o ponto {SequenceNumber}.", sequenceNumber);

        try
        {
            // rstrui abre o assistente de restauração na etapa de confirmação.
            var result = await _commands
                .RunAsync(
                    Path.Combine(Environment.SystemDirectory, "rstrui.exe"),
                    $"/restorepoint:{sequenceNumber}",
                    cancellationToken,
                    elevated: !ElevationHelper.IsProcessElevated(),
                    timeout: TimeSpan.FromSeconds(30))
                .ConfigureAwait(false);

            if (result.ExitCode == 1223)
            {
                _logger.LogWarning("Restauração cancelada: elevação recusada pelo usuário.");
                return false;
            }

            return result.IsSuccess;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha ao iniciar a restauração para o ponto {SequenceNumber}.", sequenceNumber);
            return false;
        }
    }

    /// <inheritdoc />
    public Task<string> ExportSettingsAsync(string? destinationFile = null, CancellationToken cancellationToken = default)
    {
        var target = string.IsNullOrWhiteSpace(destinationFile)
            ? BuildBackupPath($"settings-{DateTime.Now:yyyyMMdd-HHmmss}.json")
            : destinationFile;

        return Task.Run(() =>
        {
            try
            {
                var directory = Path.GetDirectoryName(target);

                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var json = JsonConvert.SerializeObject(_settings.Current, Formatting.Indented);

                File.WriteAllText(target, json, Encoding.UTF8);

                _logger.LogInformation("Configurações exportadas para {Target}.", target);

                return target;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Falha ao exportar as configurações para {Target}.", target);
                return string.Empty;
            }
        }, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<AppSettings?> ImportSettingsAsync(string sourceFile, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceFile);

        if (!File.Exists(sourceFile))
        {
            _logger.LogWarning("Arquivo de configurações não encontrado: {File}.", sourceFile);
            return null;
        }

        try
        {
            var json = await File.ReadAllTextAsync(sourceFile, Encoding.UTF8, cancellationToken).ConfigureAwait(false);
            var imported = JsonConvert.DeserializeObject<AppSettings>(json);

            if (imported is null)
            {
                _logger.LogWarning("O arquivo {File} não contém configurações válidas.", sourceFile);
                return null;
            }

            // Sanitização mínima: valores fora de faixa voltam ao padrão.
            imported.MonitoringIntervalSeconds = Math.Clamp(imported.MonitoringIntervalSeconds, 1, 60);
            imported.MonitoringHistorySeconds = Math.Clamp(imported.MonitoringHistorySeconds, 10, 600);

            await _settings.SaveAsync(imported, cancellationToken).ConfigureAwait(false);

            _logger.LogInformation("Configurações importadas de {File}.", sourceFile);

            return imported;
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "JSON inválido em {File}.", sourceFile);
            return null;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha ao importar as configurações de {File}.", sourceFile);
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<string> CreateFullBackupAsync(CancellationToken cancellationToken = default)
    {
        var folder = Path.Combine(_paths.BackupDirectory, $"{BackupFolderPrefix}{DateTime.Now:yyyyMMdd-HHmmss}");
        var manifest = new List<string>();

        try
        {
            Directory.CreateDirectory(folder);

            _logger.LogInformation("Criando backup completo em {Folder}.", folder);

            // 1) Chaves de inicialização (as mais alteradas pelos otimizados).
            foreach (var (hive, key, fileName) in new[]
                     {
                         (RegistryHiveKind.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", "registry-hklm-run.reg"),
                         (RegistryHiveKind.CurrentUser, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", "registry-hkcu-run.reg"),
                         (RegistryHiveKind.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System", "registry-uac-policies.reg")
                     })
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    if (await _registry.ExportKeyAsync(hive, key, Path.Combine(folder, fileName), cancellationToken).ConfigureAwait(false))
                    {
                        manifest.Add(fileName);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Falha ao exportar {Key}.", key);
                }
            }

            // 2) Arquivo hosts.
            try
            {
                if (File.Exists(_paths.HostsFilePath))
                {
                    File.Copy(_paths.HostsFilePath, Path.Combine(folder, "hosts.txt"), overwrite: true);
                    manifest.Add("hosts.txt");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Falha ao copiar o arquivo hosts.");
            }

            // 3) Configurações do aplicativo.
            var settingsFile = await ExportSettingsAsync(Path.Combine(folder, "settings.json"), cancellationToken).ConfigureAwait(false);

            if (!string.IsNullOrEmpty(settingsFile))
            {
                manifest.Add("settings.json");
            }

            // 4) Lista de programas de inicialização.
            try
            {
                var programs = await _startupManager.GetStartupProgramsAsync(cancellationToken).ConfigureAwait(false);

                File.WriteAllText(
                    Path.Combine(folder, "startup.json"),
                    JsonConvert.SerializeObject(programs, Formatting.Indented),
                    Encoding.UTF8);

                manifest.Add("startup.json");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Falha ao exportar a lista de inicialização.");
            }

            // 5) Manifesto (auditoria).
            File.WriteAllText(
                Path.Combine(folder, "manifest.json"),
                JsonConvert.SerializeObject(
                    new
                    {
                        createdAt = DateTime.Now.ToString("o", CultureInfo.InvariantCulture),
                        machineName = Environment.MachineName,
                        user = Environment.UserName,
                        application = "HL PRO OPTIMIZER",
                        files = manifest
                    },
                    Formatting.Indented),
                Encoding.UTF8);

            _logger.LogInformation("Backup completo criado com {Count} arquivo(s).", manifest.Count);

            PruneOldBackups(keep: 10);

            return folder;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha ao criar o backup completo.");
            return string.Empty;
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<string>> GetLocalBackupsAsync(CancellationToken cancellationToken = default)
    {
        return Task.Run<IReadOnlyList<string>>(() =>
        {
            try
            {
                if (!Directory.Exists(_paths.BackupDirectory))
                {
                    return [];
                }

                return Directory
                    .EnumerateDirectories(_paths.BackupDirectory, $"{BackupFolderPrefix}*")
                    .OrderByDescending(d => d, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Falha ao listar os backups locais.");
                return [];
            }
        }, cancellationToken);
    }

    // ---------------------------------------------------------------------
    // Internos
    // ---------------------------------------------------------------------

    /// <summary>Cria um ponto de restauração via WMI (exige processo elevado).</summary>
    private RestorePointInfo? TryCreateRestorePointViaWmi(string description)
    {
        try
        {
            using var scope = new ManagementScope(@"root\default");
            using var managementClass = new ManagementClass(scope, new ManagementPath("SystemRestore"), null);
            using var parameters = managementClass.GetMethodParameters("CreateRestorePoint");

            parameters["Description"] = description;
            parameters["RestorePointType"] = RestorePointTypeModifySettings;
            parameters["EventType"] = EventTypeBeginSystemChange;

            using var result = managementClass.InvokeMethod("CreateRestorePoint", parameters, null);

            var returnValue = Convert.ToUInt32(result?["ReturnValue"], CultureInfo.InvariantCulture);

            if (returnValue == 0 && result is not null)
            {
                var sequenceNumber = 0L;

                if (result["RestorePoint"] is ManagementBaseObject restorePoint)
                {
                    using (restorePoint)
                    {
                        sequenceNumber = Convert.ToInt64(restorePoint["SequenceNumber"], CultureInfo.InvariantCulture);
                    }
                }

                _logger.LogInformation("Ponto de restauração criado (seq {SequenceNumber}).", sequenceNumber);

                return new RestorePointInfo(
                    sequenceNumber,
                    description,
                    DateTime.Now,
                    TranslateRestorePointType(RestorePointTypeModifySettings));
            }

            // 0x8007043C / 12: o Windows só permite um ponto de restauração a cada 24 h.
            _logger.LogWarning("CreateRestorePoint retornou 0x{Code:X}.", returnValue);

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Falha ao criar o ponto de restauração via WMI.");
            return null;
        }
    }

    /// <summary>Constrói um caminho dentro da pasta de backups.</summary>
    private string BuildBackupPath(string fileName)
    {
        Directory.CreateDirectory(_paths.BackupDirectory);

        return Path.Combine(_paths.BackupDirectory, fileName);
    }

    /// <summary>Separa a hive do caminho relativo (ex.: "HKLM\SOFTWARE\X").</summary>
    private static (RegistryHiveKind Hive, string Path) SplitHive(string keyPath)
    {
        var separator = keyPath.IndexOf('\\');

        if (separator <= 0)
        {
            return (RegistryHiveKind.LocalMachine, keyPath);
        }

        var hiveText = keyPath[..separator];
        var relative = keyPath[(separator + 1)..];

        var hive = hiveText.Trim().ToUpperInvariant() switch
        {
            "HKCU" or "HKEY_CURRENT_USER" => RegistryHiveKind.CurrentUser,
            "HKU" or "HKEY_USERS" => RegistryHiveKind.Users,
            "HKCR" or "HKEY_CLASSES_ROOT" => RegistryHiveKind.ClassesRoot,
            "HKCC" or "HKEY_CURRENT_CONFIG" => RegistryHiveKind.CurrentConfig,
            _ => RegistryHiveKind.LocalMachine
        };

        return (hive, relative);
    }

    /// <summary>Remove caracteres inválidos de um nome de arquivo.</summary>
    private static string SanitizeFileName(string value)
    {
        var builder = new StringBuilder(value.Length);

        foreach (var character in value)
        {
            builder.Append(Array.IndexOf(Path.GetInvalidFileNameChars(), character) >= 0 ? '_' : character);
        }

        var sanitized = builder.ToString().Trim('_');

        return sanitized.Length == 0 ? "chave" : sanitized;
    }

    /// <summary>Traduz o código numérico do tipo de ponto de restauração.</summary>
    private static string TranslateRestorePointType(object? raw)
    {
        var value = raw switch
        {
            null => 13u,
            uint unsigned => unsigned,
            _ => Convert.ToUInt32(raw, CultureInfo.InvariantCulture)
        };

        return value switch
        {
            0 => "APPLICATION_INSTALL",
            1 => "APPLICATION_UNINSTALL",
            2 => "SYSTEM_CHECKPOINT",
            3 => "MODIFY_SETTINGS",
            4 => "CANCELLED_OPERATION",
            5 => "BACKUP_RECOVERY",
            6 => "DEVICE_DRIVER_INSTALL",
            7 => "BEGIN_SYSTEM_CHANGE",
            8 => "END_SYSTEM_CHANGE",
            9 => "APPLICATION_RUN",
            10 => "WINDOWS_UPDATE",
            11 => "CRITICAL_UPDATE",
            12 => "RUNTIME_PERIODIC",
            _ => "UNKNOWN"
        };
    }

    /// <summary>Converte datas WMI (CIM_DATETIME) com fallback para "agora".</summary>
    private static DateTime ParseWmiDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return DateTime.Now;
        }

        try
        {
            return ManagementDateTimeConverter.ToDateTime(value);
        }
        catch (Exception)
        {
            return DateTime.Now;
        }
    }

    /// <summary>Mantém apenas os N backups completos mais recentes.</summary>
    private void PruneOldBackups(int keep)
    {
        try
        {
            var old = Directory
                .EnumerateDirectories(_paths.BackupDirectory, $"{BackupFolderPrefix}*")
                .OrderByDescending(d => d, StringComparer.OrdinalIgnoreCase)
                .Skip(keep)
                .ToList();

            foreach (var directory in old)
            {
                try
                {
                    Directory.Delete(directory, recursive: true);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Não foi possível remover o backup antigo {Directory}.", directory);
                }
            }

            if (old.Count > 0)
            {
                _logger.LogInformation("{Count} backup(s) completo(s) antigo(s) removido(s).", old.Count);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Falha ao limpar backups antigos.");
        }
    }
}
