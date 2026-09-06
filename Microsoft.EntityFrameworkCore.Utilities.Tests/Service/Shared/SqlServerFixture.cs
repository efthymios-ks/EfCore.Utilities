using Microsoft.EntityFrameworkCore.Utilities.Interceptors;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Testcontainers.MsSql;
using Xunit;

namespace Microsoft.EntityFrameworkCore.Utilities.Tests.Service.Shared;

/// <summary>
/// One SQL Server container for the whole service suite. Tests share it and call
/// <see cref="ResetAsync"/> first, so they must stay in the single collection below.
/// </summary>
public sealed class SqlServerFixture : IAsyncLifetime
{
    public static readonly DateTimeOffset StartTime = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly MsSqlContainer _container =
        new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();

    private DbContextOptions _options = null!;

    public FakeTimeProvider TimeProvider { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        TimeProvider = new FakeTimeProvider(StartTime);
        _options = new DbContextOptionsBuilder<TestDbContext>()
            .UseSqlServer(_container.GetConnectionString())
            .Options;

        await using var context = CreateContext();
        await context.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
        => await _container.DisposeAsync();

    public TestDbContext CreateContext()
        => new(_options, TimeProvider);

    /// <summary>A context wired up with the interceptors by hand rather than by inheriting a base.</summary>
    public PlainDbContext CreateInterceptedContext()
        => new(new DbContextOptionsBuilder<PlainDbContext>()
            .UseSqlServer(_container.GetConnectionString())
            .AddInterceptors(
                new AuditableTimestampInterceptor(TimeProvider),
                new EntityChangeInterceptor(TimeProvider)
            )
            .Options);

    /// <summary>A context built without a TimeProvider, to exercise the system-clock fallback.</summary>
    public SystemClockDbContext CreateSystemClockContext()
        => new(new DbContextOptionsBuilder<SystemClockDbContext>()
            .UseSqlServer(_container.GetConnectionString())
            .Options);

    /// <summary>A context with no interceptors at all, to show what they are responsible for.</summary>
    public PlainDbContext CreateBareContext()
        => new(new DbContextOptionsBuilder<PlainDbContext>()
            .UseSqlServer(_container.GetConnectionString())
            .Options);

    public async Task ResetAsync()
    {
        // A fresh provider rather than SetUtcNow: a test that advanced the clock leaves it ahead,
        // and FakeTimeProvider refuses to go backwards.
        TimeProvider = new FakeTimeProvider(StartTime);

        await using var context = CreateContext();
        await context.Database.ExecuteSqlRawAsync(
            """
            DELETE FROM [__EntityChanges];
            DELETE FROM [Products];
            DELETE FROM [Customers];
            DBCC CHECKIDENT ('[Products]', RESEED, 0) WITH NO_INFOMSGS;
            """
        );
    }
}

[CollectionDefinition(SqlServerCollection.Name)]
public sealed class SqlServerCollection : ICollectionFixture<SqlServerFixture>
{
    public const string Name = "SqlServer";
}
