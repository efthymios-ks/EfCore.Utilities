using Microsoft.EntityFrameworkCore.Utilities.ChangeTracking;
using Microsoft.EntityFrameworkCore.Utilities.Interceptors;
using Microsoft.EntityFrameworkCore;

namespace Microsoft.EntityFrameworkCore.Utilities.DbContexts;

/// <summary>
/// A context with the audit interceptors already wired up. The interceptors work on any
/// <see cref="DbContext"/>; this only saves you registering them and mapping
/// <see cref="EntityChange"/> by hand.
/// </summary>
public abstract class AuditableDbContext(DbContextOptions options, TimeProvider timeProvider)
    : DbContext(options)
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    protected AuditableDbContext(DbContextOptions options)
        : this(options, TimeProvider.System)
    {
    }

    public DbSet<EntityChange> EntityChanges
        => Set<EntityChange>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        base.OnConfiguring(optionsBuilder);

        optionsBuilder.AddInterceptors(
            new AuditableTimestampInterceptor(_timeProvider),
            new EntityChangeInterceptor(_timeProvider)
        );
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // A computed DbSet property on an abstract base is not discovered, so map it explicitly.
        modelBuilder.Entity<EntityChange>();
    }
}
