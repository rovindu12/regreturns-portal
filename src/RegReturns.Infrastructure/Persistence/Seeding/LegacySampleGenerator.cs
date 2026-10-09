using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

using RegReturns.Domain.Institutions;
using RegReturns.Domain.Periods;
using RegReturns.Domain.Templates;
using RegReturns.Infrastructure.Legacy;

namespace RegReturns.Infrastructure.Persistence.Seeding;

/// <summary>
/// Generates the sample exports of the fictional legacy system, VRRS (Valoria Returns Reporting System), and their
/// mapping (ADR 0029). The figures come from <see cref="FigureGenerator"/>, so they continue the seeded history; the
/// mess is deliberate and the same on every run: bank name spellings, five date styles and Excel serial dates, thousand
/// separators, currency codes, percent signs, padding, a European number, N/A, blank and short rows, duplicates, a
/// closed bank, a misspelt bank, a period before the current forms, a keying slip and dates out of order.
/// samples/legacy/README.md lists every planted defect and what the migrator does with it.
/// </summary>
internal static class LegacySampleGenerator
{
    /// <summary>The mapping file name.</summary>
    public const string MappingFile = "mapping.json";

    /// <summary>The Monthly Liquidity Return export.</summary>
    public const string MlrFile = "VRRS_MLR_EXPORT.csv";

    /// <summary>The Monthly Deposits and Advances Return export.</summary>
    public const string MdaFile = "vrrs_mda_history.csv";

    /// <summary>The Quarterly Capital Adequacy Return export.</summary>
    public const string QcarFile = "VRRS_QCAR_2024_2025.csv";

    /// <summary>The code the mapping gives a bank that closed before the portal, so the portal does not know it.</summary>
    public const string ClosedBankCode = "VAB";

    private const string ClosedBankName = "Valoria Agricultural Bank";

    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
    private static readonly ReportingPeriod FirstMonth = ReportingPeriod.Monthly(2024, 1);
    private static readonly ReportingPeriod LastMonth = ReportingPeriod.Monthly(2025, 9);
    private static readonly ReportingPeriod FirstQuarter = ReportingPeriod.Quarterly(2024, 1);
    private static readonly ReportingPeriod LastQuarter = ReportingPeriod.Quarterly(2025, 3);

    private static readonly string[] DateFormats =
        ["yyyy-MM-dd", "yyyy-MM-dd HH:mm:ss", "dd/MM/yyyy", "dd/MM/yyyy HH:mm", "d/M/yyyy", "dd-MMM-yyyy", "dd.MM.yyyy", "yyyyMMdd", "MMM d, yyyy"];

    private static readonly Dictionary<string, string[]> Spellings = new(StringComparer.Ordinal)
    {
        [DemoBank.Harbourline] = ["Harbourline Bank PLC", "HARBOURLINE BANK PLC", "Harbourline Bank Plc."],
        [DemoBank.Crestmont] = ["Crestmont Commercial Bank", "CRESTMONT COMMERCIAL BANK LTD", "Crestmont Comm. Bank"],
        [DemoBank.LotusUnion] = ["Lotus Union Bank", "LOTUS UNION BANK", "Lotus Union Bank Ltd"],
        [DemoBank.Northgate] = ["Northgate Savings Bank", "NORTHGATE SAVINGS", "Northgate Savings Bank Ltd."],
        [DemoBank.Meridian] = ["Meridian Development Bank", "MERIDIAN DEV. BANK", "Meridian Development Bank (MDB)"],
        [ClosedBankCode] = [ClosedBankName],
    };

    private static readonly string[] Operators = ["JMK", "RDS", "akp", "T.N."];

    private static readonly Dictionary<string, string[]> Aliases = new(StringComparer.Ordinal)
    {
        [DemoBank.Harbourline] = ["Harbourline Bank PLC"],
        [DemoBank.Crestmont] = ["Crestmont Commercial Bank", "Crestmont Commercial Bank Ltd", "Crestmont Comm Bank"],
        [DemoBank.LotusUnion] = ["Lotus Union Bank", "Lotus Union Bank Ltd"],
        [DemoBank.Northgate] = ["Northgate Savings Bank", "Northgate Savings", "Northgate Savings Bank Ltd"],
        [DemoBank.Meridian] = ["Meridian Development Bank", "Meridian Dev Bank", "Meridian Development Bank MDB"],
        [ClosedBankCode] = [ClosedBankName],
    };

