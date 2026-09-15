using HLProOptimizer.Core.Models;

namespace HLProOptimizer.Core.Abstractions;

/// <summary>Ferramentas de reparo do sistema (tela Ferramentas &gt; System Tools).</summary>
public interface ISystemRepairService
{
    /// <summary>Executa o System File Checker (<c>sfc /scannow</c>).</summary>
    /// <param name="progress">Streaming da saída.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    Task<CommandResult> RunSfcScanAsync(IProgress<string>? progress = null, CancellationToken cancellationToken = default);

    /// <summary>Executa <c>DISM /Online /Cleanup-Image /ScanHealth</c>.</summary>
    Task<CommandResult> RunDismScanHealthAsync(IProgress<string>? progress = null, CancellationToken cancellationToken = default);

    /// <summary>Executa <c>DISM /Online /Cleanup-Image /RestoreHealth</c>.</summary>
    Task<CommandResult> RunDismRestoreHealthAsync(IProgress<string>? progress = null, CancellationToken cancellationToken = default);

    /// <summary>Agenda <c>chkdsk</c> para o próximo boot.</summary>
    /// <param name="driveLetter">Letra da unidade (ex.: "C").</param>
    /// <param name="fixErrors">Se deve corrigir erros (/f).</param>
    /// <param name="scanBadSectors">Se deve procurar setores defeituosos (/r).</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    Task<CommandResult> ScheduleChkdskAsync(string driveLetter, bool fixErrors = true, bool scanBadSectors = false, CancellationToken cancellationToken = default);

    /// <summary>Inicia uma verificação do Windows Defender.</summary>
    /// <param name="quickScan">True para verificação rápida, false para completa.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    Task<CommandResult> StartDefenderScanAsync(bool quickScan = true, CancellationToken cancellationToken = default);

    /// <summary>Abre o solucionador de problemas de desempenho do Windows.</summary>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    Task<CommandResult> RunPerformanceTroubleshooterAsync(CancellationToken cancellationToken = default);

    /// <summary>Limpa componentes do Windows Update (<c>DISM /StartComponentCleanup</c>).</summary>
    Task<CommandResult> CleanupWindowsUpdateAsync(IProgress<string>? progress = null, CancellationToken cancellationToken = default);
}
