using Microsoft.EntityFrameworkCore.Utilities.Entities;
using Microsoft.EntityFrameworkCore;

namespace Microsoft.EntityFrameworkCore.Utilities.Extensions;

/// <summary>
/// The set operations <see cref="DbSet{TEntity}"/> leaves out: deciding per entity whether it is
/// new, and doing that decision in one query rather than one per entity.
/// </summary>
public static class DbSetExtensions
{
    public static async Task<IReadOnlyList<TEntity>> AddManyIfMissingAsync<TEntity, TKey>(
        this DbSet<TEntity> dbSet,
        IEnumerable<TEntity> entities,
        CancellationToken cancellationToken = default
    ) where TEntity : EntityBase<TKey>
    {
        ArgumentNullException.ThrowIfNull(dbSet);
        ArgumentNullException.ThrowIfNull(entities);

        var split = await SplitByExistenceAsync<TEntity, TKey>(dbSet, entities, cancellationToken);
        await dbSet.AddRangeAsync(split.Missing, cancellationToken);

        return split.Missing;
    }

    public static async Task<IReadOnlyList<TEntity>> UpdateManyIfPresentAsync<TEntity, TKey>(
        this DbSet<TEntity> dbSet,
        IEnumerable<TEntity> entities,
        CancellationToken cancellationToken = default
    ) where TEntity : EntityBase<TKey>
    {
        ArgumentNullException.ThrowIfNull(dbSet);
        ArgumentNullException.ThrowIfNull(entities);

        var split = await SplitByExistenceAsync<TEntity, TKey>(dbSet, entities, cancellationToken);
        dbSet.UpdateRange(split.Present);

        return split.Present;
    }

    public static async Task<UpsertResult<TEntity>> AddOrUpdateManyAsync<TEntity, TKey>(
        this DbSet<TEntity> dbSet,
        IEnumerable<TEntity> entities,
        CancellationToken cancellationToken = default
    ) where TEntity : EntityBase<TKey>
    {
        ArgumentNullException.ThrowIfNull(dbSet);
        ArgumentNullException.ThrowIfNull(entities);

        var split = await SplitByExistenceAsync<TEntity, TKey>(dbSet, entities, cancellationToken);

        await dbSet.AddRangeAsync(split.Missing, cancellationToken);
        dbSet.UpdateRange(split.Present);

        return new UpsertResult<TEntity>(split.Missing, split.Present);
    }

    /// <summary>False when there was no such row.</summary>
    public static async Task<bool> RemoveByKeyAsync<TEntity, TKey>(
        this DbSet<TEntity> dbSet,
        TKey key,
        CancellationToken cancellationToken = default
    ) where TEntity : EntityBase<TKey>
    {
        ArgumentNullException.ThrowIfNull(dbSet);

        var entity = await dbSet.FindAsync([key], cancellationToken);

        if (entity is null)
        {
            return false;
        }

        dbSet.Remove(entity);

        return true;
    }

    public static Task<bool> ExistsAsync<TEntity, TKey>(
        this DbSet<TEntity> dbSet,
        TKey key,
        CancellationToken cancellationToken = default
    ) where TEntity : EntityBase<TKey>
    {
        ArgumentNullException.ThrowIfNull(dbSet);

        return dbSet
            .AsNoTracking()
            .AnyAsync(entity => Equals(entity.Id, key), cancellationToken);
    }

    /// <summary>
    /// One round trip to find out which of these keys already exist, instead of a query per entity.
    /// </summary>
    private static async Task<ExistenceSplit<TEntity>> SplitByExistenceAsync<TEntity, TKey>(
        DbSet<TEntity> dbSet,
        IEnumerable<TEntity> entities,
        CancellationToken cancellationToken
    ) where TEntity : EntityBase<TKey>
    {
        var candidates = entities.ToArray();

        // An unassigned key cannot match a stored row, so it needs no lookup and is always missing.
        var assignedKeys = candidates
            .Where(entity => !IsUnassigned(entity.Id))
            .Select(entity => entity.Id)
            .ToArray();

        var keysToProbe = assignedKeys
            .Distinct()
            .ToArray();

        if (keysToProbe.Length != assignedKeys.Length)
        {
            // Left to EF this surfaces later as a tracking conflict, naming neither the key nor the caller.
            var error = new ArgumentException(
                message: $"'{typeof(TEntity).Name}' was given more than once for the same key.",
                paramName: nameof(entities)
            );

            throw error;
        }

        var existing = keysToProbe.Length == 0
            ? []
            : (await dbSet
                .AsNoTracking()
                .Where(entity => keysToProbe.Contains(entity.Id))
                .Select(entity => entity.Id)
                .ToArrayAsync(cancellationToken))
                .ToHashSet();

        var missing = new List<TEntity>(candidates.Length);
        var present = new List<TEntity>(candidates.Length);

        foreach (var candidate in candidates)
        {
            if (existing.Contains(candidate.Id))
            {
                present.Add(candidate);
            }
            else
            {
                missing.Add(candidate);
            }
        }

        return new([.. missing], [.. present]);
    }

    private static bool IsUnassigned<TKey>(TKey key)
        => EqualityComparer<TKey>.Default.Equals(key, default);

    private readonly record struct ExistenceSplit<TEntity>(TEntity[] Missing, TEntity[] Present);
}

public sealed record UpsertResult<TEntity>(IReadOnlyList<TEntity> Added, IReadOnlyList<TEntity> Updated);
