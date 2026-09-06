using Microsoft.EntityFrameworkCore.Utilities.ChangeTracking;
using Microsoft.EntityFrameworkCore.Utilities.Tests.Service.Shared;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Microsoft.EntityFrameworkCore.Utilities.Tests.Service;

[Collection(SqlServerCollection.Name)]
public class TransactionTests(SqlServerFixture fixture)
{
    /// <summary>The batch every transaction test applies: two inserts, two updates, two deletes.</summary>
    private static readonly DateTimeOffset BatchTime = SqlServerFixture.StartTime.AddHours(1);

    [Fact]
    public async Task SaveChangesAsync_WhenItOwnsTheTransaction_ShouldCommitEveryActionAndItsAuditRows()
    {
        // Arrange
        await fixture.ResetAsync();
        var seeded = await SeedAsync();

        await using (var context = fixture.CreateContext())
        {
            await ApplyMixedBatchAsync(context, seeded);
            await context.SaveChangesAsync();
        }

        // Act
        await AssertBatchAppliedAsync();
    }

    [Fact]
    public async Task SaveChangesAsync_WhenTheCallerCommitsTheirTransaction_ShouldCommitEveryActionAndItsAuditRows()
    {
        // Arrange
        await fixture.ResetAsync();
        var seeded = await SeedAsync();

        await using (var context = fixture.CreateContext())
        {
            await using var transaction = await context.Database.BeginTransactionAsync();

            await ApplyMixedBatchAsync(context, seeded);
            await context.SaveChangesAsync();

            await transaction.CommitAsync();
        }

        // Act
        await AssertBatchAppliedAsync();
    }

    [Fact]
    public async Task SaveChangesAsync_WhenTheCallerRollsBackTheirTransaction_ShouldUndoEveryActionAndItsAuditRows()
    {
        // Arrange
        await fixture.ResetAsync();
        var seeded = await SeedAsync();

        await using (var context = fixture.CreateContext())
        {
            await using var transaction = await context.Database.BeginTransactionAsync();

            await ApplyMixedBatchAsync(context, seeded);
            await context.SaveChangesAsync();

            await transaction.RollbackAsync();
        }

        // Act
        await AssertBatchUndoneAsync();
    }

    [Fact]
    public async Task SaveChanges_WhenItOwnsTheTransaction_ShouldCommitEveryActionAndItsAuditRows()
    {
        // Arrange
        await fixture.ResetAsync();
        var seeded = await SeedAsync();

        using (var context = fixture.CreateContext())
        {
            await ApplyMixedBatchAsync(context, seeded);
            context.SaveChanges();
        }

        // Act
        await AssertBatchAppliedAsync();
    }

    [Fact]
    public async Task SaveChanges_WhenTheCallerRollsBackTheirTransaction_ShouldUndoEveryActionAndItsAuditRows()
    {
        // Arrange
        await fixture.ResetAsync();
        var seeded = await SeedAsync();

        using (var context = fixture.CreateContext())
        {
            using var transaction = context.Database.BeginTransaction();

            await ApplyMixedBatchAsync(context, seeded);
            context.SaveChanges();

            transaction.Rollback();
        }

        // Act
        await AssertBatchUndoneAsync();
    }

    [Fact]
    public async Task SaveChangesAsync_WhenSeveralSavesShareTheCallersTransaction_ShouldApplyThemAsAUnit()
    {
        // Arrange
        await fixture.ResetAsync();

        await using (var context = fixture.CreateContext())
        {
            await using var transaction = await context.Database.BeginTransactionAsync();

            context.Products.Add(new Product { Name = "First", Price = 1m });
            await context.SaveChangesAsync();

            context.Products.Add(new Product { Name = "Second", Price = 2m });
            await context.SaveChangesAsync();

            await transaction.RollbackAsync();
        }

        // Act
        await using var verifyContext = fixture.CreateContext();

        // Assert
        Assert.Empty(await verifyContext.Products.ToArrayAsync());
        Assert.Empty(await verifyContext.EntityChanges.ToArrayAsync());
    }

    [Fact]
    public async Task SaveChangesAsync_WhenTheSaveFails_ShouldLeaveNothingBehind()
    {
        // Arrange
        await fixture.ResetAsync();
        await SeedAsync();

        await using (var context = fixture.CreateContext())
        {
            // Name is required, so the database rejects the insert.
            context.Products.Add(new Product { Name = null!, Price = 1m });

            await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        }

        // Act
        await AssertBatchUndoneAsync();
    }

