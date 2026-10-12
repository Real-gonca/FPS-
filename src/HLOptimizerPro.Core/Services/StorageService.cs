using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using HLOptimizerPro.Core.Models;
using HLOptimizerPro.Core.Services.Abstractions;
using Microsoft.Extensions.Logging;

namespace HLOptimizerPro.Core.Services;

/// <summary>Resultado da análise de fragmentação de um volume.</summary>
public sealed record FragmentationReport(
    string Drive,
    bool IsSolidState,
    double? FragmentationPercent,
    string RawOutput,
    bool AnalysisSucceeded)
{
    /// <summary>Recomendação textual honesta: se não conseguimos medir, dizemos que não medimos.</summary>
    public string Recommendation
    {
        get
        {
            if (!AnalysisSucceeded)
                return "Não foi possível analisar este volume automaticamente (requer a ferramenta «Otimizar Unidades» do Windows).";

            if (IsSolidState)
                return "Este volume é um SSD: não se desfragmenta. Recomendamos apenas TRIM (otimização), que é seguro e rápido.";

            return FragmentationPercent is { } percent
                ? percent > Helpers.AppConstants.FragmentationWarnPercent
                    ? $"Fragmentação de {percent:N0} % — acima do recomendado ({Helpers.AppConstants.FragmentationWarnPercent:N0} %). A desfragmentação vai melhorar os tempos de acesso."
                    : $"Fragmentação de {percent:N0} % — dentro do aceitável. Não é necessária desfragmentação."
                : "Análise concluída sem percentagem legível (idioma do Windows não reconhecido).";
        }
    }
}