    private static readonly (string Column, string Field)[] MlrColumns =
    [
        ("Lvl 1 Assets", MlrTemplate.L1Hqla), ("Lvl 2A Assets", MlrTemplate.L2aHqla), ("Lvl 2B Assets", MlrTemplate.L2bHqla),
        ("Total HQLA", MlrTemplate.TotalHqla), ("Cash Outflows 30d", MlrTemplate.Outflows), ("Cash Inflows 30d", MlrTemplate.Inflows),
        ("Net Cash Outflows", MlrTemplate.NetOutflows), ("LCR %", MlrTemplate.Lcr), ("Total Deposits", MlrTemplate.TotalDeposits),
        ("Liquid Assets", MlrTemplate.LiquidAssets), ("Liquid Assets Ratio %", MlrTemplate.LiquidAssetsRatio),
    ];

    private static readonly (string Column, string Field)[] MdaColumns =
    [
        ("DEP_DD", MdaTemplate.DepDemand), ("DEP_SAV", MdaTemplate.DepSavings), ("DEP_TIME", MdaTemplate.DepTime),
        ("DEP_TOT", MdaTemplate.TotalDeposits), ("ADV_AGRI", MdaTemplate.LoansAgriculture), ("ADV_MFG", MdaTemplate.LoansManufacturing),
        ("ADV_TRADE", MdaTemplate.LoansTrade), ("ADV_PERS", MdaTemplate.LoansPersonal), ("ADV_OTH", MdaTemplate.LoansOther),
        ("ADV_TOT", MdaTemplate.TotalLoans), ("NPL_AMT", MdaTemplate.NplAmount), ("NPL_PCT", MdaTemplate.NplRatio),
    ];

    private static readonly (string Column, string Field)[] QcarColumns =
    [
        ("CET1 Capital", QcarTemplate.Cet1), ("AT1 Capital", QcarTemplate.At1), ("Tier 1 Capital", QcarTemplate.Tier1),
        ("Tier 2 Capital", QcarTemplate.Tier2), ("Total Capital", QcarTemplate.TotalCapital), ("Credit RWA", QcarTemplate.RwaCredit),
        ("Market RWA", QcarTemplate.RwaMarket), ("Operational RWA", QcarTemplate.RwaOperational), ("Total RWA", QcarTemplate.TotalRwa),
        ("CET1 Ratio", QcarTemplate.Cet1Ratio), ("Tier 1 Ratio", QcarTemplate.Tier1Ratio), ("Total CAR", QcarTemplate.Car),
    ];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>Generates the mapping and the three exports.</summary>
    /// <returns>File names and contents, the same bytes on every call.</returns>
    public static IReadOnlyList<(string Name, byte[] Content)> Generate() =>
    [
        (MappingFile, Encoding.UTF8.GetBytes(JsonSerializer.Serialize(Mapping(), JsonOptions).ReplaceLineEndings("\n") + "\n")),
        (MlrFile, Encoding.UTF8.GetBytes(Mlr())),
        (MdaFile, [.. CsvBom, .. Encoding.UTF8.GetBytes(Mda())]),
        (QcarFile, [.. CsvBom, .. Encoding.UTF8.GetBytes(Qcar())]),
    ];

    private static byte[] CsvBom => [0xEF, 0xBB, 0xBF];

