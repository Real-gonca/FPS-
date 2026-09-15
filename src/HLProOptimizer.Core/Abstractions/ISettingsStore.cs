using HLProOptimizer.Core.Models;

namespace HLProOptimizer.Core.Abstractions;

/// <summary>
/// Persistência bruta das configurações (JSON em %AppData%). Isolada do serviço
/// para permitir testes com um store em memória.
/// </summary>
public interface ISettingsStore
{
    /// <summary>Carrega as configurações ou retorna null quando não existem.</summary>
    Task<AppSettings?> LoadAsync(CancellationToken cancellationToken = default);

    /// <summary>Salva as configurações.</summary>
    Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default);

    /// <summary>Apaga as configurações persistidas.</summary>
    Task DeleteAsync(CancellationToken cancellationToken = default);
}
