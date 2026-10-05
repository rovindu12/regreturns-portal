using System.Text.Json;

namespace RegReturns.Application.Auditing;

/// <summary>
/// The JSON document a data-change audit entry carries (ADR 0024), and a reader for it. The document is an array of
/// the items that changed: the aggregate root first (its <c>item</c> is the root's type name), then its child rows by
/// path, such as <c>Values[TOTAL_HQLA]</c>. Each item says how it changed (<see cref="Added"/>,
/// <see cref="Modified"/> or <see cref="Deleted"/>) and lists its audited values by property name, each with a
/// <c>before</c> (absent for an added item) and an <c>after</c> (absent for a deleted one):
/// <code>[{"item":"Submission","change":"Modified","values":{"Status":{"before":"Draft","after":"Submitted"}}}]</code>
/// </summary>
public static class AuditChanges
{
    /// <summary>Name of the item path property.</summary>
    public const string ItemProperty = "item";

    /// <summary>Name of the property saying how the item changed.</summary>
    public const string ChangeProperty = "change";

    /// <summary>Name of the object holding the item's values by property name.</summary>
    public const string ValuesProperty = "values";

    /// <summary>Name of a value's old value.</summary>
    public const string BeforeProperty = "before";

    /// <summary>Name of a value's new value.</summary>
    public const string AfterProperty = "after";

    /// <summary>The item was created.</summary>
    public const string Added = "Added";

    /// <summary>The item existed and some of its values changed.</summary>
    public const string Modified = "Modified";

    /// <summary>The item was deleted.</summary>
    public const string Deleted = "Deleted";

    /// <summary>Reads a change document for display.</summary>
    /// <param name="json">The document, as stored.</param>
    /// <returns>The items, or <see langword="null"/> when there is no document or it is not in the expected shape.</returns>
    public static IReadOnlyList<AuditChangeItem>? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            var items = new List<AuditChangeItem>();
            foreach (var element in document.RootElement.EnumerateArray())
            {
                if (ReadItem(element) is not { } item)
                {
                    return null;
                }

                items.Add(item);
            }

            return items;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static AuditChangeItem? ReadItem(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object
            || !element.TryGetProperty(ItemProperty, out var item) || item.ValueKind != JsonValueKind.String
            || !element.TryGetProperty(ChangeProperty, out var change) || change.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var values = new List<AuditValueChange>();
        if (element.TryGetProperty(ValuesProperty, out var properties) && properties.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in properties.EnumerateObject())
            {
                var before = property.Value.ValueKind == JsonValueKind.Object && property.Value.TryGetProperty(BeforeProperty, out var b) ? Text(b) : null;
                var after = property.Value.ValueKind == JsonValueKind.Object && property.Value.TryGetProperty(AfterProperty, out var a) ? Text(a) : null;
                values.Add(new AuditValueChange(property.Name, before, after));
            }
        }

        return new AuditChangeItem(item.GetString()!, change.GetString()!, values);
    }

    private static string? Text(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Null or JsonValueKind.Undefined => null,
        JsonValueKind.String => value.GetString(),
        JsonValueKind.True => bool.TrueString,
        JsonValueKind.False => bool.FalseString,
        JsonValueKind.Array => string.Join(", ", value.EnumerateArray().Select(element => Text(element) ?? "(none)")),

        // Numbers keep the exact text they were written with (invariant culture, no exponent).
        _ => value.GetRawText(),
    };
}

/// <summary>One changed item of a data-change audit entry.</summary>
/// <param name="Item">The root's type name, or a child row's path such as <c>Values[TOTAL_HQLA]</c>.</param>
/// <param name="Change"><see cref="AuditChanges.Added"/>, <see cref="AuditChanges.Modified"/> or <see cref="AuditChanges.Deleted"/>.</param>
/// <param name="Values">The audited values, in property-name order.</param>
public sealed record AuditChangeItem(string Item, string Change, IReadOnlyList<AuditValueChange> Values);

/// <summary>One audited value of a changed item, as display text.</summary>
/// <param name="Property">The property name.</param>
/// <param name="Before">The old value; <see langword="null"/> when it was empty or the item is new.</param>
/// <param name="After">The new value; <see langword="null"/> when it is empty or the item was deleted.</param>
public sealed record AuditValueChange(string Property, string? Before, string? After);
