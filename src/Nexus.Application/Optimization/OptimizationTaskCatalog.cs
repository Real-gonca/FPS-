using Nexus.Domain.Optimization;

namespace Nexus.Application.Optimization;

public interface IOptimizationTaskCatalog
{
    IOptimizationTask? Find(string key);

    bool Has(string key);

    IReadOnlyCollection<IOptimizationTask> All { get; }
}

/// <summary>
/// Catálogo de tarefas disponíveis. Cada tarefa é registada na DI
/// (AddSingleton&lt;IOptimizationTask, T&gt;) e entra no catálogo
/// automaticamente — os patches seguintes apenas registam as suas tarefas.
/// </summary>
public sealed class OptimizationTaskCatalog : IOptimizationTaskCatalog
{
    private readonly Dictionary<string, IOptimizationTask> _byKey;

    public OptimizationTaskCatalog(IEnumerable<IOptimizationTask> tasks)
    {
        _byKey = tasks.ToDictionary(t => t.Key, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyCollection<IOptimizationTask> All => _byKey.Values;

    public IOptimizationTask? Find(string key) =>
        _byKey.TryGetValue(key, out var task) ? task : null;

    public bool Has(string key) => _byKey.ContainsKey(key);
}
