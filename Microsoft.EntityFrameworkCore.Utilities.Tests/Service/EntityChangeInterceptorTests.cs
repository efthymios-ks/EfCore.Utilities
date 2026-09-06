using System.Text.Json;
using Microsoft.EntityFrameworkCore.Utilities.ChangeTracking;
using Microsoft.EntityFrameworkCore.Utilities.Tests.Service.Shared;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Microsoft.EntityFrameworkCore.Utilities.Tests.Service;

[Collection(SqlServerCollection.Name)]
public class EntityChangeInterceptorTests(SqlServerFixture fixture)
{
    [Fact]
    public async Task SaveChangesAsync_WhenAnEntityIsInserted_ShouldRecordAnInsert()
    {
        // Arrange
        await fixture.ResetAsync();
        await using var context = fixture.CreateContext();

        context.Products.Add(new Product { Name = "Widget", Price = 10m });
        await context.SaveChangesAsync();

        // Act
        var change = await context.EntityChanges.SingleAsync();

        // Assert
        Assert.Equal(EntityChangeAction.Insert, change.Action);
        Assert.Equal("Products", change.TableName);
        Assert.Equal(SqlServerFixture.StartTime, change.ChangedAt);
        Assert.Null(change.OldValues);
    }

    [Fact]
    public async Task SaveChangesAsync_WhenAnEntityIsInserted_ShouldRecordTheNewValuesAsJson()
    {
        // Arrange
        await fixture.ResetAsync();
        await using var context = fixture.CreateContext();

        context.Products.Add(new Product { Name = "Widget", Price = 10m });
        await context.SaveChangesAsync();

        // Act
        var newValues = Parse((await context.EntityChanges.SingleAsync()).NewValues);

        // Assert
        Assert.Equal("Widget", newValues["Name"].GetString());
        Assert.Equal(10m, newValues["Price"].GetDecimal());
    }

    [Fact]
    public async Task SaveChangesAsync_WhenTheKeyIsDatabaseGenerated_ShouldRecordTheResolvedKey()
    {
        // Arrange
        await fixture.ResetAsync();
        await using var context = fixture.CreateContext();

        var product = new Product { Name = "Widget", Price = 10m };
        context.Products.Add(product);
        await context.SaveChangesAsync();

        // Act
        var keys = Parse((await context.EntityChanges.SingleAsync()).Keys);

        // Assert
        Assert.True(product.Id > 0);
        Assert.Equal(product.Id, keys["Id"].GetInt32());
    }

    [Fact]
    public async Task SaveChangesAsync_WhenTheKeyIsCallerAssigned_ShouldRecordItAsGiven()
    {
        // Arrange
        await fixture.ResetAsync();
        await using var context = fixture.CreateContext();

        var id = Guid.NewGuid();
        context.Customers.Add(new Customer { Id = id, Email = "a@b.c" });
        await context.SaveChangesAsync();

        // Act
        var keys = Parse((await context.EntityChanges.SingleAsync()).Keys);

        // Assert
        Assert.Equal(id, keys["Id"].GetGuid());
    }

    [Fact]
    public async Task SaveChangesAsync_WhenAnEntityIsUpdated_ShouldRecordOnlyTheChangedProperties()
    {
        // Arrange
        await fixture.ResetAsync();
        var id = await SeedProductAsync();

        await using var context = fixture.CreateContext();
        var product = await context.Products.SingleAsync(product => product.Id == id);
        product.Price = 99m;
        await context.SaveChangesAsync();

        // Act
        var change = await SingleChangeAsync(context, EntityChangeAction.Update);
        var oldValues = Parse(change.OldValues);
        var newValues = Parse(change.NewValues);

        // Assert
        Assert.Equal(10m, oldValues["Price"].GetDecimal());
        Assert.Equal(99m, newValues["Price"].GetDecimal());
        Assert.DoesNotContain("Name", newValues.Keys);
    }

