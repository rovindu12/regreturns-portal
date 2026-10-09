using System.Text;

using RegReturns.Infrastructure.Legacy;

namespace RegReturns.UnitTests.Infrastructure.Legacy;

/// <summary>Cleansing rules, mappings and legacy tables for the legacy migration tests.</summary>
internal static class LegacyTestData
{
    /// <summary>A valid mapping file with two banks and one file, as JSON.</summary>
    public const string MappingJson = """
        {
          // The legacy system.
          "system": "VRRS",
          "dateFormats": ["dd/MM/yyyy", "yyyy-MM-dd HH:mm"],
          "excelSerialDates": true,
          "nullTokens": ["N/A"],
          "currencyCodes": ["VLD"],
          "institutions": {
            "HLB": ["Harbourline Bank PLC", "Harbourline Bank Plc."],
            "CCB": ["Crestmont Commercial Bank"],
          },
          "files": [
            {
              "file": "returns.csv",
              "returnType": "MLR",
              "institutionColumn": "Bank",
              "periodEndColumn": "Period End",
              "submittedColumn": "Filed",
              "approvedColumn": "Approved",
              "fields": { "Assets": "ASSETS", "Ratio %": "RATIO" },
              "ignoredColumns": ["Remarks"]
            }
          ]
        }
        """;

    /// <summary>The cleansing rules of the sample VRRS mapping, written out so the tests do not depend on it.</summary>
    public static CleansingRules Rules { get; } = new(
        DateFormats: ["yyyy-MM-dd", "yyyy-MM-dd HH:mm:ss", "dd/MM/yyyy", "dd/MM/yyyy HH:mm", "d/M/yyyy", "dd-MMM-yyyy", "dd.MM.yyyy", "yyyyMMdd", "MMM d, yyyy"],
        ExcelSerialDates: true,
        NullTokens: ["N/A", "NA", "n.a.", "NULL", "#N/A"],
        CurrencyCodes: ["VLD"]);

    /// <summary>The header of a file that <see cref="FileMapping"/> describes.</summary>
    public static IReadOnlyList<string> Header { get; } = ["Bank", "Period End", "Filed", "Approved", "Assets", "Ratio %", "Remarks"];

    /// <summary>The mapping of one monthly file with two value columns and a remarks column.</summary>
    public static LegacyFileMapping FileMapping { get; } = new()
    {
        File = "returns.csv",
        ReturnType = "MLR",
        InstitutionColumn = "Bank",
        PeriodEndColumn = "Period End",
        SubmittedColumn = "Filed",
        ApprovedColumn = "Approved",
        Fields = new Dictionary<string, string>(StringComparer.Ordinal) { ["Assets"] = "ASSETS", ["Ratio %"] = "RATIO" },
        IgnoredColumns = ["Remarks"],
    };

    /// <summary>Cleansed legacy names of the banks, as <see cref="LegacyMapping.InstitutionLookup"/> builds them.</summary>
    public static IReadOnlyDictionary<string, string> Lookup { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["HARBOURLINE BANK PLC"] = "HLB",
        ["HLB"] = "HLB",
        ["CRESTMONT COMMERCIAL BANK"] = "CCB",
        ["CCB"] = "CCB",
        ["VALORIA AGRICULTURAL BANK"] = "VAB",
        ["VAB"] = "VAB",
    };

    /// <summary>The banks the portal knows: not the closed VAB.</summary>
    public static IReadOnlySet<string> PortalBanks { get; } = new HashSet<string>(StringComparer.Ordinal) { "HLB", "CCB" };

    /// <summary>Returns the bytes of a mapping file.</summary>
    /// <param name="json">The JSON text.</param>
    /// <returns>UTF-8 bytes.</returns>
    public static byte[] Utf8(string json) => Encoding.UTF8.GetBytes(json);

    /// <summary>Reads a <c>returns.csv</c> with the standard header and the given CSV lines (from line 2, LF line ends).</summary>
    /// <param name="lines">The data lines.</param>
    /// <returns>The table.</returns>
    public static LegacyTable Table(params string[] lines) => Table(Header, lines);

    /// <summary>Reads a <c>returns.csv</c> with the given header and CSV lines (from line 2, LF line ends).</summary>
    /// <param name="header">The header cells.</param>
    /// <param name="lines">The data lines.</param>
    /// <returns>The table.</returns>
    public static LegacyTable Table(IReadOnlyList<string> header, params string[] lines)
    {
        var text = string.Join('\n', [string.Join(',', header), .. lines]) + "\n";
        return LegacyCsvReader.Read("returns.csv", Encoding.UTF8.GetBytes(text)).Value;
    }
}