    [Fact]
    public async Task SaveChangesAsync_AfterAFailedSave_ShouldStillAuditTheNextOne()
    {
        // Arrange
        await fixture.ResetAsync();
        await using var context = fixture.CreateContext();

        var invalid = new Product { Name = null!, Price = 1m };
        context.Products.Add(invalid);
        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());

        // Act
        context.Entry(invalid).State = EntityState.Detached;
        context.Products.Add(new Product { Name = "Valid", Price = 2m });
        await context.SaveChangesAsync();

        // Assert
        Assert.Single(await context.EntityChanges.ToArrayAsync());
    }

    [Fact]
    public async Task SaveChanges_WhenASynchronousSaveFails_ShouldLeaveNothingBehind()
    {
        // Arrange
        await fixture.ResetAsync();
        await SeedAsync();

        using (var context = fixture.CreateContext())
        {
            context.Products.Add(new Product { Name = null!, Price = 1m });

            Assert.Throws<DbUpdateException>(() => context.SaveChanges());
        }

        // Act
        await AssertBatchUndoneAsync();
    }

    [Fact]
    public async Task SaveChanges_AfterAFailedSynchronousSave_ShouldStillAuditTheNextOne()
    {
        // Arrange
        await fixture.ResetAsync();
        using var context = fixture.CreateContext();

        var invalid = new Product { Name = null!, Price = 1m };
        context.Products.Add(invalid);
        Assert.Throws<DbUpdateException>(() => context.SaveChanges());

        // Act
        context.Entry(invalid).State = EntityState.Detached;
        context.Products.Add(new Product { Name = "Valid", Price = 2m });
        context.SaveChanges();

        // Assert
        Assert.Single(await context.EntityChanges.ToArrayAsync());
    }

    /// <summary>Four rows: two to update, two to delete. Their inserts are audited at the seed time.</summary>
    private async Task<int[]> SeedAsync()
    {
        await using var context = fixture.CreateContext();

        var products = Enumerable
            .Range(1, 4)
            .Select(index => new Product { Name = $"Seed {index}", Price = index })
            .ToArray();

        context.Products.AddRange(products);
        await context.SaveChangesAsync();

        // Separates the batch's audit rows from the seed's, so each can be counted on its own.
        fixture.TimeProvider.Advance(TimeSpan.FromHours(1));

        return [.. products.Select(product => product.Id)];
    }

    private static async Task ApplyMixedBatchAsync(TestDbContext context, int[] seededIds)
    {
        var toUpdate = await context.Products
            .Where(product => seededIds[0] == product.Id || seededIds[1] == product.Id)
            .ToArrayAsync();

        foreach (var product in toUpdate)
        {
            product.Price += 100m;
        }

        var toDelete = await context.Products
            .Where(product => seededIds[2] == product.Id || seededIds[3] == product.Id)
            .ToArrayAsync();

        context.Products.RemoveRange(toDelete);

        context.Products.AddRange(
            new Product { Name = "Added 1", Price = 11m },
            new Product { Name = "Added 2", Price = 22m }
        );
    }

    private async Task AssertBatchAppliedAsync()
    {
        await using var context = fixture.CreateContext();

        var names = await context.Products
            .OrderBy(product => product.Name)
            .Select(product => product.Name)
            .ToArrayAsync();

        string[] expectedNames = ["Added 1", "Added 2", "Seed 1", "Seed 2"];
        Assert.Equal(expectedNames, names);

        Assert.Equal(2, await CountBatchAsync(context, EntityChangeAction.Insert));
        Assert.Equal(2, await CountBatchAsync(context, EntityChangeAction.Update));
        Assert.Equal(2, await CountBatchAsync(context, EntityChangeAction.Delete));
    }

    private async Task AssertBatchUndoneAsync()
    {
        await using var context = fixture.CreateContext();

        var names = await context.Products
            .OrderBy(product => product.Name)
            .Select(product => product.Name)
            .ToArrayAsync();

        string[] expectedNames = ["Seed 1", "Seed 2", "Seed 3", "Seed 4"];
        Assert.Equal(expectedNames, names);

        Assert.Empty(await context.EntityChanges
            .Where(change => change.ChangedAt == BatchTime)
            .ToArrayAsync());
    }

    private static Task<int> CountBatchAsync(TestDbContext context, EntityChangeAction action)
        => context.EntityChanges
            .Where(change => change.ChangedAt == BatchTime && change.Action == action)
            .CountAsync();
}
