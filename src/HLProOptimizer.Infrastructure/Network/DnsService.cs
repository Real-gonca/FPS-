using System.Net.NetworkInformation;
using HLProOptimizer.Core.Abstractions;
using HLProOptimizer.Core.Enums;
using HLProOptimizer.Core.Models;
using Microsoft.Extensions.Logging;

namespace HLProOptimizer.Infrastructure.Network;

/// <summary>
/// Configuração e diagnóstico de DNS.
/// </summary>
/// <remarks>
/// <para>
/// <b>Leitura</b> é feita direto no registro
/// (<c>HKLM\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces\{GUID}</c>),
/// que é a fonte da verdade do Windows: <c>NameServer</c> guarda o DNS estático e
/// <c>DhcpNameServer</c> o DNS entregue pelo DHCP. Isso permite distinguir
/// "automático" de "estático" com precisão, algo que a API gerenciada não expõe.
/// </para>
/// <para>
/// <b>Escrita</b> usa <c>netsh interface ipv4|ipv6 set dnsservers</c>, que exige
/// elevação — o <see cref="ICommandRunner"/> trata o prompt de UAC e devolve
/// exit code 1223 quando o usuário cancela.
/// </para>
/// </remarks>
public sealed class DnsService : IDnsService
{
    private const string TcpipInterfacesKey = @"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces";
    private const string DnscacheInterfacesKey = @"SYSTEM\CurrentControlSet\Services\Dnscache\Parameters\Interfaces";

    private readonly ICommandRunner _commands;
    private readonly IRegistryService _registry;
    private readonly ILogger<DnsService> _logger;

    /// <summary>Cria o serviço de DNS.</summary>
    /// <param name="commands">Executor de comandos (netsh/ipconfig).</param>
    /// <param name="registry">Registro do Windows.</param>
    /// <param name="logger">Logger.</param>
    public DnsService(ICommandRunner commands, IRegistryService registry, ILogger<DnsService> logger)
    {
        _commands = commands;
        _registry = registry;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<DnsConfiguration>> GetConfigurationsAsync(CancellationToken cancellationToken = default)
    {
        return Task.Run<IReadOnlyList<DnsConfiguration>>(() =>
        {
            var configurations = new List<DnsConfiguration>();

            foreach (var networkInterface in GetActiveAdapters())
            {
                cancellationToken.ThrowIfCancellationRequested();

                configurations.Add(ReadConfiguration(networkInterface));
            }

            _logger.LogDebug("{Count} configuração(ões) de DNS lida(s).", configurations.Count);

            return configurations
                .OrderByDescending(c => !c.IsAutomatic || !string.IsNullOrEmpty(c.PrimaryDns))
                .ThenBy(c => c.AdapterName, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> SetDnsAsync(
        string adapterName,
        string primaryDns,
        string? secondaryDns,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(adapterName);
        ArgumentException.ThrowIfNullOrWhiteSpace(primaryDns);

        var ok = true;

        // IPv4
        ok &= await RunNetshAsync(
            $"interface ipv4 set dnsservers name=\"{adapterName}\" static {primaryDns} validate=no",
            cancellationToken).ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(secondaryDns))
        {
            ok &= await RunNetshAsync(
                $"interface ipv4 add dnsservers name=\"{adapterName}\" {secondaryDns} index=2 validate=no",
                cancellationToken).ConfigureAwait(false);
        }

        // IPv6 (melhor esforço: muitos adaptadores não têm IPv6 ativo).
        await RunNetshAsync(
            $"interface ipv6 set dnsservers name=\"{adapterName}\" static {primaryDns} validate=no",
            cancellationToken).ConfigureAwait(false);

        if (ok)
        {
            await _commands
                .RunAsync("ipconfig.exe", "/flushdns", cancellationToken, timeout: TimeSpan.FromSeconds(30))
                .ConfigureAwait(false);

            _logger.LogInformation("DNS de {Adapter} definido para {Primary}/{Secondary}.", adapterName, primaryDns, secondaryDns);
        }

        return ok;
    }

    /// <inheritdoc />
    public async Task<int> ApplyPresetAsync(DnsPreset preset, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preset);

        var adapters = GetActiveAdapters().ToList();
        var applied = 0;

        foreach (var adapter in adapters)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                if (await SetDnsAsync(adapter.Name, preset.Primary, preset.Secondary, cancellationToken).ConfigureAwait(false))
                {
                    applied++;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Falha ao aplicar o preset {Preset} em {Adapter}.", preset.Name, adapter.Name);
            }
        }

        _logger.LogInformation("Preset DNS {Preset} aplicado em {Applied}/{Total} adaptador(es).", preset.Name, applied, adapters.Count);

        return applied;
    }

    /// <inheritdoc />
    public async Task<bool> ResetToAutomaticAsync(string adapterName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(adapterName);

        var ok = await RunNetshAsync(
            $"interface ipv4 set dnsservers name=\"{adapterName}\" dhcp",
            cancellationToken).ConfigureAwait(false);

        await RunNetshAsync(
            $"interface ipv6 set dnsservers name=\"{adapterName}\" dhcp",
            cancellationToken).ConfigureAwait(false);

        if (ok)
        {
            await _commands
                .RunAsync("ipconfig.exe", "/flushdns", cancellationToken, timeout: TimeSpan.FromSeconds(30))
                .ConfigureAwait(false);

            _logger.LogInformation("DNS de {Adapter} voltou para automático (DHCP).", adapterName);
        }

        return ok;
    }

    /// <inheritdoc />
    public async Task<CommandResult> FlushCacheAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Limpando o cache DNS.");

        return await _commands
            .RunAsync("ipconfig.exe", "/flushdns", cancellationToken, timeout: TimeSpan.FromSeconds(60))
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> GetCachedEntriesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _commands
                .RunAsync("ipconfig.exe", "/displaydns", cancellationToken, timeout: TimeSpan.FromSeconds(60))
                .ConfigureAwait(false);

            return ParseDisplayDns(result.StandardOutput);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha ao ler o cache DNS.");
            return [];
        }
    }

