using Microsoft.EntityFrameworkCore.Utilities.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Microsoft.EntityFrameworkCore.Utilities.Interceptors;

/// <summary>
/// Stamps <see cref="EntityBase"/> timestamps as part of the save itself, so no caller can persist
/// an entity without them.
/// </summary>
public sealed class AuditableTimestampInterceptor(TimeProvider timeProvider) : SaveChangesInterceptor
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result
    )
    {
        Stamp(eventData.Context);

        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default
    )
    {
        Stamp(eventData.Context);

        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void Stamp(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        // One timestamp for the whole save, so everything written together carries the same instant.
        var now = _timeProvider.GetUtcNow();

        foreach (var entry in context.ChangeTracker.Entries<EntityBase>())
        {
            Stamp(entry, now);
        }
    }

    private static void Stamp(EntityEntry<EntityBase> entry, DateTimeOffset now)
    {
        switch (entry.State)
        {
            case EntityState.Added:
                entry.Entity.CreatedAt = now;
                entry.Entity.ModifiedAt = null;
                break;

            case EntityState.Modified:
                entry.Entity.ModifiedAt = now;

                // An update must not be able to rewrite when the row was created.
                entry.Property(entity => entity.CreatedAt).IsModified = false;
                break;
        }
    }
}
