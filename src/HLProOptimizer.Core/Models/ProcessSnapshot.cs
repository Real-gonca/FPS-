using HLProOptimizer.Core.Common;

namespace HLProOptimizer.Core.Models;

/// <summary>Fotografia de um processo, usada nas listas "Top 10" do Monitoramento.</summary>
/// <param name="Id">PID.</param>
/// <param name="Name">Nome do processo (sem extensão).</param>
/// <param name="ExecutablePath">Caminho completo do binário (null quando protegido).</param>
/// <param name="Publisher">Empresa assinante do binário.</param>
/// <param name="MemoryBytes">Working set.</param>
/// <param name="CpuPercent">Uso de CPU no último intervalo.</param>
/// <param name="ThreadCount">Threads.</param>
/// <param name="HandleCount">Handles.</param>
/// <param name="StartedAt">Momento de início do processo.</param>
/// <param name="IsSystemProcess">Se pertence ao Windows (SYSTEM/Local Service).</param>
/// <param name="IsCritical">Se é um processo crítico do SO (não deve ser finalizado).</param>
public sealed record ProcessSnapshot(
    int Id,
    string Name,
    string? ExecutablePath,
    string Publisher,
    long MemoryBytes,
    double CpuPercent,
    int ThreadCount,
    int HandleCount,
    DateTime? StartedAt,
    bool IsSystemProcess,
    bool IsCritical)
{
    /// <summary>Memória formatada para exibição.</summary>
    public string MemoryFormatted => ByteFormat.Format(MemoryBytes, 1);
}