    /// <summary>Builds the mapping the exports are read with.</summary>
    /// <returns>The mapping.</returns>
    internal static LegacyMapping Mapping() => new()
    {
        System = "VRRS (Valoria Returns Reporting System)",
        DateFormats = DateFormats,
        ExcelSerialDates = true,
        NullTokens = ["N/A", "NA", "n.a.", "NULL", "#N/A"],
        CurrencyCodes = ["VLD"],
        Institutions = Aliases.ToDictionary(a => a.Key, a => (IReadOnlyList<string>)a.Value, StringComparer.Ordinal),
        Files =
        [
            new LegacyFileMapping
            {
                File = MlrFile,
                ReturnType = MlrTemplate.Code,
                InstitutionColumn = "Institution",
                PeriodEndColumn = "Return Period",
                SubmittedColumn = "Received On",
                ApprovedColumn = "Approved On",
                Fields = MlrColumns.ToDictionary(c => c.Column, c => c.Field, StringComparer.Ordinal),
                IgnoredColumns = ["Remarks"],
            },
            new LegacyFileMapping
            {
                File = MdaFile,
                ReturnType = MdaTemplate.Code,
                InstitutionColumn = "BANK",
                PeriodEndColumn = "PERIOD_END",
                SubmittedColumn = "SUBMITTED",
                ApprovedColumn = "APPROVED",
                Fields = MdaColumns.ToDictionary(c => c.Column, c => c.Field, StringComparer.Ordinal),
                IgnoredColumns = ["OPERATOR"],
            },
            new LegacyFileMapping
            {
                File = QcarFile,
                ReturnType = QcarTemplate.Code,
                InstitutionColumn = "Bank Name",
                PeriodEndColumn = "Quarter End",
                SubmittedColumn = "Date Submitted",
                ApprovedColumn = "Date Approved",
                Fields = QcarColumns.ToDictionary(c => c.Column, c => c.Field, StringComparer.Ordinal),
                IgnoredColumns = ["Comments"],
            },
        ],
    };

    private static string Mlr()
    {
        var returnType = MlrTemplate.CreateReturnType();
        var csv = new LegacyCsv("\n");
        csv.Line(["Institution", "Return Period", "Received On", "Approved On", .. MlrColumns.Select(c => c.Column), "Remarks"]);

        // A straggler from before the current forms came into force.
        var december = ReportingPeriod.Monthly(2023, 12);
        var hlb = Bank(DemoBank.Harbourline);
        csv.Line(MlrRow(returnType, hlb, december, MlrFigures(hlb, december), 0, "Late entry from the old forms"));

        foreach (var (month, m) in Periods(FirstMonth, LastMonth))
        {
            foreach (var bank in DemoBank.All)
            {
                var figures = MlrFigures(bank, month);
                var row = MlrRow(returnType, bank, month, figures, m, null);
                switch (bank.Code, month.Label)
                {
                    case (DemoBank.Harbourline, "2024-06"):
                        // The original filing; a corrected one follows after July (last row wins).
                        row = MlrRow(returnType, bank, month, Restate(figures, MlrTemplate.L1Hqla, -85m, RecalculateMlr), m, null);
                        break;
                    case (DemoBank.Meridian, "2024-09"):
                        // A keying slip: Total HQLA no longer adds up.
                        row = MlrRow(returnType, bank, month, With(figures, MlrTemplate.TotalHqla, figures[MlrTemplate.TotalHqla] + 90m), m, null);
                        break;
                    case (DemoBank.Crestmont, "2024-11"):
                        row[1] = "15/11/2024";
                        break;
                    case (DemoBank.Northgate, "2025-03"):
                        row[5] = European(figures[MlrTemplate.L2aHqla]);
                        break;
                    case (DemoBank.LotusUnion, "2025-01"):
                        // Approved two days before it was received.
                        row[3] = MlrReceived(returnType, bank, month, m).AddDays(-2).ToString("dd/MM/yyyy", Invariant);
                        break;
                    default:
                        break;
                }

                csv.Line(row);
            }

            if (month.Number <= 3 && month.Year == 2024)
            {
                var closed = new DemoBank(ClosedBankCode, ClosedBankName, "agribank.example", LicenceCategory.Development, 0.15m, 0.002m);
                csv.Line(MlrRow(returnType, closed, month, FigureGenerator.Mlr(closed, month, FigureGenerator.MlrShape.Normal), m, "Bank closed 30/06/2024"));
            }

            if (month.Label == "2024-07")
            {
                var june = ReportingPeriod.Monthly(2024, 6);
                csv.Line(MlrRow(returnType, hlb, june, MlrFigures(hlb, june), m - 1, "Resubmitted: Level 1 holdings corrected"));
            }

            if (month.Label == "2024-12")
            {
                csv.Line(Enumerable.Repeat(string.Empty, MlrColumns.Length + 5).ToArray());
            }
        }

        return csv.ToString();
    }