    [Fact]
    public async Task SaveChangesAsync_WhenAnEntityIsUpdated_ShouldRecordTheModifiedAtStamp()
    {
        // Arrange
        await fixture.ResetAsync();
        var id = await SeedProductAsync();

        fixture.TimeProvider.Advance(TimeSpan.FromHours(1));

        await using var context = fixture.CreateContext();
        var product = await context.Products.SingleAsync(product => product.Id == id);
        product.Price = 99m;
        await context.SaveChangesAsync();

        // Act
        var change = await SingleChangeAsync(context, EntityChangeAction.Update);

        // Assert
        Assert.Contains("ModifiedAt", Parse(change.NewValues).Keys);
        Assert.Equal(SqlServerFixture.StartTime.AddHours(1), change.ChangedAt);
    }

    [Fact]
    public async Task SaveChangesAsync_WhenAnEntityIsUpdated_ShouldRecordItsKey()
    {
        // Arrange
        await fixture.ResetAsync();
        var id = await SeedProductAsync();

        await using var context = fixture.CreateContext();
        var product = await context.Products.SingleAsync(product => product.Id == id);
        product.Price = 99m;
        await context.SaveChangesAsync();

        // Act
        var change = await SingleChangeAsync(context, EntityChangeAction.Update);

        // Assert
        Assert.Equal(id, Parse(change.Keys)["Id"].GetInt32());
    }

    [Fact]
    public async Task SaveChangesAsync_WhenAnEntityIsDeleted_ShouldRecordOldValuesAndNoNewValues()
    {
        // Arrange
        await fixture.ResetAsync();
        var id = await SeedProductAsync();

        await using var context = fixture.CreateContext();
        var product = await context.Products.SingleAsync(product => product.Id == id);
        context.Products.Remove(product);
        await context.SaveChangesAsync();

        // Act
        var change = await SingleChangeAsync(context, EntityChangeAction.Delete);

        // Assert
        Assert.Null(change.NewValues);
        Assert.Equal("Widget", Parse(change.OldValues)["Name"].GetString());
    }

    [Fact]
    public async Task SaveChangesAsync_WhenAnEntityIsDeleted_ShouldRecordTheKeyItHad()
    {
        // Arrange
        await fixture.ResetAsync();
        var id = await SeedProductAsync();

        await using var context = fixture.CreateContext();
        var product = await context.Products.SingleAsync(product => product.Id == id);
        context.Products.Remove(product);
        await context.SaveChangesAsync();

        // Act
        var change = await SingleChangeAsync(context, EntityChangeAction.Delete);

        // Assert
        Assert.Equal(id, Parse(change.Keys)["Id"].GetInt32());
    }

    [Fact]
    public async Task SaveChangesAsync_WhenNothingChanged_ShouldRecordNothing()
    {
        // Arrange
        await fixture.ResetAsync();
        await using var context = fixture.CreateContext();

        // Act
        await context.SaveChangesAsync();

        // Assert
        Assert.Empty(await context.EntityChanges.ToArrayAsync());
    }

    [Fact]
    public async Task SaveChangesAsync_WhenAnEntityIsReadButNotChanged_ShouldRecordNothingFurther()
    {
        // Arrange
        await fixture.ResetAsync();
        var id = await SeedProductAsync();

        // Act
        await using var context = fixture.CreateContext();
        _ = await context.Products.SingleAsync(product => product.Id == id);
        await context.SaveChangesAsync();

        // Assert
        Assert.Single(await context.EntityChanges.ToArrayAsync());
    }

    [Fact]
    public async Task SaveChangesAsync_WhenChangesAreRecorded_ShouldNotAuditTheAuditRows()
    {
        // Arrange
        await fixture.ResetAsync();
        await using var context = fixture.CreateContext();

        context.Products.Add(new Product { Name = "Widget", Price = 10m });
        await context.SaveChangesAsync();

        // Act
        var changes = await context.EntityChanges.ToArrayAsync();

        // Assert
        Assert.Single(changes);
        Assert.DoesNotContain(changes, change => change.TableName == "__EntityChanges");
    }

