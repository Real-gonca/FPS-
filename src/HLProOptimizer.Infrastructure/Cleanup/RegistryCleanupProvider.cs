using System.Text;
using HLProOptimizer.Core.Abstractions;
using HLProOptimizer.Core.Enums;
using HLProOptimizer.Core.Models;
using Microsoft.Extensions.Logging;

namespace HLProOptimizer.Infrastructure.Cleanup;

/// <summary>
/// Provedor de limpeza de registro: remove apenas entradas órfãs comprovadamente
/// inofensivas (apontando para executáveis que não existem mais).
/// </summary>
/// <remarks>
/// <para>
/// <b>Filosofia.</b> "Limpeza de registro" é um dos terrenos mais perigosos de um
/// otimizador: apagar chaves aleatórias rende ganho de espaço irrelevante e risco
/// real de quebrar o sistema. Aqui só removemos:
/// </para>
/// <list type="bullet">
/// <item><description>Run/RunOnce cujo executável não existe mais (o Windows tenta iniciá-lo a cada login e falha);</description></item>
/// <item><description>MuiCache de aplicativos desinstalados (só nomes/descrições em cache);</description></item>
/// <item><description>App Paths de executáveis ausentes (atalhos de resolução de nome).</description></item>
/// </list>
/// <para>
/// <b>Codificação dos alvos.</b> <see cref="CleanupTarget.Paths"/> é uma lista de
/// strings; para o registro usamos o formato <c>HIVE|chave|valor</c>
/// (<see cref="Encode"/> / <see cref="Decode"/>), o que mantém o contrato do Core
/// inalterado e permite que o <c>ICleanupService</c> routeie o alvo de volta a este
/// provedor sem conhecer detalhes de registro.
/// </para>
/// <para>Escritas em HKLM sem elevação falham com <c>UnauthorizedAccessException</c> e são
/// reportadas como falha do alvo, sem interromper os demais.</para>
/// </remarks>
public sealed class RegistryCleanupProvider : ICleanupProvider
{
    /// <summary>Separador do formato <c>HIVE|chave|valor</c> usado em <see cref="CleanupTarget.Paths"/>.</summary>
    private const char PathSeparator = '|';

