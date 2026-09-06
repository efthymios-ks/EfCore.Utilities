using Microsoft.EntityFrameworkCore.Utilities.Entities;

namespace Microsoft.EntityFrameworkCore.Utilities.Tests.Service.Shared;

/// <summary>A database-generated key, so the audit rows have to resolve it after the save.</summary>
public sealed class Product : EntityBase<int>
{
    public string Name { get; set; } = string.Empty;

    public decimal Price { get; set; }
}

/// <summary>A caller-assigned key, so a test can seed known ids without IDENTITY_INSERT.</summary>
public sealed class Customer : EntityBase<Guid>
{
    public string Email { get; set; } = string.Empty;

    public int LoyaltyPoints { get; set; }
}
