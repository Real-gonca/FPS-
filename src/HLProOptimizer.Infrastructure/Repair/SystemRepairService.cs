using HLProOptimizer.Core.Abstractions;
using HLProOptimizer.Core.Models;
using HLProOptimizer.Infrastructure.Platform;
using Microsoft.Extensions.Logging;

namespace HLProOptimizer.Infrastructure.Repair;

/// <summary>
/// Ferramentas nativas de reparo do Windows: SFC, DISM, chkdsk, Defender e o
/// solucionador de problemas de desempenho.
/// </summary>
/// <remarks>
/// <para>
/// Todas essas ferramentas exigem administrador e podem levar de 2 a 40 minutos.
/// A execução é delegada ao <see cref="ElevatedScriptRunner"/>, que pede UAC uma
/// única vez e faz streaming da saída (o usuário vê "Verificação 43% concluída").
/// </para>
/// <para>
/// Timeouts longos são intencionais: um <c>DISM /RestoreHealth</c> em máquina com
/// disco lento passa facilmente de 30 minutos. Cancelar por timeout mataria a
/// operação no meio e poderia corromper o component store.
/// </para>
/// </remarks>
public sealed class SystemRepairService : ISystemRepairService
{
    /// <summary>Tempo máximo do SFC (varredura completa).</summary>
    private static readonly TimeSpan SfcTimeout = TimeSpan.FromMinutes(45);

    /// <summary>Tempo máximo do DISM (pode baixar componentes do Windows Update).</summary>
    private static readonly TimeSpan DismTimeout = TimeSpan.FromMinutes(90);

    /// <summary>Tempo máximo do chkdsk agendado (a execução real ocorre no boot).</summary>
    private static readonly TimeSpan ChkdskTimeout = TimeSpan.FromMinutes(2);

    private readonly ElevatedScriptRunner _runner;
    private readonly ICommandRunner _commands;
    private readonly ILogger<SystemRepairService> _logger;

    /// <summary>Cria o serviço de reparo do sistema.</summary>
    /// <param name="runner">Executor com elevação + streaming.</param>
    /// <param name="commands">Executor de comandos simples.</param>
    /// <param name="logger">Logger.</param>
    public SystemRepairService(ElevatedScriptRunner runner, ICommandRunner commands, ILogger<SystemRepairService> logger)
    {
        _runner = runner;
        _commands = commands;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<CommandResult> RunSfcScanAsync(IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Iniciando sfc /scannow.");
        progress?.Report("Iniciando a verificação de arquivos do sistema (sfc /scannow)...");

        var result = await _runner
            .RunAsync("sfc.exe", "/scannow", progress, SfcTimeout, cancellationToken)
            .ConfigureAwait(false);

        progress?.Report(InterpretSfcResult(result));
        _logger.LogInformation("sfc /scannow finalizado (exit {ExitCode}).", result.ExitCode);

        return result;
    }

    /// <inheritdoc />
    public async Task<CommandResult> RunDismScanHealthAsync(IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Iniciando DISM /ScanHealth.");
        progress?.Report("Analisando a integridade da imagem do Windows (DISM /ScanHealth)...");

        var result = await _runner
            .RunAsync("dism.exe", "/Online /Cleanup-Image /ScanHealth", progress, DismTimeout, cancellationToken)
            .ConfigureAwait(false);

        progress?.Report(result.IsSuccess
            ? "Análise da imagem concluída."
            : $"Análise da imagem terminou com o código {result.ExitCode}.");

        return result;
    }

    /// <inheritdoc />
    public async Task<CommandResult> RunDismRestoreHealthAsync(IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Iniciando DISM /RestoreHealth.");
        progress?.Report("Reparando a imagem do Windows (DISM /RestoreHealth). Isso pode levar vários minutos...");

        var result = await _runner
            .RunAsync("dism.exe", "/Online /Cleanup-Image /RestoreHealth", progress, DismTimeout, cancellationToken)
            .ConfigureAwait(false);

        progress?.Report(result.IsSuccess
            ? "Reparo da imagem concluído com sucesso."
            : $"O reparo terminou com o código {result.ExitCode}. Verifique o log em C:\\Windows\\Logs\\DISM.");

        return result;
    }

    /// <inheritdoc />
    public async Task<CommandResult> ScheduleChkdskAsync(
        string driveLetter,
        bool fixErrors = true,
        bool scanBadSectors = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(driveLetter);

        var letter = driveLetter.Trim().TrimEnd(':', '\\');

        if (letter.Length != 1 || !char.IsLetter(letter[0]))
        {
            throw new ArgumentException("A letra da unidade deve ser um único caractere (ex.: \"C\").", nameof(driveLetter));
        }

        var switches = (fixErrors ? " /f" : string.Empty) + (scanBadSectors ? " /r" : string.Empty);

        // /x força a desmontagem do volume (necessário para volumes em uso).
        var arguments = $"{char.ToUpperInvariant(letter[0])}:{switches} /x";

        _logger.LogInformation("Agendando chkdsk para {Drive}: (flags: fix={Fix}, sectors={Sectors}).", letter, fixErrors, scanBadSectors);

        // echo Y responde "Deseja agendar para a próxima reinicialização?".
        var result = await _runner
            .RunAsync("cmd.exe", $"/c echo Y| chkdsk {arguments}", null, ChkdskTimeout, cancellationToken)
            .ConfigureAwait(false);

        return result;
    }

    /// <inheritdoc />
    public async Task<CommandResult> StartDefenderScanAsync(bool quickScan = true, CancellationToken cancellationToken = default)
    {
        var scanType = quickScan ? 1 : 2;

        _logger.LogInformation("Iniciando verificação do Windows Defender (tipo {ScanType}).", scanType);

        // MpCmdRun sai imediatamente; a varredura continua em segundo plano.
        var result = await _commands
            .RunAsync(
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"Windows Defender\MpCmdRun.exe"),
                $"-Scan -ScanType {scanType}",
                cancellationToken,
                timeout: TimeSpan.FromMinutes(5))
            .ConfigureAwait(false);

        if (!result.IsSuccess)
        {
            // Caminho alternativo no Windows 11 (plataforma antimalware).
            result = await _commands
                .RunAsync("MpCmdRun.exe", $"-Scan -ScanType {scanType}", cancellationToken, timeout: TimeSpan.FromMinutes(5))
                .ConfigureAwait(false);
        }

        return result;
    }