    /// <inheritdoc />
    public async Task<bool> SetDnsOverHttpsAsync(string adapterName, bool enabled, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(adapterName);

        var configuration = GetActiveAdapters()
            .Where(a => string.Equals(a.Name, adapterName, StringComparison.OrdinalIgnoreCase))
            .Select(ReadConfiguration)
            .FirstOrDefault();

        if (configuration is null)
        {
            _logger.LogWarning("Adaptador {Adapter} não encontrado para configurar DoH.", adapterName);
            return false;
        }

        if (!enabled)
        {
            // Template vazio devolve o adaptador à resolução simples.
            return await RunNetshAsync(
                $"dns set encryption servers={configuration.PrimaryDns} dohtemplate=",
                cancellationToken).ConfigureAwait(false);
        }

        var template = ResolveDohTemplate(configuration.PrimaryDns);

        if (template is null)
        {
            _logger.LogWarning(
                "O servidor {Dns} não tem template DoH conhecido; habilite um preset público primeiro.",
                configuration.PrimaryDns);

            return false;
        }

        var servers = string.IsNullOrEmpty(configuration.SecondaryDns)
            ? configuration.PrimaryDns
            : $"{configuration.PrimaryDns},{configuration.SecondaryDns}";

        var ok = await RunNetshAsync($"dns set encryption servers={servers} dohtemplate={template}", cancellationToken)
            .ConfigureAwait(false);

        if (ok)
        {
            _logger.LogInformation("DNS-over-HTTPS habilitado em {Adapter} (template {Template}).", adapterName, template);
        }

        return ok;
    }

    // ---------------------------------------------------------------------
    // Internos
    // ---------------------------------------------------------------------

    /// <summary>Adaptadores físicos e ativos (exclui loopback/túneis).</summary>
    private static IEnumerable<NetworkInterface> GetActiveAdapters()
    {
        if (!OperatingSystem.IsWindows())
        {
            return [];
        }

        return NetworkInterface.GetAllNetworkInterfaces()
            .Where(ni => ni.OperationalStatus == OperationalStatus.Up)
            .Where(ni => ni.NetworkInterfaceType is not (NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel))
            .Where(ni => !string.IsNullOrWhiteSpace(ni.Name));
    }

