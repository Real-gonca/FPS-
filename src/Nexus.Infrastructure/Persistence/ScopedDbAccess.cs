using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Nexus.Infrastructure.Persistence;

/// <summary>
/// Padrão partilhado pelos stores: os stores são SINGLETON mas o DbContext é
/// SCOPED — cada operação abre o seu scope (evita captive dependency e partilha
/// de contexto entre operações concorrentes da UI/sampler).
/// </summary>
public abstract class ScopedDbAccess
{
    private readonly IServiceScopeFactory _scopes;

    protected ScopedDbAccess(IServiceScopeFactory scopes, ILogger log)
    {
        _scopes = scopes;
        Log = log;
    }

    protected ILogger Log { get; }

    protected async Task<TResult> WithDbAsync<TResult>(Func<NexusDbContext, Task<TResult>> work, CancellationToken ct)
    {
        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NexusDbContext>();
        return await work(db);
    }
}