    private static Dictionary<string, decimal> MlrFigures(DemoBank bank, ReportingPeriod month) =>
        bank.Code == DemoBank.LotusUnion && month.Label == "2024-08"
            ? FigureGenerator.Mlr(bank, month, new FigureGenerator.MlrShape(Level1Factor: 0.45m, Level2AFactor: 0.55m))
            : FigureGenerator.Mlr(bank, month, FigureGenerator.MlrShape.Normal);

    private static string[] MlrRow(
        ReturnType returnType, DemoBank bank, ReportingPeriod month, Dictionary<string, decimal> figures, int m, string? remark)
    {
        var received = MlrReceived(returnType, bank, month, m);
        var approved = received.AddDays(3 + Pick(bank, m, 4, 6));
        var periodEnd = Pick(bank, m, 5, 4) switch
        {
            0 => month.End.ToString("dd/MM/yyyy", Invariant),
            1 => month.End.ToString("yyyy-MM-dd", Invariant),
            2 => month.End.ToString("dd-MMM-yyyy", Invariant),
            _ => month.End.ToString("dd.MM.yyyy", Invariant),
        };

        var cells = new List<string>
        {
            Spelling(bank, m),
            periodEnd,
            received.ToString(m % 2 == 0 ? "dd/MM/yyyy HH:mm" : "yyyy-MM-dd HH:mm:ss", Invariant),
            approved.ToString("dd/MM/yyyy", Invariant),
        };
        cells.AddRange(MlrColumns.Select((c, i) => c.Field is MlrTemplate.Lcr or MlrTemplate.LiquidAssetsRatio
            ? Ratio(figures[c.Field], Pick(bank, m, i, 2) == 0)
            : Amount(figures[c.Field], Pick(bank, m, i, 5))));
        cells.Add(remark ?? (Pick(bank, m, 7, 9) == 0 ? "=see covering letter" : string.Empty));
        return [.. cells];
    }

    private static DateTime MlrReceived(ReturnType returnType, DemoBank bank, ReportingPeriod month, int m)
    {
        var due = returnType.DueDateFor(month);
        var late = bank.Code == DemoBank.Northgate && month.Label == "2024-10" ? 3 : 0;
        return At(due.AddDays(late > 0 ? late : -(2 + Pick(bank, m, 1, 6))), 9 + Pick(bank, m, 2, 8), 5 * Pick(bank, m, 3, 12));
    }

    private static string Mda()
    {
        var returnType = MdaTemplate.CreateReturnType();
        var csv = new LegacyCsv("\r\n");
        csv.Line(["BANK", "PERIOD_END", "SUBMITTED", "APPROVED", .. MdaColumns.Select(c => c.Column), "OPERATOR"]);
        foreach (var bank in DemoBank.All)
        {
            foreach (var (month, m) in Periods(FirstMonth, LastMonth))
            {
                var row = MdaRow(returnType, bank, month, m);
                switch (bank.Code, month.Label)
                {
                    case (DemoBank.LotusUnion, "2024-04"):
                        // Keyed with a misspelt name first, then again correctly.
                        var typo = (string[])row.Clone();
                        typo[0] = "Lotus Unoin Bank";
                        csv.Line(typo);
                        break;
                    case (DemoBank.Northgate, "2024-11"):
                        row[14] = "N/A";
                        row[15] = "N/A";
                        break;
                    case (DemoBank.Meridian, "2024-02"):
                        row[1] = "31/02/2024";
                        break;
                    case (DemoBank.Harbourline, "2025-05"):
                        // The export cut this line short.
                        row = row[..10];
                        break;
                    default:
                        break;
                }

                csv.Line(row);
                if (bank.Code == DemoBank.LotusUnion && month.Label == "2025-02")
                {
                    // Exported twice.
                    csv.Line(row);
                }
            }
        }

        csv.Line([string.Empty]);
        return csv.ToString();
    }

