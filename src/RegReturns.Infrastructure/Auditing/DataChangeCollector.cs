using System.Buffers;
using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;

using RegReturns.Application.Auditing;
using RegReturns.Domain.Auditing;

namespace RegReturns.Infrastructure.Auditing;

/// <summary>One aggregate and everything that changed under it in one save, ready to record.</summary>
/// <param name="Action">Created, Updated, Deleted or StateChanged.</param>
/// <param name="EntityType">The aggregate root's CLR type name, such as <c>Submission</c>.</param>
/// <param name="EntityId">The aggregate root's key.</param>
/// <param name="Details">A short summary of a status step, or <see langword="null"/>.</param>
/// <param name="Changes">The change document (<see cref="AuditChanges"/>).</param>
public sealed record DataChange(AuditAction Action, string EntityType, string EntityId, string? Details, string Changes);

/// <summary>
/// Reads the change tracker and groups what is about to be saved by aggregate root (ADR 0024). An entity is a child of
/// its principal when the principal reaches it through a collection navigation (or owns it); everything else is a root.
/// A child row is identified by a readable key when a unique index pairs its parent key with one text property (a
/// value's field code, a field's or rule's code), and by its id otherwise. Keys, the parent key, concurrency tokens and
/// properties marked <see cref="NotAuditedAttribute"/> are left out, and neither <see cref="AuditEntry"/> nor an entity
/// type marked <see cref="NotAuditedAttribute"/> is ever audited.
/// </summary>
internal static class DataChangeCollector
{
    /// <summary>The root property whose change makes an entry <see cref="AuditAction.StateChanged"/>.</summary>
    public const string StatusProperty = "Status";

    // All of Unicode stays readable; quotes, control characters and HTML-sensitive characters are still escaped, so the
    // document never contains the canonical form's field separator.
    private static readonly JsonWriterOptions WriterOptions = new() { Encoder = JavaScriptEncoder.Create(UnicodeRanges.All) };

    /// <summary>Describes the pending changes, one <see cref="DataChange"/> per aggregate root, ordered by type and id.</summary>
    /// <param name="changeTracker">The change tracker, after change detection.</param>
    /// <returns>The changes; empty when nothing audited is about to change.</returns>
    public static IReadOnlyList<DataChange> Collect(ChangeTracker changeTracker)
    {
        ArgumentNullException.ThrowIfNull(changeTracker);
        var tracked = changeTracker.Entries().Where(e => e.Entity is not AuditEntry && !IsNotAudited(e.Metadata.ClrType)).ToList();
        var locator = new RootLocator(tracked);
        var groups = new Dictionary<(string Type, string Id), Group>();
        foreach (var entry in tracked.Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted))
        {
            var location = locator.Locate(entry);
            var values = AuditedValues(entry, location.Parent);
            if (entry.State == EntityState.Modified && values.Count == 0)
            {
                continue;
            }

            var key = (location.RootType, location.RootId);
            if (!groups.TryGetValue(key, out var group))
            {
                group = new Group(location.RootType, location.RootId);
                groups.Add(key, group);
            }

            group.Add(new Item(location.Path, entry.State, values));
        }

