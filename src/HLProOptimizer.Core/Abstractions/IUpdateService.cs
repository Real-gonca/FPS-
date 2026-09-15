using HLProOptimizer.Core.Models;

namespace HLProOptimizer.Core.Abstractions;

/// <summary>Verificação de atualizações do produto.</summary>
public interface IUpdateService
{
    /// <summary>Versão atualmente instalada.</summary>
    string CurrentVersion { get; }

    /// <summary>Verifica se há uma nova versão disponível.</summary>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default);

    /// <summary>Baixa e instala a atualização (abre o instalador).</summary>
    /// <param name="result">Resultado da verificação.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    Task<bool> InstallAsync(UpdateCheckResult result, CancellationToken cancellationToken = default);

    /// <summary>Informações para a aba "Sobre".</summary>
    AboutInfo GetAboutInfo();
}
