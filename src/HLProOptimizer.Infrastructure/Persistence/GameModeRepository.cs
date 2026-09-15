using HLProOptimizer.Core.Abstractions;
using HLProOptimizer.Core.Models;
using HLProOptimizer.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace HLProOptimizer.Infrastructure.Persistence;

/// <summary>
/// Repositório do estado persistente do Modo Gamer (linha única no SQLite).
/// </summary>
/// <remarks>
/// O estado é gravado a cada ativação/desativação para que o aplicativo consiga
/// reverter os tweaks mesmo se for fechado abruptamente durante uma sessão de jogo
/// (o <c>GameModeService</c> chama <c>RestoreStateAsync</c> no próximo startup).
/// </remarks>
public sealed class GameModeRepository : IGameModeRepository
{
    private static readonly JsonSerializerSettings SerializerSettings = new()
    {
        Converters = { new Newtonsoft.Json.Converters.StringEnumConverter() },
        NullValueHandling = NullValueHandling.Ignore,
        TypeNameHandling = TypeNameHandling.None
    };

    private readonly IDbContextFactory<HlOptimizerDbContext> _factory;
    private readonly ILogger<GameModeRepository> _logger;

    /// <summary>Cria o repositório.</summary>
    /// <param name="factory">Fábrica de contextos.</param>
    /// <param name="logger">Logger.</param>
    public GameModeRepository(IDbContextFactory<HlOptimizerDbContext> factory, ILogger<GameModeRepository> logger)
    {
        _factory = factory;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task SaveStateAsync(GameModeState state, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);

        try
        {
            await using var context = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

            var existing = await context.GameModeStates
                .FirstOrDefaultAsync(e => e.Id == GameModeStateEntity.SingletonRowId, cancellationToken)
                .ConfigureAwait(false);

            var entity = existing ?? new GameModeStateEntity();

            entity.Id = GameModeStateEntity.SingletonRowId;
            entity.IsActive = state.IsActive;
            entity.ActivatedAt = state.ActivatedAt;
            entity.PreviousPowerPlanGuid = state.PreviousPowerPlanGuid;
            entity.FreedMemoryBytes = state.FreedMemoryBytes;
            entity.StateJson = JsonConvert.SerializeObject(state, SerializerSettings);
            entity.UpdatedAt = DateTime.Now;

            // Primeira gravação: insere a linha única; depois disso o EF só atualiza.
            if (existing is null)
            {
                context.GameModeStates.Add(entity);
            }

            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            _logger.LogDebug(
                "Estado do Modo Gamer salvo (ativo={Active}, {Tweaks} tweak(s), plano anterior={Plan}).",
                state.IsActive,
                state.AppliedTweaks.Count,
                state.PreviousPowerPlanGuid ?? "-");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha ao salvar o estado do Modo Gamer.");
        }
    }

    /// <inheritdoc />
    public async Task<GameModeState?> LoadStateAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await using var context = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

            var entity = await context.GameModeStates
                .AsNoTracking()
                .FirstOrDefaultAsync(e => e.Id == GameModeStateEntity.SingletonRowId, cancellationToken)
                .ConfigureAwait(false);

            if (entity is null)
            {
                return null;
            }

            var state = JsonConvert.DeserializeObject<GameModeState>(entity.StateJson, SerializerSettings);

            if (state is not null)
            {
                return state;
            }

            // JSON ilegível (versão antiga): reconstroi o mínimo a partir das colunas.
            _logger.LogWarning("Estado do Modo Gamer ilegível; usando apenas os campos persistidos em coluna.");

            return new GameModeState
            {
                IsActive = entity.IsActive,
                ActivatedAt = entity.ActivatedAt,
                PreviousPowerPlanGuid = entity.PreviousPowerPlanGuid,
                FreedMemoryBytes = entity.FreedMemoryBytes
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "JSON inválido no estado do Modo Gamer.");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha ao carregar o estado do Modo Gamer.");
            return null;
        }
    }

    /// <inheritdoc />
    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await using var context = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

            var deleted = await context.GameModeStates
                .Where(e => e.Id == GameModeStateEntity.SingletonRowId)
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);

            _logger.LogDebug("Estado do Modo Gamer apagado ({Count} linha(s)).", deleted);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha ao apagar o estado do Modo Gamer.");
        }
    }
}
