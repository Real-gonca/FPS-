using System.Globalization;
using System.Text.RegularExpressions;
using HLProOptimizer.Core.Abstractions;
using HLProOptimizer.Core.Models;
using HLProOptimizer.Infrastructure.Interop;
using HLProOptimizer.Infrastructure.Platform;
using Microsoft.Extensions.Logging;

namespace HLProOptimizer.Infrastructure.Drivers;

/// <summary>
/// Gerenciador de drivers baseado em <c>pnputil</c> (driver store) e nas APIs do
/// Configuration Manager (<c>cfgmgr32</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>Inventário</b>: <c>pnputil /enum-drivers</c> lista os pacotes de terceiros
/// (oem*.inf) com provedor, classe e versão. A saída é localizada, então o parser
/// reconhece os rótulos em PT-BR e EN por palavras-chave, nunca por posição.
/// </para>
/// <para>
/// <b>Procurar alterações de hardware</b> usa <see cref="NativeMethods.CM_Reenumerate_DevNode"/>
/// sobre o nó raiz — o mesmo caminho do Gerenciador de Dispositivos, sem criar
/// processo e sem exigir elevação.
/// </para>
/// <para>
/// <b>Reversão de driver</b> não tem equivalente de linha de comando suportado pela
/// Microsoft (o rollback depende de uma cópia anterior mantida pelo instalador do
/// dispositivo). O método abre o Gerenciador de Dispositivos e devolve um resultado
/// explicando o passo manual, em vez de fingir sucesso.
/// </para>
/// </remarks>
public sealed partial class DriverManagerService : IDriverManagerService
{
    /// <summary>Código de saída usado quando a operação exige ação manual do usuário.</summary>
    private const int ManualActionRequiredExitCode = 121;

    private static readonly Regex LabelValuePattern = LabelValueRegex();

    private readonly ICommandRunner _commands;
    private readonly ElevatedScriptRunner _runner;
    private readonly ISystemInformationService _systemInformation;
    private readonly ILogger<DriverManagerService> _logger;

    /// <summary>Cria o gerenciador de drivers.</summary>
    /// <param name="commands">Executor de comandos.</param>
    /// <param name="runner">Executor com elevação + streaming.</param>
    /// <param name="systemInformation">Inventário WMI (nomes de dispositivo e problemas).</param>
    /// <param name="logger">Logger.</param>
    public DriverManagerService(
        ICommandRunner commands,
        ElevatedScriptRunner runner,
        ISystemInformationService systemInformation,
        ILogger<DriverManagerService> logger)
    {
        _commands = commands;
        _runner = runner;
        _systemInformation = systemInformation;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DriverInfo>> GetPublishedDriversAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _commands
                .RunAsync("pnputil.exe", "/enum-drivers", cancellationToken, timeout: TimeSpan.FromMinutes(1))
                .ConfigureAwait(false);

            if (!result.IsSuccess)
            {
                _logger.LogWarning("pnputil /enum-drivers falhou (exit {ExitCode}): {Output}", result.ExitCode, result.CombinedOutput);
            }

            // Nomes amigáveis vêm do WMI (o pnputil só conhece o INF original).
            var namesByInf = await BuildDeviceNameLookupAsync(cancellationToken).ConfigureAwait(false);
            var parsed = ParsePublishedDrivers(result.StandardOutput, namesByInf);

            _logger.LogInformation("{Count} pacote(s) de driver de terceiro encontrado(s).", parsed.Count);

            return parsed;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha inesperada ao listar os drivers publicados.");
            return [];
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<DriverInfo>> GetProblemDevicesAsync(CancellationToken cancellationToken = default)
        => _systemInformation.GetProblemDevicesAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<CommandResult> UpdateDriverAsync(string deviceId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceId);

        _logger.LogInformation("Forçando a reavaliação do driver de {DeviceId}.", deviceId);

        // 1) Reinicia o dispositivo: o PnP reavalia o melhor driver disponível no store.
        var restart = await _commands
            .RunAsync("pnputil.exe", $"/restart-device \"{deviceId}\"", cancellationToken, timeout: TimeSpan.FromMinutes(2))
            .ConfigureAwait(false);

        if (restart.IsSuccess)
        {
            return restart;
        }

        _logger.LogWarning(
            "pnputil /restart-device não disponível ou falhou (exit {ExitCode}). Procurando alterações de hardware.",
            restart.ExitCode);

        // 2) Fallback: reenumera a árvore de dispositivos (equivalente ao "Scan for hardware changes").
        var scan = await ScanForHardwareChangesAsync(cancellationToken).ConfigureAwait(false);

        return scan.IsSuccess
            ? scan
            : restart with
            {
                StandardError = string.IsNullOrWhiteSpace(restart.StandardError)
                    ? "Não foi possível atualizar o driver por linha de comando. Use o Gerenciador de Dispositivos > Atualizar driver > Pesquisar no Windows Update."
                    : restart.StandardError
            };
    }