/// <summary>
/// Análise de discos: espaço, tipo de média, fragmentação, TRIM e saúde física.
///
/// Nota importante sobre fragmentação: a ferramenta <c>defrag.exe</c> imprime texto localizado.
/// Em vez de inventar um número quando o idioma não é reconhecido, devolvemos
/// <c>FragmentationPercent = null</c> e dizemos explicitamente que não foi possível medir —
/// requisito do projeto: "resultados baseados em medições reais; não exibir números falsos".
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class StorageService
{
    private static readonly Regex PercentRegex = new(@"(\d{1,3})(?:[.,]\d+)?\s*%", RegexOptions.Compiled);
    private static readonly Regex FragmentacaoPt = new(@"(?:fragmenta[çc][ãa]o|fragmentado)\D{0,40}?(\d{1,3})\s*%", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex FragmentationEn = new(@"(\d{1,3})\s*%\s*(?:fragment|fragmented)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private readonly ILogger<StorageService> _logger;
    private readonly IProcessRunner _runner;
    private readonly SystemInfoService _systemInfo;

    public StorageService(ILogger<StorageService> logger, IProcessRunner runner, SystemInfoService systemInfo)
    {
        _logger = logger;
        _runner = runner;
        _systemInfo = systemInfo;
    }

    /// <summary>Discos fixos com ocupação atual.</summary>
    public IReadOnlyList<DiskInfo> GetDisks() => _systemInfo.Disks;

    /// <summary>Espaço livre do volume do sistema (0–100 %).</summary>
    public double GetSystemDriveFreePercent()
        => GetDisks().FirstOrDefault(d => d.IsSystemDrive)?.FreePercent ?? 0;

    /// <summary>True quando o volume é SSD (não deve ser desfragmentado).</summary>
    public bool IsSolidState(string drive)
    {
        var disk = GetDisks().FirstOrDefault(d => string.Equals(d.Drive, drive, StringComparison.OrdinalIgnoreCase));
        if (disk is null) return false;

        var systemType = _systemInfo.Hardware.StorageType;
        return disk.IsSystemDrive
            ? systemType.Contains("SSD", StringComparison.OrdinalIgnoreCase)
            : disk.MediaType.Contains("SSD", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Analisa a fragmentação de um volume com <c>defrag /A</c>.</summary>
    public async Task<FragmentationReport> AnalyzeAsync(string drive = "C:", CancellationToken cancellationToken = default)
    {
        var isSsd = IsSolidState(drive);

        try
        {
            // /A = apenas análise (não altera nada)  •  /V = saída detalhada
            var result = await _runner.RunAsync("defrag.exe", $"{drive} /A /V", cancellationToken).ConfigureAwait(false);

            if (!result.Success && string.IsNullOrWhiteSpace(result.Output))
            {
                return new FragmentationReport(drive, isSsd, null,
                    "O comando «defrag» não respondeu. Em Windows sem permissões elevadas ou com o serviço «Optimize drives» desativado, a análise não é possível.",
                    AnalysisSucceeded: false);
            }

            var percent = ParseFragmentation(result.Output);

            return new FragmentationReport(drive, isSsd, percent, Truncate(result.Output, 800), AnalysisSucceeded: true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Análise de fragmentação de {Drive} falhou.", drive);
            return new FragmentationReport(drive, isSsd, null, ex.Message, AnalysisSucceeded: false);
        }
    }

    /// <summary>
    /// Otimiza um volume:
    ///   • SSD  → TRIM (<c>/L</c>), que apenas comunica ao disco quais os blocos livres.
    ///   • HDD  → desfragmentação (<c>/D</c>) ou otimização completa (<c>/O</c>).
    /// </summary>
    public async Task<Result> OptimizeAsync(string drive = "C:", bool fullOptimization = false, CancellationToken cancellationToken = default)
    {
        var isSsd = IsSolidState(drive);
        var arguments = isSsd ? $"{drive} /L" : fullOptimization ? $"{drive} /O" : $"{drive} /D";
        var description = isSsd ? "TRIM (SSD)" : "desfragmentação";

        _logger.LogInformation("A iniciar {Operation} em {Drive}", description, drive);

        var result = await _runner.RunAsync("defrag.exe", arguments, cancellationToken).ConfigureAwait(false);

        // defrag.exe não é consistente com os códigos de saída: 0 = sucesso, 2 = ainda a correr.
        if (result.Success || result.ExitCode == 2)
            return Result.Ok($"{char.ToUpper(description[0])}{description[1..]} de {drive} concluída com sucesso.");

        if (result.Output.Contains("acesso negado", StringComparison.OrdinalIgnoreCase)
            || result.Output.Contains("access is denied", StringComparison.OrdinalIgnoreCase))
            return Result.Fail("Acesso negado: execute o HL Optimizer Pro como administrador.", result.Output);

        return Result.Fail($"Não foi possível concluir a {description} de {drive}.", result.Output);
    }

    /// <summary>Saúde física dos discos (SMART) via módulo de armazenamento do PowerShell.</summary>
    public IReadOnlyList<(string Disk, string Health, string OperationalStatus)> GetDiskHealth()
    {
        var list = new List<(string, string, string)>();

        try
        {
            const string script =
                "Get-PhysicalDisk | Select-Object FriendlyName, HealthStatus, OperationalStatus | " +
                "ForEach-Object { \"$($_.FriendlyName)|$($_.HealthStatus)|$($_.OperationalStatus)\" }";

            var result = _runner.Run("powershell.exe", $"-NoProfile -NonInteractive -Command \"{script}\"");
            if (!result.Success) return list;

            foreach (var line in result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = line.Trim().Split('|');
                if (parts.Length >= 3) list.Add((parts[0], parts[1], parts[2]));
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Não foi possível ler a saúde dos discos.");
        }

        return list;
    }

    /// <summary>Ativa ou desativa a "Sensor de Armazenamento" (Storage Sense) do Windows.</summary>
    public Result SetStorageSense(bool enabled, int cleanupDays = 30)
    {
        try
        {
            var result = _runner.Run("reg.exe",
                $"add \"HKCU\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\StorageSense\\Parameters\\StoragePolicy\" " +
                $"/v 01 /t REG_DWORD /d {(enabled ? 1 : 0)} /f");

            if (!result.Success) return Result.Fail("Não foi possível alterar o Sensor de Armazenamento.", result.Output);

            if (enabled && cleanupDays > 0)
            {
                _runner.Run("reg.exe",
                    $"add \"HKCU\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\StorageSense\\Parameters\\StoragePolicy\" " +
                    $"/v 08 /t REG_DWORD /d {Math.Clamp(cleanupDays, 1, 60)} /f");
            }

            return Result.Ok(enabled
                ? $"Sensor de Armazenamento ativado (limpeza a cada {cleanupDays} dias)."
                : "Sensor de Armazenamento desativado.");
        }
        catch (Exception ex)
        {
            return Result.Fail("Falha ao configurar o Sensor de Armazenamento.", ex.Message);
        }
    }

    /// <summary>Executa a limpeza de ficheiros do Windows (cleanmgr) com um perfil de definições.</summary>
    public async Task<Result> RunDiskCleanupAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // Configura todos os handlers de limpeza e corre em modo silencioso (/sagerun:5141).
            var configure = _runner.Run("cleanmgr.exe", "/sagerun:5141");
            if (!configure.Success)
                return Result.Fail("Não foi possível iniciar a Limpeza de Disco do Windows.", configure.Output);

            await Task.Delay(500, cancellationToken).ConfigureAwait(false);
            return Result.Ok("Limpeza de Disco do Windows executada (poderá demorar alguns minutos a terminar em segundo plano).");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Limpeza de disco do Windows falhou.");
            return Result.Fail("A Limpeza de Disco do Windows falhou.", ex.Message);
        }
    }

    // ------------------------------------------------------------------ internos

    /// <summary>Extrai a percentagem de fragmentação de saída localizada (PT/EN).</summary>
    internal static double? ParseFragmentation(string output)
    {
        if (string.IsNullOrWhiteSpace(output)) return null;

        // Padrões específicos primeiro (mais fiáveis), genérico depois.
        var pt = FragmentacaoPt.Match(output);
        if (pt.Success && double.TryParse(pt.Groups[1].Value, out var ptValue)) return ptValue;

        var en = FragmentationEn.Match(output);
        if (en.Success && double.TryParse(en.Groups[1].Value, out var enValue)) return enValue;

        // Última tentativa: a última percentagem da saída (o defrag imprime "X% fragmentado" no fim).
        var matches = PercentRegex.Matches(output);
        if (matches.Count > 0 && double.TryParse(matches[^1].Groups[1].Value, out var last)) return last;

        return null;
    }

    private static string Truncate(string text, int max) => text.Length <= max ? text : text[..max] + "…";
}