    private static string[] MdaRow(ReturnType returnType, DemoBank bank, ReportingPeriod month, int m)
    {
        var figures = FigureGenerator.Mda(bank, month);
        var due = returnType.DueDateFor(month);
        var late = bank.Code == DemoBank.Meridian && month.Label == "2024-07" ? 2 : 0;
        var received = At(due.AddDays(late > 0 ? late : -(1 + Pick(bank, m, 11, 7))), 0, 0);
        var approved = received.AddDays(4 + Pick(bank, m, 12, 5));
        var periodEnd = month.Year == 2024
            ? (month.End.DayNumber - new DateOnly(1899, 12, 30).DayNumber).ToString(Invariant)
            : month.End.ToString("yyyyMMdd", Invariant);

        var cells = new List<string>
        {
            Spelling(bank, m + 1),
            periodEnd,
            received.ToString("yyyyMMdd", Invariant),
            approved.ToString("dd-MMM-yyyy", Invariant).ToUpperInvariant(),
        };
        cells.AddRange(MdaColumns.Select((c, i) => c.Field == MdaTemplate.NplRatio
            ? figures[c.Field].ToString("0.00", Invariant)
            : Amount(figures[c.Field], Pick(bank, m, i, 2))));
        cells.Add(Operators[Pick(bank, m, 13, Operators.Length)]);
        return [.. cells];
    }

    private static string Qcar()
    {
        var returnType = QcarTemplate.CreateReturnType();
        var csv = new LegacyCsv("\r\n");
        csv.Line(["Bank Name", "Quarter End", "Date Submitted", "Date Approved", .. QcarColumns.Select(c => c.Column), "Comments"]);
        foreach (var (quarter, q) in Periods(FirstQuarter, LastQuarter))
        {
            foreach (var bank in DemoBank.All)
            {
                var figures = FigureGenerator.Qcar(bank, quarter);
                if (bank.Code == DemoBank.Crestmont && quarter.Label == "2024-Q3")
                {
                    // The first filing; a restatement with more Tier 2 capital closes the file (last row wins).
                    figures = Restate(figures, QcarTemplate.Tier2, -12.5m, RecalculateQcar);
                }

                var row = QcarRow(returnType, bank, quarter, figures, q, null);
                if (bank.Code == DemoBank.Northgate && quarter.Label == "2024-Q4")
                {
                    row[15] = "(" + figures[QcarTemplate.Car].ToString("0.00", Invariant) + ")";
                }

                csv.Line(row);
            }
        }

        var restated = ReportingPeriod.Quarterly(2024, 3);
        var crestmont = Bank(DemoBank.Crestmont);
        var restatement = QcarRow(returnType, crestmont, restated, FigureGenerator.Qcar(crestmont, restated), 2, "Restated: subordinated debt reclassified to Tier 2");
        restatement[1] = restated.End.ToString("MMM d, yyyy", Invariant);
        csv.Line(restatement);
        return csv.ToString();
    }

    private static string[] QcarRow(
        ReturnType returnType, DemoBank bank, ReportingPeriod quarter, Dictionary<string, decimal> figures, int q, string? comment)
    {
        var due = returnType.DueDateFor(quarter);
        var late = bank.Code == DemoBank.Northgate && quarter.Label == "2024-Q2" ? 4 : 0;
        var received = At(due.AddDays(late > 0 ? late : -(3 + Pick(bank, q, 21, 9))), 0, 0);
        var approved = received.AddDays(6 + Pick(bank, q, 22, 8));
        var cells = new List<string>
        {
            Spelling(bank, q + 2),
            Pick(bank, q, 23, 2) == 0 ? quarter.End.ToString("dd/MM/yyyy", Invariant) : quarter.End.ToString("MMM d, yyyy", Invariant),
            received.ToString("MMM d, yyyy", Invariant),
            approved.ToString("d/M/yyyy", Invariant),
        };
        cells.AddRange(QcarColumns.Select(c => c.Field is QcarTemplate.Cet1Ratio or QcarTemplate.Tier1Ratio or QcarTemplate.Car
            ? Ratio(figures[c.Field], percentSign: true)
            : Amount(figures[c.Field], 1)));
        cells.Add(comment ?? string.Empty);
        return [.. cells];
    }