    /// <inheritdoc />
    public async Task<CommandResult> RollbackDriverAsync(string deviceId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceId);

        const string message =
            "A reversão de driver não possui comando equivalente suportado pela Microsoft. " +
            "O Gerenciador de Dispositivos foi aberto: selecione o dispositivo > Propriedades > aba Driver > Reverter Driver.";

        _logger.LogWarning("Rollback solicitado para {DeviceId}: {Message}", deviceId, message);

        // Abre o Gerenciador de Dispositivos para o passo manual.
        try
        {
            await _commands
                .RunHiddenAsync("mmc.exe", "devmgmt.msc", cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Não foi possível abrir o Gerenciador de Dispositivos.");
        }

        return new CommandResult(
            "pnputil.exe",
            $"/rollback {deviceId}",
            ManualActionRequiredExitCode,
            string.Empty,
            message,
            TimeSpan.Zero,
            false);
    }

    /// <inheritdoc />
    public async Task<CommandResult> UninstallDriverAsync(
        string publishedName,
        bool deleteBinary = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(publishedName);

        if (!Regex.IsMatch(publishedName, @"^oem\d+\.inf$", RegexOptions.IgnoreCase))
        {
            throw new ArgumentException("O nome publicado deve seguir o padrão oemXX.inf.", nameof(publishedName));
        }

        // /uninstall remove o driver dos dispositivos que o usam; /force permite remover pacotes em uso.
        var arguments = $"/delete-driver {publishedName} /uninstall" + (deleteBinary ? " /force" : string.Empty);

        _logger.LogInformation("Removendo o pacote de driver {PublishedName} (forçar={Force}).", publishedName, deleteBinary);

        return await _runner
            .RunAsync("pnputil.exe", arguments, null, TimeSpan.FromMinutes(5), cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<CommandResult> ScanForHardwareChangesAsync(CancellationToken cancellationToken = default)
    {
        var started = DateTime.Now;

        var nativeResult = await Task.Run(TryNativeReenumerate, cancellationToken).ConfigureAwait(false);

        if (nativeResult is not null)
        {
            return new CommandResult(
                "cfgmgr32.dll",
                "CM_Reenumerate_DevNode(root)",
                nativeResult.Value ? 0 : -1,
                nativeResult.Value ? "Procura por alterações de hardware concluída." : string.Empty,
                nativeResult.Value ? string.Empty : "A reenumeração nativa falhou.",
                DateTime.Now - started,
                false);
        }

        // Fallback: pnputil /scan-devices (Windows 11).
        _logger.LogInformation("Usando pnputil /scan-devices como alternativa.");

        return await _commands
            .RunAsync("pnputil.exe", "/scan-devices", cancellationToken, timeout: TimeSpan.FromMinutes(2))
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Reenumera a árvore de dispositivos pelo Configuration Manager.
    /// </summary>
    /// <returns><c>true</c>/<c>false</c> para sucesso/falha; <c>null</c> quando a API não está disponível.</returns>
    private bool? TryNativeReenumerate()
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        try
        {
            // CM_LOCATE_DEVNODE_NORMAL (0) com DeviceID nulo retorna o nó raiz da árvore.
            var locate = NativeMethods.CM_Locate_DevNodeW(out var rootNode, null, 0);

            if (locate != NativeMethods.CR_SUCCESS)
            {
                _logger.LogWarning("CM_Locate_DevNodeW retornou 0x{Code:X}.", locate);
                return null;
            }

            var reenumerate = NativeMethods.CM_Reenumerate_DevNode(rootNode, NativeMethods.CM_REENUMERATE_RETRY_INSTALLATION);

            if (reenumerate == NativeMethods.CR_SUCCESS)
            {
                _logger.LogInformation("Reenumeração da árvore de dispositivos concluída.");
                return true;
            }

            _logger.LogWarning("CM_Reenumerate_DevNode retornou 0x{Code:X}.", reenumerate);

            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Falha na reenumeração nativa de dispositivos.");
            return null;
        }
    }

    // ---------------------------------------------------------------------
    // Parsing da saída do pnputil
    // ---------------------------------------------------------------------

    /// <summary>
    /// Interpreta a saída de <c>pnputil /enum-drivers</c>, que é dividida em blocos
    /// separados por linha em branco.
    /// </summary>
    /// <example>
    /// <code>
    /// Nome publicado:     oem12.inf
    /// Nome original:      nv_dispi.inf
    /// Nome do provedor:   NVIDIA
    /// Nome da classe:     Display
    /// Versão do driver:   12/05/2023 31.0.15.3623
    /// </code>
    /// </example>
    private static IReadOnlyList<DriverInfo> ParsePublishedDrivers(
        string output,
        IReadOnlyDictionary<string, string> namesByInf)
    {
        var drivers = new List<DriverInfo>();
        var block = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var rawLine in output.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');

            if (string.IsNullOrWhiteSpace(line))
            {
                if (block.Count > 0)
                {
                    AddIfValid(drivers, block, namesByInf);
                    block.Clear();
                }

                continue;
            }

            var match = LabelValuePattern.Match(line.Trim());

            if (match.Success)
            {
                block[match.Groups["label"].Value.Trim()] = match.Groups["value"].Value.Trim();
            }
        }

        if (block.Count > 0)
        {
            AddIfValid(drivers, block, namesByInf);
        }

        return drivers;
    }

    /// <summary>Converte um bloco de rótulos em um <see cref="DriverInfo"/>.</summary>
    private static void AddIfValid(
        List<DriverInfo> drivers,
        IReadOnlyDictionary<string, string> block,
        IReadOnlyDictionary<string, string> namesByInf)
    {
        var published = Find(block, "published", "publicado");

        if (string.IsNullOrWhiteSpace(published))
        {
            return;
        }

        var original = Find(block, "original");
        var provider = Find(block, "provider", "provedor", "fornecedor");
        var className = Find(block, "class", "classe");
        var version = Find(block, "version", "versão", "versao");
        var signer = Find(block, "signer", "assinante", "assinado");

        var (driverDate, driverVersion) = SplitVersionAndDate(version);

        // Preferimos o nome amigável do dispositivo (WMI) ao nome do INF.
        var deviceName = !string.IsNullOrWhiteSpace(original) && namesByInf.TryGetValue(original, out var friendly)
            ? friendly
            : (string.IsNullOrWhiteSpace(original) ? published : original);

        drivers.Add(new DriverInfo
        {
            DeviceName = deviceName,
            Manufacturer = string.IsNullOrWhiteSpace(provider) ? "Desconhecido" : provider,
            Version = driverVersion,
            DriverDate = driverDate,
            DeviceClass = className ?? string.Empty,
            InfPath = original ?? string.Empty,
            PublishedName = published,
            DeviceId = published,
            IsSigned = !string.IsNullOrWhiteSpace(signer),
            Status = "OK"
        });
    }

    /// <summary>Localiza um valor por palavras-chave do rótulo (independente de idioma).</summary>
    private static string? Find(IReadOnlyDictionary<string, string> block, params string[] keywords)
    {
        foreach (var (label, value) in block)
        {
            if (keywords.Any(keyword => label.Contains(keyword, StringComparison.OrdinalIgnoreCase)))
            {
                return value;
            }
        }

        return null;
    }

    /// <summary>
    /// Separa a data e a versão do campo "Versão do driver", que o pnputil imprime
    /// como <c>dd/mm/yyyy X.Y.Z.W</c>.
    /// </summary>
    private static (DateTime? Date, string Version) SplitVersionAndDate(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return (null, "0.0");
        }

        var parts = raw.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        DateTime? date = null;
        var version = raw;

        foreach (var part in parts)
        {
            if (date is null && TryParseDriverDate(part, out var parsedDate))
            {
                date = parsedDate;
            }
            else if (part.Contains('.') && version == raw)
            {
                version = part;
            }
        }

        return (date, version);
    }

    /// <summary>Tenta interpretar a data do driver nos formatos comuns (regional e ISO).</summary>
    private static bool TryParseDriverDate(string value, out DateTime date)
    {
        string[] formats =
        [
            "dd/MM/yyyy",
            "MM/dd/yyyy",
            "yyyy-MM-dd",
            "d/M/yyyy"
        ];

        return DateTime.TryParseExact(value, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
    }

    /// <summary>Mapa INF → nome amigável do dispositivo (via WMI).</summary>
    private async Task<Dictionary<string, string>> BuildDeviceNameLookupAsync(CancellationToken cancellationToken)
    {
        var lookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            foreach (var driver in await _systemInformation.GetDriversAsync(cancellationToken).ConfigureAwait(false))
            {
                if (!string.IsNullOrWhiteSpace(driver.InfPath) && !lookup.ContainsKey(driver.InfPath))
                {
                    lookup[driver.InfPath] = driver.DeviceName;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Não foi possível enriquecer os drivers com os nomes do WMI.");
        }

        return lookup;
    }

    [GeneratedRegex(@"^(?<label>[^:]+):\s*(?<value>.*)$", RegexOptions.Compiled)]
    private static partial Regex LabelValueRegex();
}
