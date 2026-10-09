using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;

using RegReturns.Application.Insights;
using RegReturns.Domain.Insights;
using RegReturns.Domain.Templates;

namespace RegReturns.Web.Models.Supervision;

/// <summary>The advisory insight panel of a return on the review page (ADR 0030).</summary>
/// <param name="SubmissionId">The return.</param>
/// <param name="Panel">The latest insight of the current revision and the configured provider.</param>
public sealed record ReturnInsightViewModel(Guid SubmissionId, ReturnInsightPanel Panel)
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    private static readonly JsonSerializerOptions Indented = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
    };

    /// <summary>Gets the insight shown, if any.</summary>
    public ReturnInsightView? Latest => Panel.Latest;

    /// <summary>Gets a value indicating whether an AI model wrote the insight shown.</summary>
    public bool IsAiWritten => Latest is { Provider: InsightProvider.Anthropic };

    /// <summary>Gets the sentence under the heading saying who writes insights and what they receive.</summary>
    public string ProviderNote => (Panel.Provider, Panel.ProviderConfigured) switch
    {
        (InsightProvider.Anthropic, true) =>
            $"Written by Claude ({Panel.Model}) through the Anthropic API, from field codes, labels, figures and rule texts only.",
        (InsightProvider.Anthropic, false) =>
            "No API key is configured, so insights are written by fixed rules and nothing leaves the portal.",
        _ => "Insights are written by fixed rules; nothing leaves the portal.",
    };

    /// <summary>Gets the label of the generate button.</summary>
    public string ButtonLabel => Latest is null ? "Generate insight" : "Refresh insight";

    /// <summary>Gets who wrote the insight shown.</summary>
    public string WrittenBy => Latest switch
    {
        null => string.Empty,
        { Provider: InsightProvider.Anthropic } insight => $"Claude ({insight.Model})",
        _ => "fixed rules",
    };

    /// <summary>Gets a value indicating whether the payload of the insight shown left the portal.</summary>
    /// <remarks>
    /// An AI-written insight was sent; so was a fallback after the provider was asked (it timed out, refused or failed).
    /// Without a key, after a failed privacy check and with rule-based insights nothing was sent.
    /// </remarks>
    public bool PayloadSent => Latest switch
    {
        { Provider: InsightProvider.Anthropic } => true,
        { FallbackReason: InsightFallbackReason.NotConfigured or InsightFallbackReason.PayloadRejected or null } => false,
        null => false,
        _ => true,
    };

    /// <summary>Gets the sentence above the payload under "What was shared".</summary>
    public string SharedNote => (Latest, PayloadSent) switch
    {
        (null, _) => string.Empty,
        ({ Provider: InsightProvider.Anthropic }, _) =>
            "This exact document was sent to the AI provider: field codes, labels, figures and rule texts, with no bank, people, comments or justifications.",
        (_, true) => "This exact document was sent to the AI provider, which gave no usable answer, so fixed rules wrote the insight.",
        _ => "Nothing was sent. This is the document the AI provider would have received.",
    };

    /// <summary>Gets the payload shown under "What was shared", indented for reading.</summary>
    public string PayloadForDisplay
    {
        get
        {
            if (Latest is null)
            {
                return string.Empty;
            }

            using var document = JsonDocument.Parse(Latest.Payload);
            return JsonSerializer.Serialize(document.RootElement, Indented);
        }
    }

    /// <summary>Formats a figure with its unit.</summary>
    /// <param name="value">The figure.</param>
    /// <param name="unit">The unit, such as <c>VLD m</c> or <c>%</c>.</param>
    /// <returns>For example <c>1,234.5 VLD m</c> or <c>8.9%</c>.</returns>
    public static string Figure(decimal value, string unit)
    {
        var figure = value.ToString("#,##0.##", Invariant);
        if (string.IsNullOrEmpty(unit))
        {
            return figure;
        }

        return unit == "%" ? figure + "%" : figure + " " + unit;
    }

    /// <summary>Formats a change in percent with its sign.</summary>
    /// <param name="changePercent">The change.</param>
    /// <returns>For example <c>+147.2%</c> or <c>-12%</c>.</returns>
    public static string Change(decimal changePercent) =>
        (changePercent > 0 ? "+" : string.Empty) + changePercent.ToString("0.#", Invariant) + "%";

    /// <summary>Names the period a mover is compared with.</summary>
    /// <param name="basis">The basis.</param>
    /// <returns>The label.</returns>
    public static string Basis(VarianceBasis basis) => basis == VarianceBasis.PreviousPeriod ? "previous period" : "same period last year";

    /// <summary>Formats a duration.</summary>
    /// <param name="milliseconds">The duration.</param>
    /// <returns>For example <c>8.1 s</c>.</returns>
    public static string Duration(int milliseconds) => milliseconds < 1000
        ? string.Create(Invariant, $"{milliseconds} ms")
        : string.Create(Invariant, $"{milliseconds / 1000d:0.#} s");
}
