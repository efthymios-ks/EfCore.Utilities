using Microsoft.EntityFrameworkCore.Utilities.Extensions;
using Microsoft.EntityFrameworkCore.Utilities.Tests.Service.Shared;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Microsoft.EntityFrameworkCore.Utilities.Tests.Service;

/// <summary>
/// Runs against <see cref="Customer"/> because its key is caller-assigned, so a test can seed known
/// ids without turning on IDENTITY_INSERT.
/// </summary>
[Collection(SqlServerCollection.Name)]
public class DbSetExtensionsTests(SqlServerFixture fixture)
{
    private static readonly Guid _first = new("00000000-0000-0000-0000-000000000001");
    private static readonly Guid _second = new("00000000-0000-0000-0000-000000000002");
    private static readonly Guid _third = new("00000000-0000-0000-0000-000000000003");
    private static readonly Guid _absent = new("00000000-0000-0000-0000-0000000000ff");

    [Fact]
    public async Task AddManyIfMissingAsync_WhenSomeKeysAlreadyExist_ShouldAddOnlyTheMissingOnes()
    {
        // Arrange
        await fixture.ResetAsync();
        await SeedAsync(_first, _second);

        await using var context = fixture.CreateContext();

        var added = await context.Customers.AddManyIfMissingAsync<Customer, Guid>(
        [
            Customer(_second, "existing@test.com"),
            Customer(_third, "fresh@test.com")
        ]);

        await context.SaveChangesAsync();

        // Act
        Guid[] expectedAdded = [_third];
        Guid[] expectedIds = [_first, _second, _third];

        // Assert
        Assert.Equal(expectedAdded, added.Select(customer => customer.Id));
        Assert.Equal(expectedIds, await OrderedIdsAsync());
    }

    [Fact]
    public async Task AddManyIfMissingAsync_WhenTheListIsEmpty_ShouldAddNothing()
    {
        // Arrange & Act
        await fixture.ResetAsync();
        await using var context = fixture.CreateContext();

        // Assert
        Assert.Empty(await context.Customers.AddManyIfMissingAsync<Customer, Guid>([]));
    }

    [Fact]
    public async Task AddManyIfMissingAsync_WhenAnExistingKeyIsGiven_ShouldNotOverwriteThatRow()
    {
        // Arrange
        await fixture.ResetAsync();
        await SeedAsync(_first);

        await using var context = fixture.CreateContext();

        await context.Customers.AddManyIfMissingAsync<Customer, Guid>(
            [Customer(_first, "overwrite@test.com")]
        );

        await context.SaveChangesAsync();

        // Act
        await using var verifyContext = fixture.CreateContext();

        // Assert
        Assert.Equal("seed-1@test.com", (await verifyContext.Customers.SingleAsync()).Email);
    }

    [Fact]
    public async Task AddManyIfMissingAsync_WhenEveryKeyIsUnassigned_ShouldAddThemAll()
    {
        // Arrange
        await fixture.ResetAsync();
        await using var context = fixture.CreateContext();

        var added = await context.Products.AddManyIfMissingAsync<Product, int>(
        [
            new Product { Name = "One", Price = 1m },
            new Product { Name = "Two", Price = 2m }
        ]);

        // Act
        await context.SaveChangesAsync();

        // Assert
        Assert.Equal(2, added.Count);
        Assert.Equal(2, await context.Products.CountAsync());
    }

    [Fact]
    public async Task UpdateManyIfPresentAsync_WhenSomeKeysAreMissing_ShouldUpdateOnlyThePresentOnes()
    {
        // Arrange
        await fixture.ResetAsync();
        await SeedAsync(_first, _second);

        await using var context = fixture.CreateContext();

        var updated = await context.Customers.UpdateManyIfPresentAsync<Customer, Guid>(
        [
            Customer(_second, "updated@test.com"),
            Customer(_absent, "absent@test.com")
        ]);

        await context.SaveChangesAsync();

        Guid[] expectedUpdated = [_second];
        Guid[] expectedIds = [_first, _second];

        Assert.Equal(expectedUpdated, updated.Select(customer => customer.Id));
        Assert.Equal(expectedIds, await OrderedIdsAsync());

        // Act
        await using var verifyContext = fixture.CreateContext();
        var stored = await verifyContext.Customers.SingleAsync(customer => customer.Id == _second);

        // Assert
        Assert.Equal("updated@test.com", stored.Email);
    }

