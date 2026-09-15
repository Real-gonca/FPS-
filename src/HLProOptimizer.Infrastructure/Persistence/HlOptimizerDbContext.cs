using HLProOptimizer.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HLProOptimizer.Infrastructure.Persistence;

/// <summary>
/// Contexto EF Core do aplicativo (SQLite).
/// </summary>
/// <remarks>
/// <para>
/// <b>Por que <c>IDbContextFactory</c> nos repositórios?</b> <see cref="DbContext"/>
/// não é thread-safe. O aplicativo tem várias telas gravando em paralelo
/// (histórico de ações, relatórios de scan, estado do Modo Gamer); com uma fábrica,
/// cada operação usa um contexto próprio e curto, eliminando a classe inteira de
/// erros "A second operation started on this context".
/// </para>
/// <para>
/// <b>Schema</b> é criado com <c>EnsureCreatedAsync</c> no startup
/// (ver <see cref="HlOptimizerDatabaseInitializer"/>): o banco é local, descartável e não há
/// requisito de migração de dados entre versões — migrar seria complexidade sem
/// benefício. Se o schema mudar, o arquivo antigo é renomeado e recriado.
/// </para>
/// </remarks>
public class HlOptimizerDbContext : DbContext
{
    /// <summary>Cria o contexto com as opções fornecidas (UseSqlite).</summary>
    /// <param name="options">Opções do contexto.</param>
    public HlOptimizerDbContext(DbContextOptions<HlOptimizerDbContext> options)
        : base(options)
    {
    }

    /// <summary>Histórico de ações do usuário.</summary>
    public DbSet<ActionRecordEntity> ActionRecords => Set<ActionRecordEntity>();

    /// <summary>Relatórios de análise.</summary>
    public DbSet<ScanReportEntity> ScanReports => Set<ScanReportEntity>();

    /// <summary>Estado persistente do Modo Gamer (linha única).</summary>
    public DbSet<GameModeStateEntity> GameModeStates => Set<GameModeStateEntity>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<ActionRecordEntity>(entity =>
        {
            entity.ToTable("ActionRecords");
            entity.HasKey(e => e.Id);

            // Consultas típicas: "últimas N ações" e "ações por tipo".
            entity.HasIndex(e => e.Timestamp).HasDatabaseName("IX_ActionRecords_Timestamp");
            entity.HasIndex(e => e.Kind).HasDatabaseName("IX_ActionRecords_Kind");

            entity.Property(e => e.Title).HasMaxLength(256).IsRequired();
            entity.Property(e => e.Details).HasMaxLength(8192);
            entity.Property(e => e.Kind).HasConversion<int>();
        });

        modelBuilder.Entity<ScanReportEntity>(entity =>
        {
            entity.ToTable("ScanReports");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.StartedAt).HasDatabaseName("IX_ScanReports_StartedAt");

            entity.Property(e => e.IssuesJson).IsRequired();
            entity.Property(e => e.FailedCategories).HasMaxLength(2048);
        });

        modelBuilder.Entity<GameModeStateEntity>(entity =>
        {
            entity.ToTable("GameModeStates");
            entity.HasKey(e => e.Id);

            // Linha única: sem necessidade de índice adicional.
            entity.Property(e => e.PreviousPowerPlanGuid).HasMaxLength(64);
            entity.Property(e => e.StateJson).IsRequired();
        });
    }
}

/// <summary>Inicialização do banco de dados local.</summary>
public static class HlOptimizerDatabaseInitializer
{
    /// <summary>
    /// Garante que o banco exista e esteja no schema atual.
    /// </summary>
    /// <remarks>
    /// Quando o schema muda entre versões, <c>EnsureCreated</c> não altera tabelas
    /// existentes. Nesse caso apagamos e recriamos o arquivo: histórico local não é
    /// dado crítico, e um banco incompatível jamais pode impedir o aplicativo de abrir.
    /// </remarks>
    /// <param name="context">Contexto.</param>
    /// <param name="logger">Logger usado para registrar a recriação.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    public static async Task InitializeAsync(
        this HlOptimizerDbContext context,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await context.Database.EnsureCreatedAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Banco de dados incompatível ou corrompido; recriando do zero.");

            await context.Database.EnsureDeletedAsync(cancellationToken).ConfigureAwait(false);
            await context.Database.EnsureCreatedAsync(cancellationToken).ConfigureAwait(false);

            logger.LogInformation("Banco de dados recriado com sucesso.");
        }
    }
}
