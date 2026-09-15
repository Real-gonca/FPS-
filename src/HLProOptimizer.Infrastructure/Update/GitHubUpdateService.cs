using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using HLProOptimizer.Core.Abstractions;
using HLProOptimizer.Core.Enums;
using HLProOptimizer.Core.Models;
using HLProOptimizer.Infrastructure.Logging;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace HLProOptimizer.Infrastructure.Update;

/// <summary>
/// Verificação e instalação de atualizações a partir dos Releases do GitHub.
/// </summary>
/// <remarks>
/// <para>
/// <b>Fonte da verdade</b>: <c>GET /repos/{owner}/{repo}/releases/latest</c> da API
/// pública do GitHub (sem token, com User-Agent obrigatório). A tag da release é
/// comparada com a versão do assembly usando regras de versão semântica — nunca
/// comparação de strings, senão "1.10.0" &lt; "1.9.0".
/// </para>
/// <para>
/// <b>Canal</b>: releases marcadas como <c>prerelease</c> só entram quando o canal
/// configurado é <see cref="UpdateChannel.Beta"/>.
/// </para>
/// <para>
/// <b>Instalação</b>: baixa o asset para a pasta temporária e abre o instalador
/// (elevado). Falha de rede nunca lança exceção: devolve
/// <see cref="UpdateCheckResult.Failed"/> para a UI explicar ao usuário.
/// </para>
/// </remarks>
public sealed class GitHubUpdateService : IUpdateService
{
    /// <summary>Proprietário do repositório de releases.</summary>
    private const string RepositoryOwner = "Real-gonca";

    /// <summary>Nome do repositório de releases.</summary>
    private const string RepositoryName = "FPS-";

    /// <summary>Nome do produto exibido na aba "Sobre".</summary>
    private const string ProductName = "HL PRO OPTIMIZER";

    /// <summary>Licença do projeto.</summary>
    private const string License = "MIT";

    private const string ReleasesUrl = $"https://api.github.com/repos/{RepositoryOwner}/{RepositoryName}/releases";

    private readonly HttpClient _httpClient;
    private readonly ISettingsService _settings;
    private readonly ISystemPaths _paths;
    private readonly ILogger<GitHubUpdateService> _logger;

    /// <summary>Cria o serviço de atualizações.</summary>
    /// <param name="httpClient">Cliente HTTP (singleton, timeout configurado no DI).</param>
    /// <param name="settings">Configurações (canal de atualização e data da última verificação).</param>
    /// <param name="paths">Caminhos do aplicativo (log/banco para a aba "Sobre").</param>
    /// <param name="logger">Logger.</param>
    public GitHubUpdateService(
        HttpClient httpClient,
        ISettingsService settings,
        ISystemPaths paths,
        ILogger<GitHubUpdateService> logger)
    {
        _httpClient = httpClient;
        _settings = settings;
        _paths = paths;
        _logger = logger;
    }

    /// <inheritdoc />
    public string CurrentVersion => GetAssemblyVersion().ToString();

    /// <inheritdoc />
    public async Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        var current = GetAssemblyVersion();
        var channel = _settings.Current.UpdateChannel;

