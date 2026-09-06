using Microsoft.EntityFrameworkCore.Utilities.Entities;
using Xunit;

namespace Microsoft.EntityFrameworkCore.Utilities.Tests.Unit.Entities;

public class EntityBaseTests
{
    [Fact]
    public void Id_WhenNotAssigned_ShouldBeTheDefaultForItsType()
    {
        // Act & Assert
        Assert.Equal(0, new IntKeyed().Id);
        Assert.Equal(Guid.Empty, new GuidKeyed().Id);
    }

    [Fact]
    public void ModifiedAt_WhenNeverModified_ShouldBeNull()
        => Assert.Null(new IntKeyed().ModifiedAt);

    [Fact]
    public void CreatedAt_WhenNeverSaved_ShouldBeTheDefault()
        => Assert.Equal(default, new IntKeyed().CreatedAt);

    [Fact]
    public void ToString_WhenIdIsAssigned_ShouldNameTheTypeAndTheId()
        => Assert.Equal("IntKeyed 42", new IntKeyed { Id = 42 }.ToString());

    [Fact]
    public void ToString_WhenIdIsUnassigned_ShouldStillNameTheType()
        => Assert.Equal("IntKeyed 0", new IntKeyed().ToString());

    [Fact]
    public void CreatedAt_WhenAssignedWithAnOffset_ShouldKeepThatOffset()
    {
        // Arrange
        var createdAt = new DateTimeOffset(2026, 3, 1, 12, 0, 0, TimeSpan.FromHours(2));

        // Act
        var entity = new IntKeyed { CreatedAt = createdAt };

        // Assert
        Assert.Equal(createdAt, entity.CreatedAt);
        Assert.Equal(TimeSpan.FromHours(2), entity.CreatedAt.Offset);
    }

    [Fact]
    public void EntityBase_WhenTheGenericSubclassIsUsed_ShouldStillBeTheNonGenericBase()
        => Assert.IsType<EntityBase>(new IntKeyed(), exactMatch: false);

    private sealed class IntKeyed : EntityBase<int>;

    private sealed class GuidKeyed : EntityBase<Guid>;
}