        return [.. groups.Values
            .OrderBy(g => g.RootType, StringComparer.Ordinal)
            .ThenBy(g => g.RootId, StringComparer.Ordinal)
            .Select(g => g.ToDataChange())];
    }

    /// <summary>Returns how an entity type hangs off its aggregate root, or <see langword="null"/> for a root.</summary>
    /// <param name="type">The entity type.</param>
    /// <returns>The foreign key to its parent.</returns>
    internal static IForeignKey? ParentKey(IEntityType type) =>
        type.GetForeignKeys().FirstOrDefault(fk => fk.IsOwnership || fk.PrincipalToDependent is { IsCollection: true });

    /// <summary>Returns the readable key of a child type: the text property a unique index pairs with its parent key.</summary>
    /// <param name="type">The child entity type.</param>
    /// <param name="parent">The foreign key to its parent.</param>
    /// <returns>The property, or <see langword="null"/> when rows are identified by their id.</returns>
    internal static IProperty? ReadableKey(IEntityType type, IForeignKey parent) =>
        type.GetIndexes()
            .Where(index => index.IsUnique && index.Properties.Count == parent.Properties.Count + 1)
            .Where(index => parent.Properties.All(index.Properties.Contains))
            .Select(index => index.Properties.Single(p => !parent.Properties.Contains(p)))
            .FirstOrDefault(p => p.ClrType == typeof(string));

    private static SortedDictionary<string, ValueChange> AuditedValues(EntityEntry entry, IForeignKey? parent)
    {
        var type = entry.Metadata;
        var skipped = new HashSet<IProperty>(type.FindPrimaryKey()?.Properties ?? []);
        if (parent is not null)
        {
            skipped.UnionWith(parent.Properties);
            if (entry.State != EntityState.Modified && ReadableKey(type, parent) is { } readable)
            {
                // Added and deleted rows carry it in their item path already.
                skipped.Add(readable);
            }
        }

        var values = new SortedDictionary<string, ValueChange>(StringComparer.Ordinal);
        AddValues(values, entry.State, entry.Properties.Where(p => !skipped.Contains(p.Metadata)), prefix: null);
        foreach (var complex in entry.ComplexProperties)
        {
            AddComplexValues(values, entry.State, complex, complex.Metadata.Name);
        }

        return values;
    }

    private static void AddComplexValues(SortedDictionary<string, ValueChange> values, EntityState state, ComplexPropertyEntry complex, string prefix)
    {
        if (IsNotAudited(complex.Metadata.PropertyInfo))
        {
            return;
        }

        AddValues(values, state, complex.Properties, prefix);
        foreach (var nested in complex.ComplexProperties)
        {
            AddComplexValues(values, state, nested, $"{prefix}.{nested.Metadata.Name}");
        }
    }

    private static void AddValues(SortedDictionary<string, ValueChange> values, EntityState state, IEnumerable<PropertyEntry> properties, string? prefix)
    {
        foreach (var property in properties)
        {
            var metadata = property.Metadata;
            if (metadata.IsConcurrencyToken || IsNotAudited(metadata.PropertyInfo))
            {
                continue;
            }

            var name = prefix is null ? metadata.Name : $"{prefix}.{metadata.Name}";
            switch (state)
            {
                case EntityState.Added when property.CurrentValue is not null:
                    values[name] = ValueChange.Created(property.CurrentValue);
                    break;
                case EntityState.Deleted when property.OriginalValue is not null:
                    values[name] = ValueChange.Removed(property.OriginalValue);
                    break;
                case EntityState.Modified when property.IsModified
                    && !metadata.GetValueComparer().Equals(property.OriginalValue, property.CurrentValue):
                    values[name] = new ValueChange(true, property.OriginalValue, true, property.CurrentValue);
                    break;
            }
        }
    }

    private static bool IsNotAudited(PropertyInfo? property) =>
        property?.GetCustomAttribute<NotAuditedAttribute>(inherit: true) is not null;

    private static bool IsNotAudited(Type entityType) => entityType.GetCustomAttribute<NotAuditedAttribute>(inherit: true) is not null;

    /// <summary>Where a tracked entity sits: its aggregate root and its path below it.</summary>
    /// <param name="RootType">The root's CLR type name.</param>
    /// <param name="RootId">The root's key.</param>
    /// <param name="Path">The path below the root; empty for the root itself.</param>
    /// <param name="Parent">The foreign key to the entity's parent, or <see langword="null"/> for a root.</param>
    private sealed record Location(string RootType, string RootId, string Path, IForeignKey? Parent);

    /// <summary>A before or after value; a side is absent for added and deleted items.</summary>
    private sealed record ValueChange(bool HasBefore, object? Before, bool HasAfter, object? After)
    {
        public static ValueChange Created(object? after) => new(false, null, true, after);

        public static ValueChange Removed(object? before) => new(true, before, false, null);
    }

    private sealed record Item(string Path, EntityState State, SortedDictionary<string, ValueChange> Values)
    {
        // Deleted, then modified, then added: a row replaced under the same key reads in the order it happened.
        public int StateOrder => State switch
        {
            EntityState.Deleted => 0,
            EntityState.Modified => 1,
            _ => 2,
        };

        public string Change => State switch
        {
            EntityState.Added => AuditChanges.Added,
            EntityState.Deleted => AuditChanges.Deleted,
            _ => AuditChanges.Modified,
        };
    }

    private sealed class Group(string rootType, string rootId)
    {
        private readonly List<Item> _children = [];
        private Item? _root;

        public string RootType { get; } = rootType;

        public string RootId { get; } = rootId;

        public void Add(Item item)
        {
            if (item.Path.Length == 0)
            {
                _root = item;
            }
            else
            {
                _children.Add(item);
            }
        }

        public DataChange ToDataChange()
        {
            var status = _root?.Values.GetValueOrDefault(StatusProperty);
            var action = _root?.State switch
            {
                EntityState.Added => AuditAction.Created,
                EntityState.Deleted => AuditAction.Deleted,
                EntityState.Modified when status is not null => AuditAction.StateChanged,
                _ => AuditAction.Updated,
            };
            var details = action == AuditAction.StateChanged
                ? $"{StatusProperty} changed from {DisplayText(status!.Before)} to {DisplayText(status.After)}."
                : null;
            return new DataChange(action, RootType, RootId, details, Document());
        }

        private static void WriteValue(Utf8JsonWriter writer, object? value)
        {
            switch (value)
            {
                case null:
                    writer.WriteNullValue();
                    break;
                case string text:
                    writer.WriteStringValue(text);
                    break;
                case bool flag:
                    writer.WriteBooleanValue(flag);
                    break;
                case Enum named:
                    writer.WriteStringValue(named.ToString());
                    break;
                case Guid guid:
                    writer.WriteStringValue(guid.ToString("D", CultureInfo.InvariantCulture));
                    break;
                case DateTimeOffset moment:
                    writer.WriteStringValue(moment.UtcDateTime.ToString("O", CultureInfo.InvariantCulture));
                    break;
                case DateTime dateTime:
                    writer.WriteStringValue(dateTime.ToString("O", CultureInfo.InvariantCulture));
                    break;
                case DateOnly date:
                    writer.WriteStringValue(date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                    break;
                case decimal number:
                    // Without trailing zeros, so 1000 read back as 1000.0000 and 1000 as entered are written the same way.
                    writer.WriteRawValue(number.ToString("0.############################", CultureInfo.InvariantCulture));
                    break;
                case int or short or byte or sbyte or ushort:
                    writer.WriteNumberValue(Convert.ToInt32(value, CultureInfo.InvariantCulture));
                    break;
                case long whole:
                    writer.WriteNumberValue(whole);
                    break;
                case uint whole:
                    writer.WriteNumberValue(whole);
                    break;
                case ulong whole:
                    writer.WriteNumberValue(whole);
                    break;
                case double real:
                    writer.WriteNumberValue(real);
                    break;
                case float real:
                    writer.WriteNumberValue(real);
                    break;
                case byte[] content:
                    writer.WriteStringValue(string.Create(CultureInfo.InvariantCulture, $"({content.Length} bytes)"));
                    break;
                case IEnumerable items:
                    writer.WriteStartArray();
                    foreach (var item in items)
                    {
                        WriteValue(writer, item);
                    }

                    writer.WriteEndArray();
                    break;
                case IFormattable formattable:
                    writer.WriteStringValue(formattable.ToString(null, CultureInfo.InvariantCulture));
                    break;
                default:
                    writer.WriteStringValue(value.ToString());
                    break;
            }
        }

        private static string DisplayText(object? value)
        {
            var buffer = new ArrayBufferWriter<byte>();
            using (var writer = new Utf8JsonWriter(buffer, WriterOptions))
            {
                writer.WriteStartArray();
                WriteValue(writer, value);
                writer.WriteEndArray();
            }

            using var document = JsonDocument.Parse(buffer.WrittenMemory);
            var element = document.RootElement[0];
            return element.ValueKind == JsonValueKind.String ? element.GetString()! : element.GetRawText();
        }

        private string Document()
        {
            var buffer = new ArrayBufferWriter<byte>();
            using (var writer = new Utf8JsonWriter(buffer, WriterOptions))
            {
                writer.WriteStartArray();
                var items = _children
                    .OrderBy(c => c.Path, StringComparer.Ordinal)
                    .ThenBy(c => c.StateOrder);
                foreach (var item in _root is null ? items : items.Prepend(_root))
                {
                    WriteItem(writer, item);
                }

                writer.WriteEndArray();
            }

            return Encoding.UTF8.GetString(buffer.WrittenSpan);
        }

        private void WriteItem(Utf8JsonWriter writer, Item item)
        {
            writer.WriteStartObject();
            writer.WriteString(AuditChanges.ItemProperty, item.Path.Length == 0 ? RootType : item.Path);
            writer.WriteString(AuditChanges.ChangeProperty, item.Change);
            writer.WriteStartObject(AuditChanges.ValuesProperty);
            foreach (var (name, value) in item.Values)
            {
                writer.WriteStartObject(name);
                if (value.HasBefore)
                {
                    writer.WritePropertyName(AuditChanges.BeforeProperty);
                    WriteValue(writer, value.Before);
                }

                if (value.HasAfter)
                {
                    writer.WritePropertyName(AuditChanges.AfterProperty);
                    WriteValue(writer, value.After);
                }

                writer.WriteEndObject();
            }

            writer.WriteEndObject();
            writer.WriteEndObject();
        }
    }

    /// <summary>Finds each changed entity's aggregate root through the foreign keys to its tracked parents.</summary>
    /// <param name="tracked">Every tracked entry, so unchanged parents can be found too.</param>
    private sealed class RootLocator(IReadOnlyList<EntityEntry> tracked)
    {
        private Dictionary<(IEntityType Type, string Key), EntityEntry>? _byKey;

        public Location Locate(EntityEntry entry)
        {
            var type = entry.Metadata;
            var parent = ParentKey(type);
            if (parent is null)
            {
                var key = type.FindPrimaryKey()?.Properties ?? [];
                return new Location(type.ClrType.Name, KeyText(key.Select(p => ValueOf(entry, p))), string.Empty, null);
            }

            var segment = Segment(entry, parent);
            var parentKey = KeyText(parent.Properties.Select(p => ValueOf(entry, p)));
            if (!parent.PrincipalKey.IsPrimaryKey() || Find(parent.PrincipalEntityType, parentKey) is not { } parentEntry)
            {
                // The parent is not loaded: it stands in as the root, which is right for every aggregate in this model.
                return new Location(parent.PrincipalEntityType.ClrType.Name, parentKey, segment, parent);
            }

            var above = Locate(parentEntry);
            return new Location(above.RootType, above.RootId, above.Path.Length == 0 ? segment : $"{above.Path}.{segment}", parent);
        }

        private static string KeyText(IEnumerable<object?> values) =>
            string.Join(",", values.Select(value => value switch
            {
                null => string.Empty,
                Guid guid => guid.ToString("D", CultureInfo.InvariantCulture),
                IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
                _ => value.ToString(),
            }));

        private static object? ValueOf(EntityEntry entry, IProperty property) =>
            entry.State == EntityState.Deleted ? entry.Property(property).OriginalValue : entry.Property(property).CurrentValue;

        private static string Segment(EntityEntry entry, IForeignKey parent)
        {
            var navigation = parent.PrincipalToDependent;
            var name = navigation?.Name ?? entry.Metadata.ClrType.Name;
            if (navigation is { IsCollection: false })
            {
                return name;
            }

            var readable = ReadableKey(entry.Metadata, parent);
            var key = readable is not null
                ? KeyText([ValueOf(entry, readable)])
                : KeyText((entry.Metadata.FindPrimaryKey()?.Properties ?? []).Select(p => ValueOf(entry, p)));
            return $"{name}[{key}]";
        }

        private EntityEntry? Find(IEntityType type, string key)
        {
            _byKey ??= tracked
                .Where(e => e.Metadata.FindPrimaryKey() is not null)
                .GroupBy(e => (e.Metadata, KeyText(e.Metadata.FindPrimaryKey()!.Properties.Select(p => ValueOf(e, p)))))
                .ToDictionary(g => g.Key, g => g.First());
            return _byKey.GetValueOrDefault((type, key));
        }
    }
}