    /// <summary>Chaves de inicialização varridas em busca de órfãos.</summary>
    private static readonly (RegistryHiveKind Hive, string Key)[] RunKeys =
    [
        (RegistryHiveKind.CurrentUser, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run"),
        (RegistryHiveKind.CurrentUser, @"SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce"),
        (RegistryHiveKind.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run"),
        (RegistryHiveKind.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce"),
        (RegistryHiveKind.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run"),
        (RegistryHiveKind.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\RunOnce")
    ];

    /// <summary>Chave do cache de nomes/descrições de aplicativos.</summary>
    private const string MuiCacheKey = @"SOFTWARE\Classes\Local Settings\Software\Microsoft\Windows\Shell\MuiCache";

    /// <summary>Chave raiz de App Paths (HKLM e HKCU).</summary>
    private const string AppPathsKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths";

    private readonly IRegistryService _registry;
    private readonly IFileSystemService _fileSystem;
    private readonly IFileMetadataService _fileMetadata;
    private readonly ILogger<RegistryCleanupProvider> _logger;

    /// <summary>Cria o provedor de limpeza de registro.</summary>
    /// <param name="registry">Registro do Windows.</param>
    /// <param name="fileSystem">Verificação de existência de arquivos.</param>
    /// <param name="fileMetadata">Extração do executável de uma linha de comando.</param>
    /// <param name="logger">Logger.</param>
    public RegistryCleanupProvider(
        IRegistryService registry,
        IFileSystemService fileSystem,
        IFileMetadataService fileMetadata,
        ILogger<RegistryCleanupProvider> logger)
    {
        _registry = registry;
        _fileSystem = fileSystem;
        _fileMetadata = fileMetadata;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => "Registro";

    /// <inheritdoc />
    public IssueCategory Category => IssueCategory.Registry;

    /// <inheritdoc />
    public int Order => 30;

    /// <inheritdoc />
    /// <remarks>Órfãos em HKLM só podem ser removidos com administrador.</remarks>
    public bool RequiresAdmin => !Interop.ElevationHelper.IsProcessElevated();

    /// <inheritdoc />
    public async Task<IReadOnlyList<CleanupTarget>> ScanAsync(OptimizationLevel level, CancellationToken cancellationToken = default)
    {
        var targets = new List<CleanupTarget>();

        var runOrphans = await Task.Run(FindRunOrphans, cancellationToken).ConfigureAwait(false);

        if (runOrphans.Count > 0)
        {
            targets.Add(BuildTarget(
                id: "registry.run.orphans",
                name: "Inicialização órfã",
                description: "Entradas Run/RunOnce que apontam para programas desinstalados. O Windows tenta executá-las a cada login e falha.",
                entries: runOrphans,
                severity: Severity.Medium,
                isSelected: true));
        }

        if (level >= OptimizationLevel.Balanced)
        {
            var muiOrphans = await Task.Run(FindMuiCacheOrphans, cancellationToken).ConfigureAwait(false);

            if (muiOrphans.Count > 0)
            {
                targets.Add(BuildTarget(
                    id: "registry.muicache.orphans",
                    name: "Cache de nomes de aplicativos",
                    description: "Entradas do MuiCache de aplicativos que não existem mais (apenas cache de nomes e descrições).",
                    entries: muiOrphans,
                    severity: Severity.Low,
                    isSelected: true));
            }

            var appPathOrphans = await Task.Run(FindAppPathsOrphans, cancellationToken).ConfigureAwait(false);

            if (appPathOrphans.Count > 0)
            {
                targets.Add(BuildTarget(
                    id: "registry.apppaths.orphans",
                    name: "App Paths inválidos",
                    description: "Atalhos de resolução de caminho (App Paths) apontando para executáveis ausentes.",
                    entries: appPathOrphans,
                    severity: Severity.Low,
                    isSelected: false));
            }
        }

        _logger.LogInformation("{Count} alvo(s) de registro encontrado(s).", targets.Count);

        return targets;
    }

    /// <inheritdoc />
    public Task<CleanupResult> CleanAsync(
        IReadOnlyList<CleanupTarget> targets,
        IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(targets);

        // O acesso ao registro é E/S síncrona: movemos para a thread pool.
        return Task.Run(() => CleanCore(targets, progress, cancellationToken), cancellationToken);
    }

    /// <summary>Remove os valores/subchaves codificados em <see cref="CleanupTarget.Paths"/>.</summary>
    private CleanupResult CleanCore(
        IReadOnlyList<CleanupTarget> targets,
        IProgress<ScanProgress>? progress,
        CancellationToken cancellationToken)
    {
        var startedAt = DateTime.Now;
        var entries = new List<CleanupEntryResult>();
        var deletedValues = 0;
        var skipped = 0;
        var failed = 0;
        var processed = 0;

        foreach (var target in targets)
        {
            cancellationToken.ThrowIfCancellationRequested();

            processed++;

            progress?.Report(new ScanProgress(
                IssueCategory.Registry,
                target.Name,
                processed - 1,
                targets.Count,
                $"Removendo {target.FileCount} entrada(s) de {target.Name}..."));

            var targetDeleted = 0;
            var targetSkipped = 0;
            var targetFailed = 0;
            string? error = null;

            foreach (var encoded in target.Paths)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var (hive, keyPath, valueName) = Decode(encoded);

                if (hive is null || string.IsNullOrWhiteSpace(keyPath))
                {
                    targetSkipped++;
                    continue;
                }

                try
                {
                    // ValueName nulo indica "remover a subchave inteira" (App Paths).
                    var removed = valueName is null
                        ? _registry.DeleteSubKeyTree(hive.Value, keyPath)
                        : _registry.DeleteValue(hive.Value, keyPath, valueName);

                    if (removed)
                    {
                        targetDeleted++;
                    }
                    else
                    {
                        targetSkipped++;
                    }
                }
                catch (UnauthorizedAccessException ex)
                {
                    targetFailed++;
                    error ??= $"Sem permissão para alterar {keyPath} (requer administrador).";
                    _logger.LogDebug(ex, "Sem permissão para remover {Key}\{Value}.", keyPath, valueName);
                }
                catch (Exception ex)
                {
                    targetFailed++;
                    error ??= ex.Message;
                    _logger.LogWarning(ex, "Falha ao remover {Key}\{Value}.", keyPath, valueName);
                }
            }

            entries.Add(new CleanupEntryResult(target.Id, target.Name, targetDeleted, 0, targetSkipped, targetFailed == 0, error));

            deletedValues += targetDeleted;
            skipped += targetSkipped;
            failed += targetFailed;

            _logger.LogInformation(
                "{Target}: {Deleted} entrada(s) removida(s), {Skipped} ignorada(s), {Failed} falha(s).",
                target.Name,
                targetDeleted,
                targetSkipped,
                targetFailed);
        }

        progress?.Report(new ScanProgress(
            IssueCategory.Registry,
            "Concluído",
            targets.Count,
            targets.Count,
            $"Limpeza de registro concluída: {deletedValues} entrada(s) removida(s)."));

        // Bytes liberados são desprezíveis no registro; o resultado útil é a contagem.
        return new CleanupResult(startedAt, DateTime.Now, deletedValues, 0, skipped, failed, entries);
    }

    // ---------------------------------------------------------------------
    // Descoberta de órfãos
    // ---------------------------------------------------------------------

    /// <summary>Localiza entradas Run/RunOnce cujo executável não existe.</summary>
    private List<RegistryOrphan> FindRunOrphans()
    {
        var orphans = new List<RegistryOrphan>();

        foreach (var (hive, keyPath) in RunKeys)
        {
            try
            {
                if (!_registry.KeyExists(hive, keyPath))
                {
                    continue;
                }

                foreach (var valueName in _registry.GetValueNames(hive, keyPath))
                {
                    var command = _registry.GetString(hive, keyPath, valueName);

                    if (string.IsNullOrWhiteSpace(command))
                    {
                        continue;
                    }

                    // RunOnce com prefixo '!' é removido pelo próprio Windows após executar.
                    if (IsOrphanCommand(command))
                    {
                        orphans.Add(new RegistryOrphan(hive, keyPath, valueName, command));
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Falha ao varrer {Key}.", keyPath);
            }
        }

        return orphans;
    }

    /// <summary>Localiza entradas MuiCache de aplicativos ausentes.</summary>
    private List<RegistryOrphan> FindMuiCacheOrphans()
    {
        var orphans = new List<RegistryOrphan>();

        try
        {
            if (!_registry.KeyExists(RegistryHiveKind.CurrentUser, MuiCacheKey))
            {
                return orphans;
            }

            foreach (var valueName in _registry.GetValueNames(RegistryHiveKind.CurrentUser, MuiCacheKey))
            {
                // O nome do valor é o caminho do executável + sufixo (ex.: ".ApplicationCompany").
                var executable = StripMuiSuffix(valueName);

                if (executable.Length == 0)
                {
                    continue;
                }

                if (!IsExistingExecutable(executable))
                {
                    orphans.Add(new RegistryOrphan(RegistryHiveKind.CurrentUser, MuiCacheKey, valueName, executable));
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Falha ao varrer o MuiCache.");
        }

        return orphans;
    }

    /// <summary>Localiza subchaves de App Paths cujo executável padrão não existe.</summary>
    private List<RegistryOrphan> FindAppPathsOrphans()
    {
        var orphans = new List<RegistryOrphan>();

        foreach (var hive in new[] { RegistryHiveKind.LocalMachine, RegistryHiveKind.CurrentUser })
        {
            try
            {
                if (!_registry.KeyExists(hive, AppPathsKey))
                {
                    continue;
                }

                foreach (var subKey in _registry.GetSubKeyNames(hive, AppPathsKey))
                {
                    var keyPath = $@"{AppPathsKey}\{subKey}";
                    var executable = _registry.GetString(hive, keyPath, string.Empty);

                    if (string.IsNullOrWhiteSpace(executable))
                    {
                        continue;
                    }

                    if (!IsExistingExecutable(executable))
                    {
                        // Remove a subchave inteira (não apenas o valor padrão).
                        orphans.Add(new RegistryOrphan(hive, keyPath, null, executable));
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Falha ao varrer App Paths em {Hive}.", hive);
            }
        }

        return orphans;
    }

    /// <summary>Verifica se o executável de uma linha de comando realmente existe.</summary>
    private bool IsOrphanCommand(string command)
    {
        var executable = _fileMetadata.ResolveExecutablePath(command);

        // Sem executável resolvível (rundll32, mshta, URLs): não consideramos órfão.
        if (string.IsNullOrWhiteSpace(executable))
        {
            return false;
        }

        return !_fileSystem.FileExists(executable);
    }

    /// <summary>Verifica se um caminho de executável existe no disco.</summary>
    private bool IsExistingExecutable(string executable)
    {
        var expanded = Environment.ExpandEnvironmentVariables(executable).Trim('"').Trim();

        if (expanded.Length == 0)
        {
            return true;
        }

        // Comandos com argumentos: usa o mesmo resolvedor da limpeza de inicialização.
        var resolved = _fileMetadata.ResolveExecutablePath(expanded) ?? expanded;

        return _fileSystem.FileExists(resolved);
    }

    /// <summary>Remove o sufixo do MuiCache (ex.: ".ApplicationCompany", ".FriendlyAppName").</summary>
    private static string StripMuiSuffix(string valueName)
    {
        string[] suffixes = [".ApplicationCompany", ".FriendlyAppName", ".AppUserModelID"];

        foreach (var suffix in suffixes)
        {
            if (valueName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                return valueName[..^suffix.Length];
            }
        }

        return string.Empty;
    }

    // ---------------------------------------------------------------------
    // Montagem e codificação dos alvos
    // ---------------------------------------------------------------------

    private static CleanupTarget BuildTarget(
        string id,
        string name,
        string description,
        IReadOnlyList<RegistryOrphan> entries,
        Severity severity,
        bool isSelected) => new()
        {
            Id = id,
            Name = name,
            Description = description,
            Category = IssueCategory.Registry,
            Paths = entries.Select(Encode).ToList(),
            // Registro não libera espaço mensurável: reportamos a quantidade de entradas.
            EstimatedBytes = 0,
            FileCount = entries.Count,
            IsSafe = true,
            RequiresAdmin = entries.Any(e => e.Hive == RegistryHiveKind.LocalMachine),
            Severity = severity,
            IsSelected = isSelected
        };

    /// <summary>Serializa um órfão como <c>HIVE|chave|valor</c>.</summary>
    private static string Encode(RegistryOrphan orphan)
    {
        var builder = new StringBuilder();

        builder.Append(orphan.Hive.ToString());
        builder.Append(PathSeparator);
        builder.Append(orphan.KeyPath);
        builder.Append(PathSeparator);
        builder.Append(orphan.ValueName ?? string.Empty);

        return builder.ToString();
    }

    /// <summary>Reverte <see cref="Encode"/>.</summary>
    private static (RegistryHiveKind? Hive, string? KeyPath, string? ValueName) Decode(string encoded)
    {
        if (string.IsNullOrWhiteSpace(encoded))
        {
            return (null, null, null);
        }

        var parts = encoded.Split(PathSeparator, 3);

        if (parts.Length < 2 || !Enum.TryParse<RegistryHiveKind>(parts[0], ignoreCase: true, out var hive))
        {
            return (null, null, null);
        }

        return (hive, parts[1], parts.Length > 2 && parts[2].Length > 0 ? parts[2] : null);
    }

    /// <summary>Uma entrada de registro órfã.</summary>
    /// <param name="Hive">Hive.</param>
    /// <param name="KeyPath">Caminho da chave.</param>
    /// <param name="ValueName">Nome do valor (null = remover a subchave inteira).</param>
    /// <param name="Detail">Comando/caminho que motivou a classificação como órfã.</param>
    private sealed record RegistryOrphan(RegistryHiveKind Hive, string KeyPath, string? ValueName, string Detail);
}
