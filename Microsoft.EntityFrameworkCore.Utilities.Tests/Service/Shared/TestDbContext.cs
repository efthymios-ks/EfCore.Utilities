using Microsoft.EntityFrameworkCore.Utilities.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace Microsoft.EntityFrameworkCore.Utilities.Tests.Service.Shared;

public sealed class TestDbContext(DbContextOptions options, TimeProvider timeProvider)
    : AuditableDbContext(options, timeProvider)
{
    public DbSet<Product> Products => Set<Product>();

    public DbSet<Customer> Customers => Set<Customer>();
}
