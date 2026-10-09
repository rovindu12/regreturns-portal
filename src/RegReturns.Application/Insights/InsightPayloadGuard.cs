using System.Text.Json;
using System.Text.RegularExpressions;

using RegReturns.Domain.Common;
using RegReturns.Domain.Periods;
using RegReturns.Domain.Templates;

namespace RegReturns.Application.Insights;

/// <summary>
/// The last check before a payload leaves the system (ADR 0030). Every string in it must be a code or text the regulator
/// wrote in the template, a known enumeration value or a period label, and none may contain an e-mail address. Bank
/// names, people, comments, justifications and text-field values therefore never pass, whatever a future change to
/// the payload adds.
/// </summary>
public static partial class InsightPayloadGuard
{
    /// <summary>The error returned when a payload fails the guard; nothing is sent.</summary>
    public static readonly Error Rejected = new(
        "Insight.PayloadRejected", "The data for the insight did not pass the privacy check, so nothing was sent.");

    /// <summary>Checks a serialised payload against the template it was built from.</summary>
    /// <param name="payloadJson">The payload exactly as it would be sent.</param>
    /// <param name="returnType">The return type.</param>
    /// <param name="template">The template version, with fields and rules.</param>
    /// <returns>Success, or <see cref="Rejected"/> with the JSON path of the first string that failed.</returns>
    public static Result Check(string payloadJson, ReturnType returnType, TemplateVersion template)
    {
        ArgumentNullException.ThrowIfNull(payloadJson);
        ArgumentNullException.ThrowIfNull(returnType);
        ArgumentNullException.ThrowIfNull(template);

        var allowed = AllowedStrings(returnType, template);
        using var document = JsonDocument.Parse(payloadJson);
        return FirstFailure(document.RootElement, "$", allowed) is { } path
            ? Rejected.WithMessage($"{Rejected.Message} Rejected value at {path}.")
            : Result.Success();
    }

    private static HashSet<string> AllowedStrings(ReturnType returnType, TemplateVersion template)
    {
        var allowed = new HashSet<string>(StringComparer.Ordinal)
        {
            InsightRequest.CurrentSchema,
            returnType.Code,
            returnType.Name,
        };
        allowed.UnionWith(Enum.GetNames<ReturnFrequency>());
        allowed.UnionWith(Enum.GetNames<RuleType>());
        allowed.UnionWith(Enum.GetNames<Severity>());
        foreach (var field in template.Fields)
        {
            allowed.UnionWith([field.Code, field.Label, field.Section, field.Unit]);
        }

        foreach (var rule in template.Rules)
        {
            allowed.UnionWith([rule.Code, rule.Message]);
        }

        allowed.RemoveWhere(s => EmailAddress().IsMatch(s));
        return allowed;
    }

    private static string? FirstFailure(JsonElement element, string path, HashSet<string> allowed)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    if (FirstFailure(property.Value, $"{path}.{property.Name}", allowed) is { } failure)
                    {
                        return failure;
                    }
                }

                return null;
            case JsonValueKind.Array:
                var index = 0;
                foreach (var item in element.EnumerateArray())
                {
                    if (FirstFailure(item, $"{path}[{index++}]", allowed) is { } failure)
                    {
                        return failure;
                    }
                }

                return null;
            case JsonValueKind.String:
                var value = element.GetString()!;
                return allowed.Contains(value) || PeriodLabel().IsMatch(value) ? null : path;
            default:
                return null;
        }
    }

    [GeneratedRegex(@"[A-Za-z0-9._%+\-]+@[A-Za-z0-9\-]+(\.[A-Za-z0-9\-]+)+", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex EmailAddress();

    [GeneratedRegex(@"^[0-9]{4}-(0[1-9]|1[0-2]|Q[1-4])\z", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex PeriodLabel();
}
