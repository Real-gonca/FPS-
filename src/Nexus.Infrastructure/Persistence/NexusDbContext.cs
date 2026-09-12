using Microsoft.EntityFrameworkCore;

namespace Nexus.Infrastructure.Persistence;

/// <summary>
/// Base de dados local (SQLite em %LOCALAPPDATA%\NexusOptimizer\nexus.db):
/// histórico de ações, backups, amostras de telemetria e definições.
/// EnsureCreated() no arranque (schema simples e estável — sem migrations
/// neste estágio; quando o schema evoluir com dados de utilizadores, passam
/// a migrations EF).
/// </summary>
public sealed class NexusDbContext : DbContext
{
    public NexusDbContext(DbContextOptions<NexusDbContext> options)
        : base(options)
    {
    }

    public DbSet<OptimizationActionRow> Actions => Set<OptimizationActionRow>();
    public DbSet<BackupRow> Backups => Set<BackupRow>();
    public DbSet<TelemetrySampleRow> TelemetrySamples => Set<TelemetrySampleRow>();
    public DbSet<SettingsRow> Settings => Set<SettingsRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TelemetrySampleRow>().HasIndex(e => e.TimestampUtc);
        modelBuilder.Entity<OptimizationActionRow>().HasIndex(e => e.StartedUtc);
    }
}
