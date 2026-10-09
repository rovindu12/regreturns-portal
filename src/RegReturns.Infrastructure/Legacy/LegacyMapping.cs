using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

using RegReturns.Application.Migration;
using RegReturns.Domain.Common;

namespace RegReturns.Infrastructure.Legacy;

/// <summary>
/// The JSON mapping from a legacy system's exports to RegReturns (ADR 0029): how to cleanse values, which bank names
/// mean which bank, and which legacy column holds which template field. Unknown JSON members are refused, so a typo
/// in the mapping fails the run instead of silently dropping a rule.
/// </summary>
internal sealed class LegacyMapping
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    /// <summary>Gets a description of the legacy system, for the reports.</summary>
    public string? System { get; init; }

    /// <summary>Gets the date formats to try, in order (.NET custom formats, invariant culture).</summary>
    public IReadOnlyList<string> DateFormats { get; init; } = [];

    /// <summary>Gets a value indicating whether a five-digit number in a date column is an Excel serial date.</summary>
    public bool ExcelSerialDates { get; init; }

    /// <summary>Gets the tokens that mean "no value", matched ignoring case.</summary>
    public IReadOnlyList<string> NullTokens { get; init; } = [];

    /// <summary>Gets the currency codes that may prefix or follow an amount.</summary>
    public IReadOnlyList<string> CurrencyCodes { get; init; } = [];

    /// <summary>Gets the legacy names of each bank, keyed by RegReturns bank code.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> Institutions { get; init; } = new Dictionary<string, IReadOnlyList<string>>();

    /// <summary>Gets the files to migrate.</summary>
    public IReadOnlyList<LegacyFileMapping> Files { get; init; } = [];

    /// <summary>Gets the cleansing rules for values.</summary>
    [JsonIgnore]
    public CleansingRules Rules => new(DateFormats, ExcelSerialDates, NullTokens, CurrencyCodes);

    /// <summary>Reads and checks a mapping file.</summary>
    /// <param name="json">The file's bytes.</param>
    /// <returns>The mapping and its SHA-256, or <see cref="MigrationErrors.MappingInvalid"/> naming every problem.</returns>
    public static Result<(LegacyMapping Mapping, string Sha256)> Parse(byte[] json)
    {
        ArgumentNullException.ThrowIfNull(json);
        LegacyMapping? mapping;
        try
        {
            mapping = JsonSerializer.Deserialize<LegacyMapping>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            return MigrationErrors.MappingInvalid.WithMessage($"The mapping file is not valid JSON: {ex.Message}");
        }

        if (mapping is null)
        {
            return MigrationErrors.MappingInvalid.WithMessage("The mapping file is empty.");
        }

        var problems = mapping.Check();
        return problems.Count > 0
            ? MigrationErrors.MappingInvalid.WithMessage("The mapping file is not valid: " + string.Join(" ", problems))
            : (mapping, Convert.ToHexStringLower(SHA256.HashData(json)));
    }

    /// <summary>Builds the lookup from cleansed legacy bank names to bank codes.</summary>
    /// <returns>The lookup; every bank code also names itself.</returns>
    public IReadOnlyDictionary<string, string> InstitutionLookup()
    {
        var lookup = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (code, names) in Institutions)
        {
            foreach (var name in names.Append(code))
            {
                lookup.TryAdd(LegacyCleansing.NormalizeName(name), code);
            }
        }

        return lookup;
    }

    private List<string> Check()
    {
        var problems = new List<string>();
        if (DateFormats.Count == 0)
        {
            problems.Add("List at least one date format.");
        }

        problems.AddRange(DateFormats.Where(f => !IsUsableDateFormat(f)).Select(f => $"Date format '{f}' cannot read back a date it writes."));
        if (Institutions.Count == 0)
        {
            problems.Add("Map at least one bank.");
        }

        var owners = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (code, names) in Institutions)
        {
            foreach (var name in (names ?? []).Append(code).Select(LegacyCleansing.NormalizeName))
            {
                if (name.Length == 0)
                {
                    problems.Add($"Bank {code} has a blank name.");
                }
                else if (owners.TryGetValue(name, out var owner) && owner != code)
                {
                    problems.Add($"The name '{name}' is mapped to both {owner} and {code}.");
                }
                else
                {
                    owners[name] = code;
                }
            }
        }

        if (Files.Count == 0)
        {
            problems.Add("List at least one file.");
        }

        problems.AddRange(Files.GroupBy(f => f.File, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1)
            .Select(g => $"File '{g.Key}' is listed more than once."));
        problems.AddRange(Files.SelectMany(f => f.Check()));
        return problems;
    }

    private static bool IsUsableDateFormat(string? format)
    {
        if (string.IsNullOrWhiteSpace(format))
        {
            return false;
        }

        var sample = new DateTime(2024, 11, 30, 14, 5, 0, DateTimeKind.Unspecified);
        try
        {
            var text = sample.ToString(format, CultureInfo.InvariantCulture);
            return DateTime.TryParseExact(text, format, CultureInfo.InvariantCulture, DateTimeStyles.None, out var back)
                && back.Date == sample.Date;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}

/// <summary>How one legacy file maps onto a return type.</summary>
internal sealed class LegacyFileMapping
{
    /// <summary>Gets the file name, without a folder.</summary>
    public string File { get; init; } = string.Empty;

    /// <summary>Gets the return type code.</summary>
    public string ReturnType { get; init; } = string.Empty;

    /// <summary>Gets the column holding the bank name.</summary>
    public string InstitutionColumn { get; init; } = string.Empty;

    /// <summary>Gets the column holding the reporting date, the last day of the period.</summary>
    public string PeriodEndColumn { get; init; } = string.Empty;

    /// <summary>Gets the column holding when the bank filed the return.</summary>
    public string SubmittedColumn { get; init; } = string.Empty;

    /// <summary>Gets the column holding when the regulator approved the return.</summary>
    public string ApprovedColumn { get; init; } = string.Empty;

    /// <summary>Gets the template field code of each value column, keyed by legacy column name.</summary>
    public IReadOnlyDictionary<string, string> Fields { get; init; } = new Dictionary<string, string>();

    /// <summary>Gets the columns the migration deliberately leaves behind, such as free-text remarks.</summary>
    public IReadOnlyList<string> IgnoredColumns { get; init; } = [];

    /// <summary>Gets every column the mapping names.</summary>
    [JsonIgnore]
    public IEnumerable<string> Columns =>
        new[] { InstitutionColumn, PeriodEndColumn, SubmittedColumn, ApprovedColumn }.Concat(Fields.Keys).Concat(IgnoredColumns);

    /// <summary>Returns the problems with this file's mapping.</summary>
    /// <returns>The problems, if any.</returns>
    public IEnumerable<string> Check()
    {
        var name = string.IsNullOrWhiteSpace(File) ? "(unnamed)" : File;
        if (string.IsNullOrWhiteSpace(File) || File.IndexOfAny(['/', '\\']) >= 0 || File != File.Trim())
        {
            yield return $"File '{name}' must be a plain file name.";
        }

        if (string.IsNullOrWhiteSpace(ReturnType))
        {
            yield return $"File '{name}' needs a return type.";
        }

        var roles = new[] { InstitutionColumn, PeriodEndColumn, SubmittedColumn, ApprovedColumn };
        if (roles.Any(string.IsNullOrWhiteSpace))
        {
            yield return $"File '{name}' must name its institution, period end, submitted and approved columns.";
        }

        if (Fields.Count == 0)
        {
            yield return $"File '{name}' maps no value columns.";
        }

        foreach (var duplicate in Columns.Where(c => !string.IsNullOrWhiteSpace(c))
                     .GroupBy(c => c.Trim(), StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
        {
            yield return $"File '{name}' names column '{duplicate.Key}' more than once.";
        }

        foreach (var field in Fields.Values.GroupBy(f => f, StringComparer.Ordinal).Where(g => g.Count() > 1))
        {
            yield return $"File '{name}' maps several columns to field {field.Key}.";
        }
    }
}
