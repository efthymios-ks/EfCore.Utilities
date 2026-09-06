using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore.Utilities.ChangeTracking;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;

namespace Microsoft.EntityFrameworkCore.Utilities.Interceptors;

/// <summary>
/// Writes an <see cref="EntityChange"/> row for every entity a save touches. Values are captured
/// before the save, while the old ones still exist, and written after it, once database-generated
/// keys hold real values rather than placeholders.
/// </summary>
public sealed class EntityChangeInterceptor(TimeProvider timeProvider) : SaveChangesInterceptor
{
    /// <summary>
    /// State lives per context rather than in a field, because a single interceptor instance can
    /// be registered once and shared by every context in the application.
    /// </summary>
    private static readonly ConditionalWeakTable<DbContext, SaveState> _states = [];

    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result
    )
    {
        if (eventData.Context is { } context)
        {
            Capture(context).BeginTransactionIfUnowned();
        }

        return base.SavingChanges(eventData, result);
    }

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default
    )
    {
        if (eventData.Context is { } context)
        {
            await Capture(context).BeginTransactionIfUnownedAsync(cancellationToken);
        }

        return await base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        if (eventData.Context is { } context
            && Take(context) is { } state
        )
        {
            Record(context, state);
            context.SaveChanges();

            state.Commit();
        }

        return base.SavedChanges(eventData, result);
    }

    public override async ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData,
        int result,
        CancellationToken cancellationToken = default
    )
    {
        if (eventData.Context is { } context
            && Take(context) is { } state
        )
        {
            Record(context, state);
            await context.SaveChangesAsync(cancellationToken);

            await state.CommitAsync(cancellationToken);
        }

        return await base.SavedChangesAsync(eventData, result, cancellationToken);
    }

    public override void SaveChangesFailed(DbContextErrorEventData eventData)
    {
        Discard(eventData.Context);

        base.SaveChangesFailed(eventData);
    }

    public override Task SaveChangesFailedAsync(
        DbContextErrorEventData eventData,
        CancellationToken cancellationToken = default
    )
    {
        Discard(eventData.Context);

        return base.SaveChangesFailedAsync(eventData, cancellationToken);
    }

    private SaveState Capture(DbContext context)
    {
        // The audit save re-enters here; recording the audit rows would never terminate.
        if (_states.TryGetValue(context, out var existing))
        {
            return existing;
        }

        var state = new SaveState(context, _timeProvider.GetUtcNow());
        _states.Add(context, state);
        state.CapturePendingChanges();

        return state;
    }

    /// <summary>
    /// Returns the state only for the save that created it, so the audit save is a no-op. The
    /// entry is dropped either way: leaving the audit save's empty state behind would make the
    /// context's next save reuse it and record nothing.
    /// </summary>
    private static SaveState? Take(DbContext context)
    {
        if (!_states.TryGetValue(context, out var state))
        {
            return null;
        }

        _states.Remove(context);

        return state.HasPendingChanges ? state : null;
    }

    private static void Discard(DbContext? context)
    {
        if (context is null || !_states.TryGetValue(context, out var state))
        {
            return;
        }

        _states.Remove(context);
        state.Dispose();
    }

    private static void Record(DbContext context, SaveState state)
        => context.Set<EntityChange>().AddRange(state.ToEntityChanges());

    private sealed class SaveState(DbContext context, DateTimeOffset changedAt)
    {
        private readonly List<PendingEntityChange> _pendingChanges = [];
        private IDbContextTransaction? _ownedTransaction;

        public bool HasPendingChanges
            => _pendingChanges.Count > 0;

        public void CapturePendingChanges()
        {
            context.ChangeTracker.DetectChanges();

            foreach (var entry in context.ChangeTracker.Entries())
            {
                // The audit rows are the record of a change, never a change worth recording.
                if (entry.Entity is EntityChange)
                {
                    continue;
                }

                if (PendingEntityChange.Capture(entry) is { } change)
                {
                    _pendingChanges.Add(change);
                }
            }
        }

        /// <summary>
        /// The audit rows go in a second save, so both need one transaction around them or a
        /// failure to record a change would leave the change itself committed. Skipped when the
        /// caller already opened one, which stays theirs to commit.
        /// </summary>
        public void BeginTransactionIfUnowned()
        {
            if (CanOwnTransaction())
            {
                _ownedTransaction = context.Database.BeginTransaction();
            }
        }

        /// <inheritdoc cref="BeginTransactionIfUnowned"/>
        public async Task BeginTransactionIfUnownedAsync(CancellationToken cancellationToken)
        {
            if (CanOwnTransaction())
            {
                _ownedTransaction = await context.Database.BeginTransactionAsync(cancellationToken);
            }
        }

        public IEnumerable<EntityChange> ToEntityChanges()
        {
            foreach (var change in _pendingChanges)
            {
                change.Resolve();
            }

            return _pendingChanges.Select(change => change.ToEntityChange(changedAt));
        }

        public void Commit()
        {
            _ownedTransaction?.Commit();
            Dispose();
        }

        public async Task CommitAsync(CancellationToken cancellationToken)
        {
            if (_ownedTransaction is not null)
            {
                await _ownedTransaction.CommitAsync(cancellationToken);
            }

            Dispose();
        }

        public void Dispose()
        {
            _ownedTransaction?.Dispose();
            _ownedTransaction = null;
        }

        private bool CanOwnTransaction()
            => _ownedTransaction is null
                && HasPendingChanges
                && context.Database.CurrentTransaction is null
                && context.Database.IsRelational();
    }
}