    /// <summary>Lê a configuração de DNS de um adaptador a partir do registro.</summary>
    private DnsConfiguration ReadConfiguration(NetworkInterface networkInterface)
    {
        var keyPath = $@"{TcpipInterfacesKey}\{networkInterface.Id}";

        // NameServer = DNS estático configurado pelo usuário; vazio = DHCP.
        var staticServers = SplitServers(_registry.GetString(RegistryHiveKind.LocalMachine, keyPath, "NameServer"));
        var dhcpServers = SplitServers(_registry.GetString(RegistryHiveKind.LocalMachine, keyPath, "DhcpNameServer"));

        var isAutomatic = staticServers.Count == 0;
        var servers = isAutomatic ? dhcpServers : staticServers;

        var dohEnabled = ReadDohEnabled(networkInterface.Id);

        return new DnsConfiguration(
            AdapterName: networkInterface.Name,
            PrimaryDns: servers.Count > 0 ? servers[0] : string.Empty,
            SecondaryDns: servers.Count > 1 ? servers[1] : string.Empty,
            IsAutomatic: isAutomatic,
            DohEnabled: dohEnabled);
    }

    /// <summary>Lê o estado de DoH do adaptador (Windows 10 21H2+).</summary>
    private bool ReadDohEnabled(string interfaceId)
    {
        try
        {
            var enableDoh = _registry.GetDword(
                RegistryHiveKind.LocalMachine,
                $@"{DnscacheInterfacesKey}\{interfaceId}",
                "EnableDoh");

            // 0 = desativado, 1 = automático (criptografado quando possível), 2 = somente criptografado.
            return enableDoh is > 0;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Não foi possível ler o estado de DoH de {Interface}.", interfaceId);
            return false;
        }
    }

    /// <summary>
    /// Separa a lista de servidores do registro (separados por espaço, vírgula ou quebra de linha).
    /// </summary>
    private static IReadOnlyList<string> SplitServers(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return [];
        }

        return raw
            .Split([' ', ',', '\r', '\n', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(s => s.Contains('.') || s.Contains(':'))
            .ToList();
    }

    /// <summary>Mapeia um servidor DNS conhecido para o template DoH do Windows.</summary>
    private static string? ResolveDohTemplate(string? primaryDns) => primaryDns?.Trim() switch
    {
        "1.1.1.1" or "1.0.0.1" => "Cloudflare",
        "8.8.8.8" or "8.8.4.4" => "Google",
        "9.9.9.9" or "149.112.112.112" => "Quad9",
        _ => null
    };

    /// <summary>Executa um comando netsh (elevado quando o processo não é administrador).</summary>
    private async Task<bool> RunNetshAsync(string arguments, CancellationToken cancellationToken)
    {
        try
        {
            var elevated = !Interop.ElevationHelper.IsProcessElevated();

            var result = await _commands
                .RunAsync("netsh.exe", arguments, cancellationToken, elevated: elevated, timeout: TimeSpan.FromSeconds(60))
                .ConfigureAwait(false);

            if (result.IsSuccess)
            {
                return true;
            }

            // 1223 = usuário cancelou o UAC (não é falha técnica).
            if (result.ExitCode == 1223)
            {
                _logger.LogWarning("Elevação cancelada pelo usuário ao executar: netsh {Arguments}", arguments);
            }
            else
            {
                _logger.LogWarning(
                    "netsh {Arguments} falhou (exit {ExitCode}): {Output}",
                    arguments,
                    result.ExitCode,
                    result.CombinedOutput);
            }

            return false;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exceção ao executar netsh {Arguments}.", arguments);
            return false;
        }
    }

    /// <summary>
    /// Extrai os nomes de host da saída de <c>ipconfig /displaydns</c>.
    /// </summary>
    /// <remarks>
    /// O formato muda conforme o idioma (PT-BR: "Nome do Registro:", EN: "Record Name:").
    /// Em vez de depender dos rótulos, reconhecemos as linhas que contêm apenas um
    /// hostname válido — é estável entre idiomas e versões do Windows.
    /// </remarks>
    private static IReadOnlyList<string> ParseDisplayDns(string output)
    {
        var entries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var rawLine in output.Split('\n'))
        {
            var line = rawLine.Trim();

            // Linhas de hostname são recuadas e não contêm espaços, dois-pontos ou traços de separação.
            if (line.Length is < 4 or > 253)
            {
                continue;
            }

            if (line.Contains(' ') || line.Contains(':'))
            {
                continue;
            }

            // Linha separadora (ex.: "----------------------------------------").
            if (line.All(c => c == '-') || !line.Contains('.'))
            {
                continue;
            }

            var isValidHost = line.All(c => char.IsLetterOrDigit(c) || c is '.' or '-' or '_');

            if (isValidHost && !line.StartsWith('.') && !line.EndsWith('.'))
            {
                entries.Add(line.ToLowerInvariant());
            }
        }

        return entries.OrderBy(e => e, StringComparer.OrdinalIgnoreCase).ToList();
    }
}
