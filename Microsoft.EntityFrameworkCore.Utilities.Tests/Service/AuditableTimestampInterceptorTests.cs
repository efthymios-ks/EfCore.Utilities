using Microsoft.EntityFrameworkCore.Utilities.Tests.Service.Shared;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Microsoft.EntityFrameworkCore.Utilities.Tests.Service;

[Collection(SqlServerCollection.Name)]
public class AuditableTimestampInterceptorTests(SqlServerFixture fixture)
{
    [Fact]
    public async Task SaveChangesAsync_WhenAnEntityIsAdded_ShouldStampCreatedAtFromTheTimeProvider()
    {
        // Arrange
        await fixture.ResetAsync();
        await using var context = fixture.CreateContext();

        context.Products.Add(new Product { Name = "Widget", Price = 10m });
        await context.SaveChangesAsync();

        // Act
        var stored = await context.Products.SingleAsync();

        // Assert
        Assert.Equal(SqlServerFixture.StartTime, stored.CreatedAt);
        Assert.Null(stored.ModifiedAt);
    }

    [Fact]
    public async Task SaveChangesAsync_WhenAnEntityIsModified_ShouldStampModifiedAt()
    {
        // Arrange
        await fixture.ResetAsync();
        var id = await SeedProductAsync();

        fixture.TimeProvider.Advance(TimeSpan.FromHours(3));

        await using (var context = fixture.CreateContext())
        {
            var product = await context.Products.SingleAsync(product => product.Id == id);
            product.Price = 99m;
            await context.SaveChangesAsync();
        }

        // Act
        await using var verifyContext = fixture.CreateContext();
        var stored = await verifyContext.Products.SingleAsync();

        // Assert
        Assert.Equal(SqlServerFixture.StartTime.AddHours(3), stored.ModifiedAt);
    }

    [Fact]
    public async Task SaveChangesAsync_WhenAnEntityIsModified_ShouldLeaveCreatedAtAlone()
    {
        // Arrange
        await fixture.ResetAsync();
        var id = await SeedProductAsync();

        fixture.TimeProvider.Advance(TimeSpan.FromDays(1));

        await using (var context = fixture.CreateContext())
        {
            var product = await context.Products.SingleAsync(product => product.Id == id);
            product.Name = "Renamed";

            // Even a caller that deliberately overwrites it must not win.
            product.CreatedAt = SqlServerFixture.StartTime.AddYears(10);
            await context.SaveChangesAsync();
        }

        // Act
        await using var verifyContext = fixture.CreateContext();

        // Assert
        Assert.Equal(SqlServerFixture.StartTime, (await verifyContext.Products.SingleAsync()).CreatedAt);
    }

    [Fact]
    public async Task SaveChangesAsync_WhenSeveralEntitiesAreSavedTogether_ShouldGiveThemOneTimestamp()
    {
        // Arrange
        await fixture.ResetAsync();
        await using var context = fixture.CreateContext();

        context.Products.AddRange(
            new Product { Name = "One", Price = 1m },
            new Product { Name = "Two", Price = 2m },
            new Product { Name = "Three", Price = 3m }
        );

        await context.SaveChangesAsync();

        // Act
        var timestamps = await context.Products
            .Select(product => product.CreatedAt)
            .Distinct()
            .ToArrayAsync();

        // Assert
        Assert.Single(timestamps);
    }

    [Fact]
    public async Task SaveChangesAsync_WhenEntitiesOfDifferentKeyTypesAreSaved_ShouldStampBoth()
    {
        // Arrange
        await fixture.ResetAsync();
        await using var context = fixture.CreateContext();

        // Act
        context.Products.Add(new Product { Name = "Widget", Price = 1m });
        context.Customers.Add(new Customer { Id = Guid.NewGuid(), Email = "a@b.c" });
        await context.SaveChangesAsync();

        // Assert
        Assert.Equal(SqlServerFixture.StartTime, (await context.Products.SingleAsync()).CreatedAt);
        Assert.Equal(SqlServerFixture.StartTime, (await context.Customers.SingleAsync()).CreatedAt);
    }

    [Fact]
    public async Task SaveChangesAsync_WhenAnEntityIsDeleted_ShouldNotFailOnTheMissingStamp()
    {
        // Arrange
        await fixture.ResetAsync();
        var id = await SeedProductAsync();

        // Act
        await using var context = fixture.CreateContext();
        var product = await context.Products.SingleAsync(product => product.Id == id);
        context.Products.Remove(product);
        await context.SaveChangesAsync();

        // Assert
        Assert.Empty(await context.Products.ToArrayAsync());
    }

    [Fact]
    public async Task SaveChanges_WhenCalledSynchronously_ShouldStampTheSameWay()
    {
        // Arrange
        await fixture.ResetAsync();
        await using var context = fixture.CreateContext();

        // Act
        context.Products.Add(new Product { Name = "Sync", Price = 5m });
        context.SaveChanges();

        // Assert
        Assert.Equal(SqlServerFixture.StartTime, context.Products.Single().CreatedAt);
    }

    [Fact]
    public async Task SaveChangesAsync_WhenNothingChanged_ShouldReportNoWrites()
    {
        // Arrange & Act
        await fixture.ResetAsync();
        await using var context = fixture.CreateContext();

        // Assert
        Assert.Equal(0, await context.SaveChangesAsync());
    }

    [Fact]
    public async Task SaveChangesAsync_WhenAnEntityIsAdded_ShouldReportOnlyTheEntityRowCount()
    {
        // Arrange
        await fixture.ResetAsync();
        await using var context = fixture.CreateContext();

        context.Products.Add(new Product { Name = "Widget", Price = 10m });

        // Act
        // The audit row is written by a second save and must not inflate the reported count.
        Assert.Equal(1, await context.SaveChangesAsync());
    }

    [Fact]
    public async Task SaveChangesAsync_WhenTheContextHasNoInterceptors_ShouldLeaveTheStampsUnset()
    {
        // Arrange
        await fixture.ResetAsync();
        await using var context = fixture.CreateBareContext();

        // Act
        context.Products.Add(new Product { Name = "Unstamped", Price = 1m });
        await context.SaveChangesAsync();

        // Assert
        Assert.Equal(default, (await context.Products.SingleAsync()).CreatedAt);
    }

    [Fact]
    public async Task SaveChangesAsync_WhenTheContextTakesNoTimeProvider_ShouldStampFromTheSystemClock()
    {
        // Arrange
        await fixture.ResetAsync();
        var before = DateTimeOffset.UtcNow;

        await using var context = fixture.CreateSystemClockContext();
        context.Products.Add(new Product { Name = "Widget", Price = 1m });
        await context.SaveChangesAsync();

        // Act
        var stored = await context.Products.SingleAsync();

        // Assert
        Assert.InRange(stored.CreatedAt, before, DateTimeOffset.UtcNow);
    }

    private async Task<int> SeedProductAsync()
    {
        await using var context = fixture.CreateContext();

        var product = new Product { Name = "Widget", Price = 10m };
        context.Products.Add(product);
        await context.SaveChangesAsync();

        return product.Id;
    }
}