    /// <inheritdoc />
    public async Task<CommandResult> RunPerformanceTroubleshooterAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Abrindo o solucionador de problemas de desempenho.");

        // msdt.exe com o pacote de diagnóstico de desempenho; abre a UI do Windows.
        var result = await _commands
            .RunAsync(
                "msdt.exe",
                "/id PerformanceDiagnostic /skip Force",
                cancellationToken,
                timeout: TimeSpan.FromSeconds(30))
            .ConfigureAwait(false);

        if (!result.IsSuccess)
        {
            _logger.LogWarning(
                "msdt.exe indisponível (removido em builds recentes). Abrindo as configurações de solução de problemas. Saída: {Output}",
                result.CombinedOutput);

            // Fallback: abre a página de solução de problemas das Configurações.
            result = await _commands
                .RunAsync("ms-settings.exe", "troubleshoot", cancellationToken, timeout: TimeSpan.FromSeconds(30))
                .ConfigureAwait(false);
        }

        return result;
    }

    /// <inheritdoc />
    public async Task<CommandResult> CleanupWindowsUpdateAsync(IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Iniciando DISM /StartComponentCleanup.");
        progress?.Report("Limpando componentes substituídos do Windows Update. Isso pode levar vários minutos...");

        var result = await _runner
            .RunAsync("dism.exe", "/Online /Cleanup-Image /StartComponentCleanup", progress, DismTimeout, cancellationToken)
            .ConfigureAwait(false);

        progress?.Report(result.IsSuccess
            ? "Limpeza de componentes concluída."
            : $"A limpeza terminou com o código {result.ExitCode}.");

        return result;
    }

    /// <summary>Traduz a saída do SFC em uma mensagem compreensível em PT-BR.</summary>
    /// <remarks>
    /// O SFC escreve mensagens localizadas; reconhecemos os padrões em PT-BR e EN
    /// para não depender do idioma do sistema.
    /// </remarks>
    private string InterpretSfcResult(CommandResult result)
    {
        var output = result.CombinedOutput;

        if (result.ExitCode == 1223)
        {
            return "Elevação recusada: a verificação não foi executada.";
        }

        if (Contains(output, "did not find any integrity violations", "não encontrou nenhuma violação de integridade"))
        {
            return "Verificação concluída: nenhum arquivo corrompido encontrado.";
        }

        if (Contains(output, "found corrupt files and successfully repaired", "encontrou arquivos corrompidos e conseguiu repará-los"))
        {
            return "Verificação concluída: arquivos corrompidos foram reparados. Reinicie o computador.";
        }

        if (Contains(output, "found corrupt files but was unable to fix", "encontrou arquivos corrompidos, mas não conseguiu corrigi-los"))
        {
            return "Arquivos corrompidos não puderam ser reparados. Execute DISM /RestoreHealth e repita a verificação.";
        }

        if (Contains(output, "Windows Resource Protection could not perform", "não pôde executar a operação solicitada"))
        {
            return "A verificação não pôde ser executada. Reinicie o computador e tente novamente.";
        }

        return result.IsSuccess
            ? "Verificação concluída."
            : $"Verificação encerrada com o código {result.ExitCode}. Veja C:\\Windows\\Logs\\CBS\\CBS.log.";
    }

    private static bool Contains(string haystack, params string[] needles) =>
        needles.Any(needle => haystack.Contains(needle, StringComparison.OrdinalIgnoreCase));
}
