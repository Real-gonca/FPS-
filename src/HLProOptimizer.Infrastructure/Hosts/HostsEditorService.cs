using System.Text;
using HLProOptimizer.Core.Abstractions;
using HLProOptimizer.Core.Models;
using HLProOptimizer.Infrastructure.Interop;
using Microsoft.Extensions.Logging;

namespace HLProOptimizer.Infrastructure.Hosts;

/// <summary>
/// Editor do arquivo <c>hosts</c> com bloqueio de domínios de telemetria/anúncios.
/// </summary>
/// <remarks>
/// <para>
/// <b>Round-trip fiel.</b> Cada linha é preservada em <see cref="HostsEntry.RawLine"/>:
/// comentários, linhas em branco e entradas de terceiros voltam exatamente como
/// estavam. Só as linhas que o aplicativo cria/remover são modificadas.
/// </para>
/// <para>
/// <b>Backup obrigatório.</b> Toda gravação copia o arquivo atual para
/// <see cref="ISystemPaths.BackupDirectory"/> antes de sobrescrever, e
/// <see cref="RestoreBackupAsync"/> recupera o mais recente. Um hosts corrompido
/// derruba a resolução de nomes da máquina inteira — não dá para arriscar.
/// </para>
/// <para>
/// <b>Elevação.</b> O arquivo fica em <c>System32\drivers\etc</c> e só
/// administradores escrevem nele. Quando o processo não é elevado, gravamos o
/// conteúdo num temporário e pedimos UAC uma vez para copiá-lo por cima.
/// </para>
/// </remarks>
public sealed class HostsEditorService : IHostsEditorService
{
    /// <summary>Marcador das linhas criadas pelo aplicativo.</summary>
    private const string MarkerComment = "HL PRO OPTIMIZER";

    /// <summary>IP usado para bloquear um domínio (rota nula, mais rápido que 127.0.0.1).</summary>
    private const string BlockAddress = "0.0.0.0";

    /// <summary>Domínios de telemetria, anúncios e rastreamento conhecidos pelo produto.</summary>
    private static readonly string[] TrackerDomains =
    [
        // Microsoft / Windows telemetry
        "vortex.data.microsoft.com",
        "vortex-win.data.microsoft.com",
        "vortex-sandbox.data.microsoft.com",
        "telemetry.microsoft.com",
        "telemetry.urs.microsoft.com",
        "telecommand.telemetry.microsoft.com",
        "watson.telemetry.microsoft.com",
        "oca.telemetry.microsoft.com",
        "ceuswatcab01.blob.core.windows.net",
        "ceuswatcab02.blob.core.windows.net",
        "eaus2watcab01.blob.core.windows.net",
        "eaus2watcab02.blob.core.windows.net",
        "weus2watcab01.blob.core.windows.net",
        "weus2watcab02.blob.core.windows.net",
        "umwatson.events.data.microsoft.com",
        "kmwatsonc.events.data.microsoft.com",
        "settings-win.data.microsoft.com",
        "feedback.microsoft-hohm.com",
        "feedback.search.microsoft.com",
        "feedback.windows.com",
        "rad.msn.com",
        "diagnostics.support.microsoft.com",
        "corpext.msitadfs.glbdns2.microsoft.com",
        "compatexchange.cloudapp.net",
        "a-0001.a-msedge.net",
        "sls.update.microsoft.com.akadns.net",
        "statsfe2.update.microsoft.com.akadns.net",
        "survey.watson.microsoft.com",
        // Anúncios / rastreamento web
        "ads.msn.com",
        "ads1.msads.net",
        "az361816.vo.msecnd.net",
        "doubleclick.net",
        "googleadservices.com",
        "pagead2.googlesyndication.com",
        "adservice.google.com",
        "ad.doubleclick.net",
        "analytics.tiktok.com",
        "telemetry.adobe.io",
        "metrics.icloud.com",
        "advertising.apple.com",
        "incoming.telemetry.mozilla.org",
        "crash-stats.mozilla.org"
    ];

    private readonly ISystemPaths _paths;
    private readonly ICommandRunner _commands;
    private readonly ILogger<HostsEditorService> _logger;