    [Fact]
    public async Task SaveChangesAsync_WhenSeveralEntitiesChange_ShouldRecordOneRowEach()
    {
        // Arrange
        await fixture.ResetAsync();
        await using var context = fixture.CreateContext();

        context.Products.AddRange(
            new Product { Name = "One", Price = 1m },
            new Product { Name = "Two", Price = 2m }
        );

        // Act
        await context.SaveChangesAsync();

        // Assert
        Assert.Equal(2, await context.EntityChanges.CountAsync());
    }

    [Fact]
    public async Task SaveChangesAsync_WhenOneSaveMixesActions_ShouldRecordEachActionSeparately()
    {
        // Arrange
        await fixture.ResetAsync();
        var id = await SeedProductAsync();

        await using var context = fixture.CreateContext();

        var existing = await context.Products.SingleAsync(product => product.Id == id);
        existing.Price = 50m;
        context.Products.Add(new Product { Name = "Added", Price = 1m });
        context.Customers.Add(new Customer { Id = Guid.NewGuid(), Email = "a@b.c" });
        await context.SaveChangesAsync();

        // Act
        var actions = await context.EntityChanges
            .Where(change => change.ChangedAt == SqlServerFixture.StartTime)
            .GroupBy(change => change.Action)
            .Select(group => new { group.Key, Count = group.Count() })
            .ToArrayAsync();

        // Assert
        Assert.Equal(3, actions.Single(action => action.Key == EntityChangeAction.Insert).Count);
        Assert.Equal(1, actions.Single(action => action.Key == EntityChangeAction.Update).Count);
    }

    [Fact]
    public async Task SaveChangesAsync_WhenSavedTwiceOnTheSameContext_ShouldAuditEachSaveOnce()
    {
        // Arrange
        await fixture.ResetAsync();
        await using var context = fixture.CreateContext();

        context.Products.Add(new Product { Name = "First", Price = 1m });
        await context.SaveChangesAsync();

        // Act
        context.Products.Add(new Product { Name = "Second", Price = 2m });
        await context.SaveChangesAsync();

        // Assert
        Assert.Equal(2, await context.EntityChanges.CountAsync());
    }

    [Fact]
    public async Task SaveChanges_WhenCalledSynchronously_ShouldAuditTheSameWay()
    {
        // Arrange
        await fixture.ResetAsync();
        await using var context = fixture.CreateContext();

        // Act
        context.Products.Add(new Product { Name = "Widget", Price = 10m });
        context.SaveChanges();

        // Assert
        Assert.Single(context.EntityChanges);
    }

    [Fact]
    public async Task SaveChangesAsync_WhenTheContextOnlyRegistersTheInterceptors_ShouldStillAudit()
    {
        // Arrange
        await fixture.ResetAsync();
        await using var context = fixture.CreateInterceptedContext();

        // Act
        context.Products.Add(new Product { Name = "Widget", Price = 10m });
        await context.SaveChangesAsync();

        // Assert
        Assert.Equal(SqlServerFixture.StartTime, (await context.Products.SingleAsync()).CreatedAt);
        Assert.Equal(EntityChangeAction.Insert, (await context.EntityChanges.SingleAsync()).Action);
    }

    [Fact]
    public async Task SaveChangesAsync_WhenTheContextHasNoInterceptors_ShouldRecordNothing()
    {
        // Arrange
        await fixture.ResetAsync();
        await using var context = fixture.CreateBareContext();

        // Act
        context.Products.Add(new Product { Name = "Widget", Price = 10m });
        await context.SaveChangesAsync();

        // Assert
        Assert.Empty(await context.EntityChanges.ToArrayAsync());
    }

    private static async Task<EntityChange> SingleChangeAsync(TestDbContext context, EntityChangeAction action)
        => await context.EntityChanges.SingleAsync(change => change.Action == action);

    private static Dictionary<string, JsonElement> Parse(string? json)
        => JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json!)!;

    private async Task<int> SeedProductAsync()
    {
        await using var context = fixture.CreateContext();

        var product = new Product { Name = "Widget", Price = 10m };
        context.Products.Add(product);
        await context.SaveChangesAsync();

        return product.Id;
    }
}
