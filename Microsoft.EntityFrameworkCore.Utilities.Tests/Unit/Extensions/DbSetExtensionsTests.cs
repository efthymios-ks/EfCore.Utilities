using Microsoft.EntityFrameworkCore.Utilities.Entities;
using Microsoft.EntityFrameworkCore.Utilities.Extensions;
using Xunit;

namespace Microsoft.EntityFrameworkCore.Utilities.Tests.Unit.Extensions;

/// <summary>The argument checks, which are the only part of the extensions that needs no database.</summary>
public class DbSetExtensionsTests
{
    [Fact]
    public async Task AddManyIfMissingAsync_WhenTheSetIsNull_ShouldThrowArgumentNull()
        => await Assert.ThrowsAsync<ArgumentNullException>(() =>
            DbSetExtensions.AddManyIfMissingAsync<Widget, int>(null!, []));

    [Fact]
    public async Task UpdateManyIfPresentAsync_WhenTheSetIsNull_ShouldThrowArgumentNull()
        => await Assert.ThrowsAsync<ArgumentNullException>(() =>
            DbSetExtensions.UpdateManyIfPresentAsync<Widget, int>(null!, []));

    [Fact]
    public async Task AddOrUpdateManyAsync_WhenTheSetIsNull_ShouldThrowArgumentNull()
        => await Assert.ThrowsAsync<ArgumentNullException>(() =>
            DbSetExtensions.AddOrUpdateManyAsync<Widget, int>(null!, []));

    [Fact]
    public async Task RemoveByKeyAsync_WhenTheSetIsNull_ShouldThrowArgumentNull()
        => await Assert.ThrowsAsync<ArgumentNullException>(() =>
            DbSetExtensions.RemoveByKeyAsync<Widget, int>(null!, 1));

    [Fact]
    public async Task ExistsAsync_WhenTheSetIsNull_ShouldThrowArgumentNull()
        => await Assert.ThrowsAsync<ArgumentNullException>(() =>
            DbSetExtensions.ExistsAsync<Widget, int>(null!, 1));

    [Fact]
    public void UpsertResult_WhenConstructed_ShouldKeepBothListsApart()
    {
        // Arrange
        Widget[] added = [new() { Id = 1 }];
        Widget[] updated = [new() { Id = 2 }];

        // Act
        var result = new UpsertResult<Widget>(added, updated);

        // Assert
        Assert.Equal(added, result.Added);
        Assert.Equal(updated, result.Updated);
    }

    private sealed class Widget : EntityBase<int>;
}
