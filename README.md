# Microsoft.EntityFrameworkCore.Utilities

Audit timestamps, a change log, and the `DbSet<T>` set operations EF Core leaves out. A demo, not a
package — clone it and copy what is useful.

```
Entities/          EntityBase, EntityBase<TId>
Interceptors/      AuditableTimestampInterceptor, EntityChangeInterceptor
DbContexts/        AuditableDbContext
Extensions/        DbSetExtensions
ChangeTracking/    EntityChange, EntityChangeAction
```

## Setup

Inherit the base, or register the interceptors on any context.

```csharp
public sealed class ShopDbContext(DbContextOptions options, TimeProvider timeProvider)
    : AuditableDbContext(options, timeProvider)
{
    public DbSet<Product> Products => Set<Product>();
}
```

```csharp
options
    .UseSqlServer(connectionString)
    .AddInterceptors(
        new AuditableTimestampInterceptor(timeProvider),
        new EntityChangeInterceptor(timeProvider)
    );
```

## Entities

Inherit `EntityBase<TId>` for a key plus timestamps.

```csharp
public sealed class Product : EntityBase<int>
{
    public string Name { get; set; } = string.Empty;

    public decimal Price { get; set; }
}
```

| Property | Type | Set by |
| --- | --- | --- |
| `Id` | `TId` | the database, on insert |
| `CreatedAt` | `DateTimeOffset` | the interceptor, on insert |
| `ModifiedAt` | `DateTimeOffset?` | the interceptor, on update — null until then |

## Timestamps

Set on save. Nothing to remember at the call site.

```csharp
context.Products.Add(product);
await context.SaveChangesAsync();

Console.WriteLine(product.CreatedAt);   // stamped
Console.WriteLine(product.ModifiedAt);  // null
```

`CreatedAt` is protected on update — assigning it yourself does not win. One timestamp per save, so
everything written together shares an instant. Pass `FakeTimeProvider` to freeze the clock in tests.

## Change log

Every insert, update and delete lands in `__EntityChanges` with the values as JSON.

```csharp
product.Price = 99m;
await context.SaveChangesAsync();

var change = await context.EntityChanges.LastAsync();

// Update | {"Id":1} | {"Price":10.00} | {"Price":99.00,"ModifiedAt":"..."}
Console.WriteLine($"{change.Action} | {change.Keys} | {change.OldValues} | {change.NewValues}");
```

| Column | Contents |
| --- | --- |
| `TableName` | table the change was made to |
| `Action` | `Insert`, `Update` or `Delete` |
| `Keys` | primary key as JSON — a composite key needs no special casing |
| `OldValues` | null for an insert |
| `NewValues` | null for a delete |
| `ChangedAt` | the save's timestamp |

An update records only what changed. Generated keys are resolved after the save, so an insert's
`Keys` holds the real id. The audit rows are never themselves audited.

Audit rows need a second save, so both run in one transaction. If you opened a transaction, it uses
yours and leaves it for you to commit.

## DbSet extensions

Decide per entity whether it is new, in one query rather than one per entity.

```csharp
var result = await context.Products.AddOrUpdateManyAsync<Product, int>(products);

Console.WriteLine($"{result.Added.Count} added, {result.Updated.Count} updated");
```

```csharp
await context.Products.AddManyIfMissingAsync<Product, int>(products);
await context.Products.UpdateManyIfPresentAsync<Product, int>(products);

var removed = await context.Products.RemoveByKeyAsync<Product, int>(id);
var exists = await context.Products.ExistsAsync<Product, int>(id);

await context.SaveChangesAsync();
```

| Method | Does |
| --- | --- |
| `AddManyIfMissingAsync` | adds only the keys not already present |
| `UpdateManyIfPresentAsync` | updates only the keys already present |
| `AddOrUpdateManyAsync` | both, returning `UpsertResult<T>` |
| `RemoveByKeyAsync` | removes one row; false when there was none |
| `ExistsAsync` | untracked existence check |

They stage the change — call `SaveChangesAsync` yourself. Unassigned keys are never probed. The same
assigned key twice is rejected up front, rather than as an EF tracking conflict later.

Service tests run against SQL Server 2022 via Testcontainers, so Docker must be running.
