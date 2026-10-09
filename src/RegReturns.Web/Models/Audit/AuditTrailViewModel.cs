using System.Globalization;

using RegReturns.Application.Auditing;
using RegReturns.Domain.Auditing;

namespace RegReturns.Web.Models.Audit;

/// <summary>
/// The audit trail filters, as sent in the query string. The action filter is <c>auditAction</c>, not <c>action</c>,
/// which MVC reserves for the action name in route values.
/// </summary>
public sealed class AuditTrailFilterInput
{
    /// <summary>Gets or sets the 1-based page number.</summary>
    public int? Page { get; set; }

    /// <summary>Gets or sets the action to show, as an <see cref="Domain.Auditing.AuditAction"/> member name.</summary>
    public string? AuditAction { get; set; }

    /// <summary>Gets or sets the entity type to show.</summary>
    public string? EntityType { get; set; }

    /// <summary>Gets or sets the entity id to show.</summary>
    public string? EntityId { get; set; }

    /// <summary>Gets or sets the actor to show: a subject id or part of a display name.</summary>
    public string? Actor { get; set; }

    /// <summary>Gets the action filter, or <see langword="null"/> when none or not a known action.</summary>
    public AuditAction? ParsedAction =>
        Enum.TryParse<AuditAction>(AuditAction, ignoreCase: true, out var action) && Enum.IsDefined(action) ? action : null;

    /// <summary>Gets a value indicating whether any filter is set.</summary>
    public bool IsFiltered =>
        ParsedAction is not null || !string.IsNullOrWhiteSpace(EntityType) || !string.IsNullOrWhiteSpace(EntityId) || !string.IsNullOrWhiteSpace(Actor);

    /// <summary>Builds the query for the page.</summary>
    /// <returns>The query.</returns>
    public GetAuditTrail ToQuery() => new(Page ?? 1, ParsedAction, EntityType, EntityId, Actor);

    /// <summary>Returns the query string values of these filters on another page, for paging links.</summary>
    /// <param name="page">The page to link to.</param>
    /// <returns>The route values; empty filters are left out.</returns>
    public Dictionary<string, string> RouteValues(int page)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        Add(values, "auditAction", ParsedAction?.ToString());
        Add(values, "entityType", EntityType);
        Add(values, "entityId", EntityId);
        Add(values, "actor", Actor);
        if (page > 1)
        {
            values["page"] = page.ToString(CultureInfo.InvariantCulture);
        }

        return values;
    }

    /// <summary>Returns the query string values that show one entity's history.</summary>
    /// <param name="entityType">The entity type.</param>
    /// <param name="entityId">The entity id.</param>
    /// <returns>The route values.</returns>
    public static Dictionary<string, string> EntityHistory(string entityType, string entityId) =>
        new(StringComparer.Ordinal) { ["entityType"] = entityType, ["entityId"] = entityId };

    private static void Add(Dictionary<string, string> values, string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            values[name] = value.Trim();
        }
    }
}

/// <summary>The audit trail page.</summary>
/// <param name="Trail">The entries on this page and the paging totals.</param>
/// <param name="Filter">The filters in force.</param>
public sealed record AuditTrailViewModel(AuditTrailPage Trail, AuditTrailFilterInput Filter);

/// <summary>Labels, badges and formatting for the audit screens.</summary>
public static class AuditDisplay
{
    /// <summary>The format times are shown in (always UTC).</summary>
    public const string TimeFormat = "yyyy-MM-dd HH:mm:ss";

    /// <summary>Shown for a value that is empty or does not exist on that side of a change.</summary>
    public const string NoValue = "—";

    /// <summary>Gets every action, in the order the filter lists them.</summary>
    public static IReadOnlyList<AuditAction> Actions { get; } = Enum.GetValues<AuditAction>();

    /// <summary>Returns the name of an action.</summary>
    /// <param name="action">The action.</param>
    /// <returns>The label.</returns>
    public static string ActionLabel(AuditAction action) => action switch
    {
        AuditAction.SignIn => "Signed in",
        AuditAction.SignOut => "Signed out",
        AuditAction.AccessDenied => "Access denied",
        AuditAction.AuthenticationFailed => "Authentication failed",
        AuditAction.Created => "Created",
        AuditAction.Updated => "Updated",
        AuditAction.Deleted => "Deleted",
        AuditAction.StateChanged => "Status changed",
        AuditAction.ChainVerified => "Chain verified",
        AuditAction.ReportExported => "Report exported",
        AuditAction.InsightGenerated => "Insight generated",
        AuditAction.DemoReset => "Demo reset",
        AuditAction.TotpEnrolmentOpened => "Authenticator reset",
        _ => action.ToString(),
    };

    /// <summary>Returns the Bootstrap badge classes of an action (the badge always shows the label too).</summary>
    /// <param name="action">The action.</param>
    /// <returns>The CSS classes.</returns>
    public static string ActionBadge(AuditAction action) => action switch
    {
        AuditAction.AccessDenied or AuditAction.AuthenticationFailed => "text-bg-danger",
        AuditAction.Created => "text-bg-success",
        AuditAction.Updated => "text-bg-primary",
        AuditAction.Deleted => "text-bg-dark",
        AuditAction.StateChanged => "text-bg-info",
        AuditAction.ChainVerified => "text-bg-secondary",
        AuditAction.ReportExported => "text-bg-warning",
        AuditAction.InsightGenerated => "text-bg-secondary",
        AuditAction.DemoReset => "text-bg-warning",
        AuditAction.TotpEnrolmentOpened => "text-bg-danger",
        _ => "text-bg-light border",
    };

    /// <summary>Returns the name of an actor type.</summary>
    /// <param name="type">The actor type.</param>
    /// <returns>The label.</returns>
    public static string ActorTypeLabel(ActorType type) => type switch
    {
        ActorType.User => "user",
        ActorType.ApiClient => "API client",
        ActorType.System => "system",
        _ => "anonymous",
    };

    /// <summary>Formats a time in UTC.</summary>
    /// <param name="time">The time.</param>
    /// <returns>The text, such as <c>2026-10-04 12:00:00 UTC</c>.</returns>
    public static string Time(DateTimeOffset time) =>
        time.UtcDateTime.ToString(TimeFormat, CultureInfo.InvariantCulture) + " UTC";

    /// <summary>Describes how an item of a data change changed.</summary>
    /// <param name="change">The change, as stored.</param>
    /// <returns>The label.</returns>
    public static string ChangeLabel(string change) => change switch
    {
        AuditChanges.Added => "added",
        AuditChanges.Deleted => "removed",
        AuditChanges.Modified => "changed",
        _ => change,
    };

    /// <summary>Shows a before or after value.</summary>
    /// <param name="value">The value.</param>
    /// <returns>The value, or <see cref="NoValue"/>.</returns>
    public static string Value(string? value) => string.IsNullOrEmpty(value) ? NoValue : value;
}
