using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Nexus.Infrastructure.Persistence;

/// <summary>Garante o schema SQLite no arranque (EnsureCreated).</summary>
public sealed class DatabaseInitializer : IHostedService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<DatabaseInitializer> _log;

    public DatabaseInitializer(IServiceScopeFactory scopes, ILogger<DatabaseInitializer> log)
    {
        _scopes = scopes;
        _log = log;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NexusDbContext>();
        db.Database.EnsureCreated();
        _log.LogInformation("Base de dados SQLite pronta.");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
