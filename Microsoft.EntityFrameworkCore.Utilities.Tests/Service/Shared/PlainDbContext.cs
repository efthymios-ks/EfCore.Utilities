using Microsoft.EntityFrameworkCore.Utilities.ChangeTracking;
using Microsoft.EntityFrameworkCore;

namespace Microsoft.EntityFrameworkCore.Utilities.Tests.Service.Shared;

/// <summary>
/// A context that does not inherit AuditableDbContext, used to prove the interceptors work on any
/// context that registers them.
/// </summary>
public sealed class PlainDbContext(DbContextOptions<PlainDbContext> options) : DbContext(options)
{
    public DbSet<Product> Products => Set<Product>();

    public DbSet<EntityChange> EntityChanges => Set<EntityChange>();
}

/// <summary>Uses the constructor that takes no TimeProvider, so the system clock is the fallback.</summary>
public sealed class SystemClockDbContext(DbContextOptions options)
    : Microsoft.EntityFrameworkCore.Utilities.DbContexts.AuditableDbContext(options)
{
    public DbSet<Product> Products => Set<Product>();
}
