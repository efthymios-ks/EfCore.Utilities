using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Microsoft.EntityFrameworkCore.Utilities.ChangeTracking;

/// <summary>
/// A change captured before the save, when the old values are still known, and finished after it,
/// when database-generated keys finally have real values.
/// </summary>
internal sealed class PendingEntityChange
{
    private readonly EntityEntry _entry;
    private readonly Dictionary<string, object?> _keys = [];
    private readonly Dictionary<string, object?> _oldValues = [];
    private readonly Dictionary<string, object?> _newValues = [];
    private readonly List<PropertyEntry> _unresolvedProperties = [];

    private PendingEntityChange(EntityEntry entry, EntityChangeAction action)
    {
        _entry = entry;
        Action = action;
        TableName = entry.Metadata.GetTableName() ?? entry.Metadata.ClrType.Name;
    }

    public EntityChangeAction Action { get; }

    public string TableName { get; }

    public static PendingEntityChange? Capture(EntityEntry entry)
    {
        var action = entry.State switch
        {
            EntityState.Added => EntityChangeAction.Insert,
            EntityState.Modified => EntityChangeAction.Update,
            EntityState.Deleted => EntityChangeAction.Delete,
            _ => (EntityChangeAction?)null
        };

        if (action is null)
        {
            return null;
        }

        var change = new PendingEntityChange(entry, action.Value);
        change.CaptureProperties();

        return change;
    }

    private void CaptureProperties()
    {
        foreach (var property in _entry.Properties)
        {
            var name = property.Metadata.Name;

            // A generated key is still a placeholder at this point; read it again after the save.
            if (property.IsTemporary)
            {
                _unresolvedProperties.Add(property);
                continue;
            }

            if (property.Metadata.IsPrimaryKey())
            {
                // A deleted entity has no current value worth reading; the key it had is the point.
                _keys[name] = Action is EntityChangeAction.Delete
                    ? property.OriginalValue
                    : property.CurrentValue;
            }

            switch (Action)
            {
                case EntityChangeAction.Insert:
                    _newValues[name] = property.CurrentValue;
                    break;

                case EntityChangeAction.Delete:
                    _oldValues[name] = property.OriginalValue;
                    break;

                case EntityChangeAction.Update when property.IsModified:
                    _oldValues[name] = property.OriginalValue;
                    _newValues[name] = property.CurrentValue;
                    break;
            }
        }
    }

    public void Resolve()
    {
        foreach (var property in _unresolvedProperties)
        {
            var name = property.Metadata.Name;

            if (property.Metadata.IsPrimaryKey())
            {
                _keys[name] = property.CurrentValue;
            }

            _newValues[name] = property.CurrentValue;
        }

        _unresolvedProperties.Clear();
    }

    public EntityChange ToEntityChange(DateTimeOffset changedAt)
        => new()
        {
            ChangedAt = changedAt,
            TableName = TableName,
            Action = Action,
            Keys = JsonSerializer.Serialize(_keys),
            OldValues = _oldValues.Count > 0 ? JsonSerializer.Serialize(_oldValues) : null,
            NewValues = _newValues.Count > 0 ? JsonSerializer.Serialize(_newValues) : null
        };
}