    [Fact]
    public async Task UpdateManyIfPresentAsync_WhenNoKeyIsPresent_ShouldUpdateNothing()
    {
        // Arrange
        await fixture.ResetAsync();
        await SeedAsync(_first);

        await using var context = fixture.CreateContext();

        var updated = await context.Customers.UpdateManyIfPresentAsync<Customer, Guid>(
            [Customer(_absent, "absent@test.com")]
        );

        // Act
        await context.SaveChangesAsync();

        // Assert
        Assert.Empty(updated);
        Assert.Single(await OrderedIdsAsync());
    }

    [Fact]
    public async Task AddOrUpdateManyAsync_WhenTheSourceMixesKnownAndUnknownKeys_ShouldSplitThem()
    {
        // Arrange
        await fixture.ResetAsync();
        await SeedAsync(_first);

        await using var context = fixture.CreateContext();

        var result = await context.Customers.AddOrUpdateManyAsync<Customer, Guid>(
        [
            Customer(_first, "updated@test.com"),
            Customer(_third, "added@test.com")
        ]);

        await context.SaveChangesAsync();

        // Act
        Guid[] expectedAdded = [_third];
        Guid[] expectedUpdated = [_first];
        Guid[] expectedIds = [_first, _third];

        // Assert
        Assert.Equal(expectedAdded, result.Added.Select(customer => customer.Id));
        Assert.Equal(expectedUpdated, result.Updated.Select(customer => customer.Id));
        Assert.Equal(expectedIds, await OrderedIdsAsync());
    }

    [Fact]
    public async Task AddOrUpdateManyAsync_WhenTheTableIsEmpty_ShouldTreatEveryEntityAsNew()
    {
        // Arrange
        await fixture.ResetAsync();
        await using var context = fixture.CreateContext();

        // Act
        var result = await context.Customers.AddOrUpdateManyAsync<Customer, Guid>(
        [
            Customer(_first, "one@test.com"),
            Customer(_second, "two@test.com")
        ]);

        // Assert
        Assert.Equal(2, result.Added.Count);
        Assert.Empty(result.Updated);
    }

    [Fact]
    public async Task AddOrUpdateManyAsync_WhenTheSameKeyAppearsTwice_ShouldRejectTheCall()
    {
        // Arrange
        await fixture.ResetAsync();
        await SeedAsync(_first);

        await using var context = fixture.CreateContext();

        // Act & Assert
        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            context.Customers.AddOrUpdateManyAsync<Customer, Guid>(
            [
                Customer(_first, "first@test.com"),
                Customer(_first, "again@test.com")
            ]));

