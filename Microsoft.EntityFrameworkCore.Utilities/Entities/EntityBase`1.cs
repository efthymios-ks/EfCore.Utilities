using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Microsoft.EntityFrameworkCore.Utilities.Entities;

/// <inheritdoc cref="EntityBase"/>
public abstract class EntityBase<TKey> : EntityBase
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public TKey Id { get; set; } = default!;

    public override string ToString()
        => $"{GetType().Name} {Id}";
}