    /// <summary>Cria o editor de hosts.</summary>
    /// <param name="paths">Caminhos do aplicativo.</param>
    /// <param name="commands">Executor de comandos (cópia elevada).</param>
    /// <param name="logger">Logger.</param>
    public HostsEditorService(ISystemPaths paths, ICommandRunner commands, ILogger<HostsEditorService> logger)
    {
        _paths = paths;
        _commands = commands;
        _logger = logger;
    }

    /// <inheritdoc />
    public IReadOnlyList<string> KnownTrackerDomains => TrackerDomains;

    /// <inheritdoc />
    public Task<HostsFileModel> LoadAsync(CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            var path = _paths.HostsFilePath;

            try
            {
                if (!File.Exists(path))
                {
                    _logger.LogWarning("Arquivo hosts não encontrado em {Path}.", path);

                    return new HostsFileModel
                    {
                        FilePath = path,
                        Entries = [],
                        IsReadOnly = false,
                        BackupPath = FindLatestBackup(),
                        LastModified = null
                    };
                }

                var lines = File.ReadAllLines(path, Encoding.UTF8);
                var entries = lines.Select(ParseLine).ToList();

                var attributes = File.GetAttributes(path);

                _logger.LogDebug(
                    "Hosts carregado: {Lines} linha(s), {Blocked} bloqueio(s).",
                    entries.Count,
                    entries.Count(e => e.IsBlocked && !e.IsCommentOnly));

                return new HostsFileModel
                {
                    FilePath = path,
                    Entries = entries,
                    IsReadOnly = attributes.HasFlag(FileAttributes.ReadOnly),
                    BackupPath = FindLatestBackup(),
                    LastModified = File.GetLastWriteTime(path)
                };
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogError(ex, "Sem permissão para ler {Path}.", path);

                return new HostsFileModel
                {
                    FilePath = path,
                    Entries = [],
                    IsReadOnly = true,
                    BackupPath = FindLatestBackup(),
                    LastModified = null
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Falha ao carregar o arquivo hosts.");

                return new HostsFileModel
                {
                    FilePath = path,
                    Entries = [],
                    IsReadOnly = false,
                    BackupPath = FindLatestBackup(),
                    LastModified = null
                };
            }
        }, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> SaveAsync(HostsFileModel model, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(model);

        var path = _paths.HostsFilePath;

        try
        {
            // 1) Backup do estado atual (somente quando o arquivo existe).
            var backupPath = await CreateBackupAsync(path, cancellationToken).ConfigureAwait(false);

            // 2) Conteúdo serializado.
            var content = SerializeEntries(model.Entries);

            // 3) Gravação (elevada se preciso).
            var saved = await WriteHostsFileAsync(path, content, cancellationToken).ConfigureAwait(false);

            if (saved)
            {
                _logger.LogInformation("Hosts salvo ({Lines} linhas). Backup: {Backup}", model.Entries.Count, backupPath ?? "nenhum");
            }

            return saved;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha ao salvar o arquivo hosts.");
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<int> BlockDomainsAsync(IEnumerable<string> domains, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(domains);

        var model = await LoadAsync(cancellationToken).ConfigureAwait(false);
        var existing = new HashSet<string>(model.BlockedHostnames, StringComparer.OrdinalIgnoreCase);

        var entries = model.Entries.ToList();
        var newEntries = new List<HostsEntry>();

        foreach (var domain in NormalizeDomains(domains))
        {
            if (existing.Contains(domain))
            {
                continue;
            }

            newEntries.Add(new HostsEntry
            {
                IpAddress = BlockAddress,
                Hostname = domain,
                Comment = MarkerComment,
                IsBlocked = true
            });

            existing.Add(domain);
        }

        var added = newEntries.Count;

        if (added == 0)
        {
            _logger.LogDebug("Nenhum domínio novo para bloquear.");
            return 0;
        }

        // Cabeçalho do bloco só na primeira vez (evita duplicar a cada bloqueio).
        var hasHeader = entries.Any(e => e.IsCommentOnly && e.Comment.Contains(MarkerComment, StringComparison.OrdinalIgnoreCase));

        if (!hasHeader && entries.Count > 0)
        {
            entries.Add(new HostsEntry
            {
                IpAddress = string.Empty,
                Hostname = string.Empty,
                Comment = $"--- Bloqueios do {MarkerComment} ---",
                IsCommentOnly = true,
                RawLine = $"# --- Bloqueios do {MarkerComment} ---"
            });
        }

        entries.AddRange(newEntries);

        var saved = await SaveAsync(new HostsFileModel
        {
            FilePath = model.FilePath,
            Entries = entries,
            IsReadOnly = model.IsReadOnly
        }, cancellationToken).ConfigureAwait(false);

        return saved ? added : 0;
    }

    /// <inheritdoc />
    public async Task<int> UnblockDomainsAsync(IEnumerable<string> domains, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(domains);

        var targets = NormalizeDomains(domains).ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (targets.Count == 0)
        {
            return 0;
        }

        var model = await LoadAsync(cancellationToken).ConfigureAwait(false);

        var removed = 0;
        var entries = new List<HostsEntry>();

        foreach (var entry in model.Entries)
        {
            if (!entry.IsCommentOnly && targets.Contains(entry.Hostname))
            {
                removed++;
                continue;
            }

            entries.Add(entry);
        }

        if (removed == 0)
        {
            _logger.LogDebug("Nenhum domínio bloqueado correspondente para remover.");
            return 0;
        }

        // Remove o cabeçalho do bloco quando não restam bloqueios nossos.
        var stillOurs = entries.Any(e => !e.IsCommentOnly && string.Equals(e.Comment, MarkerComment, StringComparison.OrdinalIgnoreCase));

        if (!stillOurs)
        {
            entries.RemoveAll(e => e.IsCommentOnly && e.Comment.Contains(MarkerComment, StringComparison.OrdinalIgnoreCase));
        }

        var saved = await SaveAsync(new HostsFileModel
        {
            FilePath = model.FilePath,
            Entries = entries,
            IsReadOnly = model.IsReadOnly
        }, cancellationToken).ConfigureAwait(false);

        return saved ? removed : 0;
    }

    /// <inheritdoc />
    public async Task<string> ExportAsync(string destinationFile, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationFile);

        var path = _paths.HostsFilePath;

        return await Task.Run(() =>
        {
            try
            {
                var directory = Path.GetDirectoryName(destinationFile);

                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                if (File.Exists(path))
                {
                    File.Copy(path, destinationFile, overwrite: true);
                }
                else
                {
                    File.WriteAllText(destinationFile, DefaultHostsHeader(), Encoding.UTF8);
                }

                _logger.LogInformation("Hosts exportado para {Destination}.", destinationFile);

                return destinationFile;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Falha ao exportar o arquivo hosts para {Destination}.", destinationFile);
                return string.Empty;
            }
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<int> ImportAsync(string sourceFile, bool blockImported = true, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceFile);

        if (!File.Exists(sourceFile))
        {
            _logger.LogWarning("Arquivo de importação não encontrado: {File}.", sourceFile);
            return 0;
        }

        var imported = new List<string>();

        try
        {
            foreach (var rawLine in await File.ReadAllLinesAsync(sourceFile, Encoding.UTF8, cancellationToken).ConfigureAwait(false))
            {
                var line = rawLine.Trim();

                // Linhas de comentário puras são ignoradas.
                if (line.Length == 0 || line.StartsWith('#'))
                {
                    continue;
                }

                // Formatos aceitos: "dominio.com" ou "0.0.0.0 dominio.com # comentário".
                var parts = line.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
                var candidate = parts.Length > 1 ? parts[1] : parts[0];

                candidate = candidate.Trim();

                if (IsPlausibleHostname(candidate))
                {
                    imported.Add(candidate);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha ao ler o arquivo de importação {File}.", sourceFile);
            return 0;
        }

        var unique = NormalizeDomains(imported).ToList();

        _logger.LogInformation("{Count} domínio(s) importado(s) de {File} (bloquear={Block}).", unique.Count, sourceFile, blockImported);

        if (unique.Count == 0)
        {
            return 0;
        }

        if (blockImported)
        {
            return await BlockDomainsAsync(unique, cancellationToken).ConfigureAwait(false);
        }

        // Sem bloqueio: importa como linhas comentadas para o usuário revisar/ativar depois.
        var model = await LoadAsync(cancellationToken).ConfigureAwait(false);
        var entries = model.Entries.ToList();
        var existing = model.BlockedHostnames.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var added = 0;

        foreach (var domain in unique.Where(d => !existing.Contains(d)))
        {
            entries.Add(new HostsEntry
            {
                IpAddress = BlockAddress,
                Hostname = domain,
                Comment = $"importado (desativado) — {MarkerComment}",
                IsCommentOnly = true,
                RawLine = $"# {BlockAddress} {domain} # importado (desativado)"
            });

            added++;
        }

        if (added == 0)
        {
            return 0;
        }

        var saved = await SaveAsync(new HostsFileModel
        {
            FilePath = model.FilePath,
            Entries = entries,
            IsReadOnly = model.IsReadOnly
        }, cancellationToken).ConfigureAwait(false);

        return saved ? added : 0;
    }

    /// <inheritdoc />
    public async Task<bool> RestoreBackupAsync(CancellationToken cancellationToken = default)
    {
        var backup = FindLatestBackup();

        if (backup is null)
        {
            _logger.LogWarning("Nenhum backup do hosts disponível para restauração.");
            return false;
        }

        try
        {
            var content = await File.ReadAllTextAsync(backup, Encoding.UTF8, cancellationToken).ConfigureAwait(false);
            var restored = await WriteHostsFileAsync(_paths.HostsFilePath, content, cancellationToken).ConfigureAwait(false);

            if (restored)
            {
                _logger.LogInformation("Hosts restaurado a partir de {Backup}.", backup);
            }

            return restored;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha ao restaurar o backup {Backup}.", backup);
            return false;
        }
    }

    // ---------------------------------------------------------------------
    // Internos
    // ---------------------------------------------------------------------

    /// <summary>Interpreta uma linha do arquivo hosts.</summary>
    private static HostsEntry ParseLine(string rawLine)
    {
        var line = rawLine.TrimEnd('\r');
        var trimmed = line.Trim();

        // Linha em branco ou comentário puro.
        if (trimmed.Length == 0 || trimmed.StartsWith('#'))
        {
            return new HostsEntry
            {
                IpAddress = string.Empty,
                Hostname = string.Empty,
                Comment = trimmed.TrimStart('#').Trim(),
                IsCommentOnly = true,
                RawLine = line
            };
        }

        var commentIndex = trimmed.IndexOf('#');
        var data = commentIndex >= 0 ? trimmed[..commentIndex] : trimmed;
        var comment = commentIndex >= 0 ? trimmed[(commentIndex + 1)..].Trim() : string.Empty;

        var parts = data.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length < 2)
        {
            // Linha malformada: preserva como comentário para não perdê-la na regravação.
            return new HostsEntry
            {
                IpAddress = string.Empty,
                Hostname = string.Empty,
                Comment = trimmed,
                IsCommentOnly = true,
                RawLine = line
            };
        }

        var address = parts[0];

        return new HostsEntry
        {
            IpAddress = address,
            Hostname = parts[1],
            Comment = comment,
            IsBlocked = IsBlockingAddress(address),
            RawLine = line
        };
    }

    /// <summary>0.0.0.0, :: e 127.0.0.1 são endereços usados para bloquear um host.</summary>
    private static bool IsBlockingAddress(string address) =>
        address is "0.0.0.0" or "0" or "::" or "::0" or "127.0.0.1";

    /// <summary>Serializa as entradas de volta para o formato hosts.</summary>
    private static string SerializeEntries(IReadOnlyList<HostsEntry> entries)
    {
        var builder = new StringBuilder();

        if (entries.Count == 0)
        {
            builder.Append(DefaultHostsHeader());
            return builder.ToString();
        }

        foreach (var entry in entries)
        {
            builder.AppendLine(entry.ToHostsLine());
        }

        return builder.ToString();
    }

    /// <summary>Cabeçalho padrão quando o arquivo não existe.</summary>
    private static string DefaultHostsHeader() =>
        """
        # Copyright (c) 1993-2009 Microsoft Corp.
        #
        # Arquivo hosts do Windows.
        # Formato: <endereço IP> <hostname> [# comentário]
        #
        127.0.0.1       localhost
        ::1             localhost

        """;

    /// <summary>Cria um backup datado do arquivo atual.</summary>
    private async Task<string?> CreateBackupAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            Directory.CreateDirectory(_paths.BackupDirectory);

            var backupPath = Path.Combine(_paths.BackupDirectory, $"hosts-{DateTime.Now:yyyyMMdd-HHmmss}.bak");

            await Task.Run(() => File.Copy(path, backupPath, overwrite: true), cancellationToken).ConfigureAwait(false);

            PruneOldBackups(keep: 10);

            return backupPath;
        }
        catch (Exception ex)
        {
            // Backup é melhor esforço: a gravação continua mesmo se ele falhar.
            _logger.LogWarning(ex, "Não foi possível criar o backup do arquivo hosts.");
            return null;
        }
    }

    /// <summary>Mantém apenas os backups mais recentes.</summary>
    private void PruneOldBackups(int keep)
    {
        try
        {
            var backups = Directory
                .EnumerateFiles(_paths.BackupDirectory, "hosts-*.bak")
                .OrderByDescending(f => f, StringComparer.OrdinalIgnoreCase)
                .Skip(keep)
                .ToList();

            foreach (var backup in backups)
            {
                File.Delete(backup);
            }

            if (backups.Count > 0)
            {
                _logger.LogDebug("{Count} backup(s) antigo(s) do hosts removido(s).", backups.Count);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Falha ao limpar backups antigos do hosts.");
        }
    }

    /// <summary>Backup mais recente do hosts, ou <c>null</c>.</summary>
    private string? FindLatestBackup()
    {
        try
        {
            if (!Directory.Exists(_paths.BackupDirectory))
            {
                return null;
            }

            return Directory
                .EnumerateFiles(_paths.BackupDirectory, "hosts-*.bak")
                .OrderByDescending(f => f, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Falha ao procurar backups do hosts.");
            return null;
        }
    }

    /// <summary>Grava o conteúdo no arquivo hosts, elevando apenas quando necessário.</summary>
    private async Task<bool> WriteHostsFileAsync(string path, string content, CancellationToken cancellationToken)
    {
        if (ElevationHelper.IsProcessElevated())
        {
            try
            {
                // Remove o atributo somente leitura, se existir.
                if (File.Exists(path) && File.GetAttributes(path).HasFlag(FileAttributes.ReadOnly))
                {
                    File.SetAttributes(path, File.GetAttributes(path) & ~FileAttributes.ReadOnly);
                }

                await File.WriteAllTextAsync(path, content, Encoding.UTF8, cancellationToken).ConfigureAwait(false);

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Falha ao gravar o hosts mesmo com elevação.");
                return false;
            }
        }

        var tempFile = Path.Combine(_paths.TempDirectory, $"hosts-{Guid.NewGuid():N}.tmp");

        try
        {
            await File.WriteAllTextAsync(tempFile, content, Encoding.UTF8, cancellationToken).ConfigureAwait(false);

            // Uma única elevação para copiar o arquivo por cima do hosts.
            var result = await _commands
                .RunAsync(
                    "cmd.exe",
                    $"/c copy /y \"{tempFile}\" \"{path}\" > nul",
                    cancellationToken,
                    elevated: true,
                    timeout: TimeSpan.FromMinutes(2))
                .ConfigureAwait(false);

            if (result.ExitCode == 1223)
            {
                _logger.LogWarning("Gravação do hosts cancelada pelo usuário (UAC).");
                return false;
            }

            if (!result.IsSuccess)
            {
                _logger.LogError("Falha ao copiar o hosts (exit {ExitCode}).", result.ExitCode);
                return false;
            }

            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha ao gravar o arquivo hosts de forma elevada.");
            return false;
        }
        finally
        {
            try
            {
                if (File.Exists(tempFile))
                {
                    File.Delete(tempFile);
                }
            }
            catch (Exception)
            {
                // Temporário: limpeza é melhor esforço.
            }
        }
    }

    /// <summary>Normaliza e valida uma coleção de domínios.</summary>
    private static IEnumerable<string> NormalizeDomains(IEnumerable<string> domains) =>
        domains
            .Where(d => !string.IsNullOrWhiteSpace(d))
            .Select(d => d.Trim().ToLowerInvariant().TrimEnd('.'))
            .Where(IsPlausibleHostname)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(d => d, StringComparer.OrdinalIgnoreCase);

    /// <summary>Validação mínima de hostname (evita escrever lixo no hosts).</summary>
    private static bool IsPlausibleHostname(string value) =>
        value.Length is >= 4 and <= 253 &&
        value.Contains('.') &&
        !value.Contains(' ') &&
        !value.StartsWith('#') &&
        value.All(c => char.IsLetterOrDigit(c) || c is '.' or '-' or '_');
}