        Assert.Contains("more than once", exception.Message);
    }

    [Fact]
    public async Task AddManyIfMissingAsync_WhenSeveralEntitiesShareAnUnassignedKey_ShouldStillAcceptThem()
    {
        // Arrange
        await fixture.ResetAsync();
        await using var context = fixture.CreateContext();

        // Every new entity has the default key; only assigned keys can genuinely collide.
        var added = await context.Products.AddManyIfMissingAsync<Product, int>(
        [
            new Product { Name = "One", Price = 1m },
            new Product { Name = "Two", Price = 2m },
            new Product { Name = "Three", Price = 3m }
        ]);

        // Act
        await context.SaveChangesAsync();

        // Assert
        Assert.Equal(3, added.Count);
    }

    [Fact]
    public async Task RemoveByKeyAsync_WhenTheKeyExists_ShouldRemoveThatRow()
    {
        // Arrange
        await fixture.ResetAsync();
        await SeedAsync(_first, _second);

        await using var context = fixture.CreateContext();

        var removed = await context.Customers.RemoveByKeyAsync<Customer, Guid>(_first);
        await context.SaveChangesAsync();

        // Act
        Guid[] expectedIds = [_second];

        // Assert
        Assert.True(removed);
        Assert.Equal(expectedIds, await OrderedIdsAsync());
    }

    [Fact]
    public async Task RemoveByKeyAsync_WhenTheKeyIsMissing_ShouldReportFalseAndRemoveNothing()
    {
        // Arrange
        await fixture.ResetAsync();
        await SeedAsync(_first);

        await using var context = fixture.CreateContext();

        var removed = await context.Customers.RemoveByKeyAsync<Customer, Guid>(_absent);
        await context.SaveChangesAsync();

        // Act
        Guid[] expectedIds = [_first];

        // Assert
        Assert.False(removed);
        Assert.Equal(expectedIds, await OrderedIdsAsync());
    }

    [Fact]
    public async Task RemoveByKeyAsync_WhenTheRowIsRemoved_ShouldRecordTheDeleteInTheAuditLog()
    {
        // Arrange
        await fixture.ResetAsync();
        await SeedAsync(_first);

        await using var context = fixture.CreateContext();

        // Act
        await context.Customers.RemoveByKeyAsync<Customer, Guid>(_first);
        await context.SaveChangesAsync();

        // Assert
        Assert.Contains(
            await context.EntityChanges.ToArrayAsync(),
            change => change.Action == ChangeTracking.EntityChangeAction.Delete
        );
    }

    [Fact]
    public async Task ExistsAsync_WhenTheKeyExists_ShouldReturnTrue()
    {
        // Arrange
        await fixture.ResetAsync();
        await SeedAsync(_first);

        // Act
        await using var context = fixture.CreateContext();

        // Assert
        Assert.True(await context.Customers.ExistsAsync<Customer, Guid>(_first));
    }

    [Fact]
    public async Task ExistsAsync_WhenTheKeyIsMissing_ShouldReturnFalse()
    {
        // Arrange
        await fixture.ResetAsync();
        await SeedAsync(_first);

        // Act
        await using var context = fixture.CreateContext();

        // Assert
        Assert.False(await context.Customers.ExistsAsync<Customer, Guid>(_absent));
    }

    [Fact]
    public async Task ExistsAsync_WhenCalled_ShouldNotTrackTheEntity()
    {
        // Arrange
        await fixture.ResetAsync();
        await SeedAsync(_first);

        await using var context = fixture.CreateContext();

        // Act
        await context.Customers.ExistsAsync<Customer, Guid>(_first);

        // Assert
        Assert.Empty(context.ChangeTracker.Entries<Customer>());
    }

    [Fact]
    public async Task ExistsAsync_WhenTheKeyIsAnIdentityColumn_ShouldStillTranslate()
    {
        // Arrange
        await fixture.ResetAsync();

        await using var seedContext = fixture.CreateContext();
        var product = new Product { Name = "Widget", Price = 1m };
        seedContext.Products.Add(product);
        await seedContext.SaveChangesAsync();

        // Act
        await using var context = fixture.CreateContext();

        // Assert
        Assert.True(await context.Products.ExistsAsync<Product, int>(product.Id));
        Assert.False(await context.Products.ExistsAsync<Product, int>(product.Id + 1000));
    }

    [Fact]
    public async Task AddManyIfMissingAsync_WhenTheEntitiesAreNull_ShouldThrowArgumentNull()
    {
        // Arrange
        await fixture.ResetAsync();
        await using var context = fixture.CreateContext();

        // Act & Assert
        var exception = await Assert.ThrowsAsync<ArgumentNullException>(() =>
            context.Customers.AddManyIfMissingAsync<Customer, Guid>(null!));

        Assert.Equal("entities", exception.ParamName);
    }

    [Fact]
    public async Task UpdateManyIfPresentAsync_WhenTheEntitiesAreNull_ShouldThrowArgumentNull()
    {
        // Arrange
        await fixture.ResetAsync();
        await using var context = fixture.CreateContext();

        // Act & Assert
        var exception = await Assert.ThrowsAsync<ArgumentNullException>(() =>
            context.Customers.UpdateManyIfPresentAsync<Customer, Guid>(null!));

        Assert.Equal("entities", exception.ParamName);
    }

    [Fact]
    public async Task AddOrUpdateManyAsync_WhenTheEntitiesAreNull_ShouldThrowArgumentNull()
    {
        // Arrange
        await fixture.ResetAsync();
        await using var context = fixture.CreateContext();

        // Act & Assert
        var exception = await Assert.ThrowsAsync<ArgumentNullException>(() =>
            context.Customers.AddOrUpdateManyAsync<Customer, Guid>(null!));

        Assert.Equal("entities", exception.ParamName);
    }

    private static Customer Customer(Guid id, string email)
        => new() { Id = id, Email = email, LoyaltyPoints = 1 };

    private async Task SeedAsync(params Guid[] ids)
    {
        await using var context = fixture.CreateContext();

        context.Customers.AddRange(ids.Select((id, index) => new Customer
        {
            Id = id,
            Email = $"seed-{index + 1}@test.com",
            LoyaltyPoints = index
        }));

        await context.SaveChangesAsync();
    }

    private async Task<Guid[]> OrderedIdsAsync()
    {
        await using var context = fixture.CreateContext();

        return await context.Customers
            .OrderBy(customer => customer.Id)
            .Select(customer => customer.Id)
            .ToArrayAsync();
    }
}
