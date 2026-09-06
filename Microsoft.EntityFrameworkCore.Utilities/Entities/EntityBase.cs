namespace Microsoft.EntityFrameworkCore.Utilities.Entities;

/// <summary>
/// The audit timestamps, on a non-generic base so the interceptors can find every entity
/// regardless of what its key is typed as.
/// </summary>
public abstract class EntityBase
{
    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? ModifiedAt { get; set; }
}