    private static Dictionary<string, decimal> Restate(
        Dictionary<string, decimal> figures, string field, decimal change, Action<Dictionary<string, decimal>> recalculate)
    {
        var restated = With(figures, field, figures[field] + change);
        recalculate(restated);
        return restated;
    }

    private static void RecalculateMlr(Dictionary<string, decimal> f)
    {
        f[MlrTemplate.TotalHqla] = f[MlrTemplate.L1Hqla] + f[MlrTemplate.L2aHqla] + f[MlrTemplate.L2bHqla];
        f[MlrTemplate.Lcr] = Math.Round(f[MlrTemplate.TotalHqla] / f[MlrTemplate.NetOutflows] * 100m, 2, MidpointRounding.ToEven);
    }

    private static void RecalculateQcar(Dictionary<string, decimal> f)
    {
        f[QcarTemplate.TotalCapital] = f[QcarTemplate.Tier1] + f[QcarTemplate.Tier2];
        f[QcarTemplate.Car] = Math.Round(f[QcarTemplate.TotalCapital] / f[QcarTemplate.TotalRwa] * 100m, 2, MidpointRounding.ToEven);
    }

    private static Dictionary<string, decimal> With(Dictionary<string, decimal> figures, string field, decimal value) =>
        new(figures, StringComparer.Ordinal) { [field] = value };

    private static IEnumerable<(ReportingPeriod Period, int Index)> Periods(ReportingPeriod first, ReportingPeriod last)
    {
        var index = 0;
        for (var period = first; period <= last; period = ReportingPeriod.Containing(period.Frequency, period.End.AddDays(1)))
        {
            yield return (period, index++);
        }
    }

    private static DemoBank Bank(string code) => DemoBank.All.Single(b => b.Code == code);

    private static string Spelling(DemoBank bank, int index)
    {
        var names = Spellings[bank.Code];
        return names[Pick(bank, index, 31, names.Length)];
    }

    // A fixed, well-spread choice per bank, period and purpose; no randomness, so the files never change.
    private static int Pick(DemoBank bank, int period, int salt, int choices) =>
        (((bank.Code[0] * 7) + (bank.Code[1] * 3) + (period * 13) + (salt * 29) + (period * salt)) & int.MaxValue) % choices;

    private static DateTime At(DateOnly date, int hour, int minute) => date.ToDateTime(new TimeOnly(hour, minute));

    private static string Amount(decimal value, int style) => style switch
    {
        0 => value.ToString("0.00", Invariant),
        1 => value.ToString("#,##0.00", Invariant),
        2 => "VLD " + value.ToString("#,##0.00", Invariant),
        3 => " " + value.ToString("0.00", Invariant) + " ",
        _ => value.ToString("#,##0.00", Invariant) + " VLD",
    };

    private static string Ratio(decimal value, bool percentSign) =>
        value.ToString("0.00", Invariant) + (percentSign ? "%" : string.Empty);

    // 1234.56 as continental Europe writes it: 1.234,56. The migrator refuses to guess and rejects it.
    private static string European(decimal value) =>
        value.ToString("#,##0.00", Invariant).Replace(",", "_", StringComparison.Ordinal)
            .Replace(".", ",", StringComparison.Ordinal).Replace("_", ".", StringComparison.Ordinal);

    /// <summary>Writes CSV the way the legacy exports did: quoting only cells with commas or quotes, no formula guard.</summary>
    private sealed class LegacyCsv(string lineBreak)
    {
        private readonly StringBuilder _text = new();

        public void Line(IEnumerable<string> cells)
        {
            _text.AppendJoin(',', cells.Select(c => c.Contains(',', StringComparison.Ordinal) || c.Contains('"', StringComparison.Ordinal)
                ? "\"" + c.Replace("\"", "\"\"", StringComparison.Ordinal) + "\""
                : c));
            _text.Append(lineBreak);
        }

        public override string ToString() => _text.ToString();
    }
}
