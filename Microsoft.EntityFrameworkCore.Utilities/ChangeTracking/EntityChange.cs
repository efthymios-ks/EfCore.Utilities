using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Microsoft.EntityFrameworkCore.Utilities.ChangeTracking;

/// <summary>One row per entity touched by a save, with the values as JSON.</summary>
[Table("__EntityChanges")]
public sealed class EntityChange
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    public DateTimeOffset ChangedAt { get; set; }

    [Required]
    public string TableName { get; set; } = string.Empty;

    public EntityChangeAction Action { get; set; }

    /// <summary>The primary key, as JSON, so a composite key needs no special casing.</summary>
    [Required]
    public string Keys { get; set; } = string.Empty;

    /// <summary>Null for an insert.</summary>
    public string? OldValues { get; set; }

    /// <summary>Null for a delete.</summary>
    public string? NewValues { get; set; }
}