        try
        {
            // Sem releases publicadas, /releases/latest retorna 404: consultamos a lista
            // e filtramos pelo canal, o que também permite releases "beta".
            using var request = new HttpRequestMessage(HttpMethod.Get, ReleasesUrl);

            // A API do GitHub exige User-Agent.
            request.Headers.TryAddWithoutValidation("User-Agent", $"{ProductName}/{current}");
            request.Headers.TryAddWithoutValidation("Accept", "application/vnd.github+json");

            using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                var error = $"GitHub respondeu {(int)response.StatusCode} ({response.ReasonPhrase}).";

                _logger.LogWarning("Verificação de atualização falhou: {Error}", error);

                return UpdateCheckResult.Failed(CurrentVersion, error);
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var releases = JArray.Parse(json);

            var candidate = SelectRelease(releases, channel);

            await RecordCheckAsync(cancellationToken).ConfigureAwait(false);

            if (candidate is null)
            {
                _logger.LogInformation("Nenhuma release disponível para o canal {Channel}.", channel);

                return UpdateCheckResult.UpToDate(CurrentVersion);
            }

            var latestVersion = ParseVersion(candidate.Value<string>("tag_name"));
            var downloadUrl = SelectAssetUrl(candidate);
            var notes = candidate.Value<string>("body") ?? string.Empty;

            if (latestVersion is null || latestVersion <= current)
            {
                _logger.LogInformation("Versão atual {Current} já é a mais recente ({Latest}).", current, latestVersion);

                return UpdateCheckResult.UpToDate(CurrentVersion);
            }

            _logger.LogInformation("Atualização disponível: {Current} → {Latest}.", current, latestVersion);

            return new UpdateCheckResult(true, CurrentVersion, latestVersion.ToString(), notes, downloadUrl, DateTime.Now);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Timeout do HttpClient: não é cancelamento do usuário.
            _logger.LogWarning("Verificação de atualização expirou (timeout de rede).");

            return UpdateCheckResult.Failed(CurrentVersion, "A verificação expirou (tempo limite de rede).");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Falha de rede ao verificar atualizações.");

            return UpdateCheckResult.Failed(CurrentVersion, $"Sem conexão com o GitHub: {ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha inesperada ao verificar atualizações.");

            return UpdateCheckResult.Failed(CurrentVersion, ex.Message);
        }
    }

    /// <inheritdoc />
    public async Task<bool> InstallAsync(UpdateCheckResult result, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (string.IsNullOrWhiteSpace(result.DownloadUrl))
        {
            // Sem asset: abre a página da release no navegador.
            OpenInBrowser($"{ReleasesUrl.Replace("api.github.com/repos", "github.com", StringComparison.OrdinalIgnoreCase)}/latest");

            _logger.LogWarning("Release sem asset de download; abrindo a página de releases.");

            return false;
        }

        try
        {
            var fileName = Path.GetFileName(new Uri(result.DownloadUrl).AbsolutePath);

            if (string.IsNullOrWhiteSpace(fileName))
            {
                fileName = $"HLProOptimizer-{result.LatestVersion}.exe";
            }

            var destination = Path.Combine(_paths.TempDirectory, fileName);

            _logger.LogInformation("Baixando a atualização {Url} para {Destination}.", result.DownloadUrl, destination);

            using (var download = await _httpClient.GetAsync(result.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false))
            {
                download.EnsureSuccessStatusCode();

                await using var source = await download.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                await using var target = File.Create(destination);

                await source.CopyToAsync(target, cancellationToken).ConfigureAwait(false);
            }

            // Instaladores (.exe/.msi) rodam elevados; .zip é apenas aberto.
            if (fileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                OpenInBrowser(destination);
                return true;
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = destination,
                UseShellExecute = true,
                Verb = "runas",
                WorkingDirectory = Path.GetDirectoryName(destination) ?? _paths.TempDirectory
            };

            using var process = Process.Start(startInfo);

            if (process is null)
            {
                _logger.LogError("Não foi possível iniciar o instalador {File}.", destination);
                return false;
            }

            _logger.LogInformation("Instalador {File} iniciado (PID {Pid}).", destination, process.Id);

            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            _logger.LogWarning("Instalação cancelada pelo usuário (UAC recusado).");
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha ao baixar/instalar a atualização.");
            return false;
        }
    }

    /// <inheritdoc />
    public AboutInfo GetAboutInfo()
    {
        var assembly = Assembly.GetEntryAssembly() ?? typeof(GitHubUpdateService).Assembly;

        return new AboutInfo(
            ProductName,
            CurrentVersion,
            GetBuildDate(assembly),
            License,
            RuntimeInformation.FrameworkDescription,
            SerilogBootstrap.GetActiveLogFilePath(_paths),
            _paths.DatabasePath);
    }

    // ---------------------------------------------------------------------
    // Internos
    // ---------------------------------------------------------------------

    /// <summary>Escolhe a release adequada ao canal configurado.</summary>
    private static JObject? SelectRelease(JArray releases, UpdateChannel channel)
    {
        var candidates = releases
            .OfType<JObject>()
            .Where(release => !(release.Value<bool?>("draft") ?? false))
            .Where(release => channel == UpdateChannel.Beta || !(release.Value<bool?>("prerelease") ?? false));

        return candidates
            .Select(release => (Release: release, Version: ParseVersion(release.Value<string>("tag_name"))))
            .Where(item => item.Version is not null)
            .OrderByDescending(item => item.Version)
            .Select(item => item.Release)
            .FirstOrDefault();
    }

    /// <summary>Seleciona o asset de instalação (preferindo .exe do Windows x64).</summary>
    private static string? SelectAssetUrl(JObject release)
    {
        var assets = release["assets"] as JArray;

        if (assets is null || assets.Count == 0)
        {
            return null;
        }

        var candidates = assets
            .OfType<JObject>()
            .Select(asset => asset.Value<string>("browser_download_url"))
            .Where(url => !string.IsNullOrWhiteSpace(url))
            .ToList();

        return candidates.FirstOrDefault(url => url.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            ?? candidates.FirstOrDefault(url => url.EndsWith(".msi", StringComparison.OrdinalIgnoreCase))
            ?? candidates.FirstOrDefault(url => url.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            ?? candidates.FirstOrDefault();
    }

    /// <summary>Converte "v1.2.3-beta" em <see cref="Version"/>.</summary>
    private static Version? ParseVersion(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
        {
            return null;
        }

        var normalized = tag.Trim().TrimStart('v', 'V');

        // Remove sufixos semânticos (-beta.1, +build) que Version não entende.
        var cut = normalized.IndexOfAny(['-', '+']);

        if (cut > 0)
        {
            normalized = normalized[..cut];
        }

        var parts = normalized.Split('.', StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length == 0)
        {
            return null;
        }

        // Version exige no mínimo major.minor.
        if (parts.Length == 1)
        {
            normalized = $"{normalized}.0";
        }

        return Version.TryParse(normalized, out var version) ? version : null;
    }

    /// <summary>Versão do assembly em execução.</summary>
    private static Version GetAssemblyVersion()
    {
        var assembly = Assembly.GetEntryAssembly() ?? typeof(GitHubUpdateService).Assembly;

        return assembly.GetName().Version ?? new Version(1, 0, 0);
    }

    /// <summary>Data de compilação do assembly (fallback: data do arquivo).</summary>
    private static DateTime GetBuildDate(Assembly assembly)
    {
        try
        {
            var location = assembly.Location;

            if (!string.IsNullOrEmpty(location) && File.Exists(location))
            {
                return File.GetLastWriteTime(location);
            }
        }
        catch (Exception)
        {
            // Assembly carregado de stream/single-file: sem caminho físico.
        }

        return new DateTime(2000, 1, 1).AddDays(assembly.GetName().Version?.Build ?? 0);
    }

    /// <summary>Registra a data da última verificação nas configurações.</summary>
    private async Task RecordCheckAsync(CancellationToken cancellationToken)
    {
        try
        {
            var settings = _settings.Current.Clone();

            settings.LastUpdateCheck = DateTime.Now;

            await _settings.SaveAsync(settings, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Não foi possível registrar a data da última verificação.");
        }
    }

    /// <summary>Abre uma URL/arquivo no navegador padrão.</summary>
    private void OpenInBrowser(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Não foi possível abrir {Url} no navegador.", url);
        }
    }
}
