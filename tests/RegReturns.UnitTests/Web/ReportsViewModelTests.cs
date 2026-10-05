using System.Text.Json;

using RegReturns.Application.Reporting;
using RegReturns.Domain.Periods;
using RegReturns.Domain.Submissions;
using RegReturns.Domain.Templates;
using RegReturns.Web.Models.Reports;

namespace RegReturns.UnitTests.Web;

public sealed class ReportsViewModelTests
{
    private static readonly ReportingPeriod August = ReportingPeriod.Monthly(2026, 8);
    private static readonly ReportingPeriod September = ReportingPeriod.Monthly(2026, 9);

    [Theory]
    [InlineData(1.0, "100.0%")]
    [InlineData(0.875, "87.5%")]
    [InlineData(null, "-")]
    public void Rates_read_as_percentages(double? rate, string expected)
    {
        ReportsViewModel.FormatRate((decimal?)rate).ShouldBe(expected);
    }

    [Theory]
    [InlineData(145.2, 2, "%", "145.20%")]
    [InlineData(1500.5, 1, "VLD m", "1,500.5 VLD m")]
    [InlineData(3.0, 0, "", "3")]
    [InlineData(null, 2, "%", "-")]
    public void Values_read_with_their_precision_and_unit(double? value, int precision, string unit, string expected)
    {
        ReportsViewModel.FormatValue((decimal?)value, precision, unit).ShouldBe(expected);
    }

    [Fact]
    public void A_cell_title_names_the_bank_period_dates_and_status()
    {
        var bank = new ComplianceRow("HLB", "Harbourline Bank PLC", []);
        var cell = new ComplianceCell(
            September, ComplianceState.OnTime, new DateOnly(2026, 10, 15), new DateTimeOffset(2026, 10, 2, 8, 5, 0, TimeSpan.Zero), SubmissionStatus.UnderReview);

        ReportsViewModel.CellTitle(bank, cell).ShouldBe("HLB 2026-09: On time, due 15 Oct 2026, first submitted 2 Oct 2026 08:05 UTC, under review");
    }

    [Fact]
    public void The_legend_spells_out_only_the_short_labels()
    {
        ReportsViewModel.LegendText(ComplianceState.OnTime).ShouldBeEmpty();
        ReportsViewModel.LegendText(ComplianceState.NotDue).ShouldBe("Not due yet");
    }

    [Fact]
    public void Every_state_has_its_own_cell_class()
    {
        var states = Enum.GetValues<ComplianceState>();

        states.Select(ReportsViewModel.CellClass).Distinct().Count().ShouldBe(states.Length);
    }

    [Fact]
    public void The_findings_chart_shows_the_top_rules_and_sums_the_rest()
    {
        var rules = Enumerable.Range(1, ReportsViewModel.ChartedRules + 2)
            .Select(i => new RuleTrend($"R{i}", Severity.Warning, [i, 1], i + 1))
            .ToList();
        var model = Model(new FindingTrend(rules, [0, 0]));

        using var chart = JsonDocument.Parse(model.FindingChartJson);

        chart.RootElement.GetProperty("kind").GetString().ShouldBe("findings");
        chart.RootElement.GetProperty("labels").EnumerateArray().Select(l => l.GetString()).ShouldBe(["2026-08", "2026-09"]);
        var series = chart.RootElement.GetProperty("series").EnumerateArray().ToList();
        series.Count.ShouldBe(ReportsViewModel.ChartedRules + 1);
        series[0].GetProperty("label").GetString().ShouldBe("R1 (Warning)");
        series[^1].GetProperty("label").GetString().ShouldBe("Other rules");
        series[^1].GetProperty("values").EnumerateArray().Select(v => v.GetDecimal()).ShouldBe([15m, 2m]);
    }

    [Fact]
    public void A_sparkline_is_described_period_by_period()
    {
        var model = Model(new FindingTrend([], [0, 0]));
        var ratio = new KeyRatioTrend("LCR", "Liquidity coverage ratio", "%", 1, []);
        var series = new KeyRatioSeries("HLB", "Harbourline Bank PLC", [140.25m, null]);

        model.SparklineLabel(ratio, series).ShouldBe("Liquidity coverage ratio of Harbourline Bank PLC: 2026-08 140.3%, 2026-09 -");
        model.SparklineJson(series).ShouldBe("{\"kind\":\"sparkline\",\"labels\":[\"2026-08\",\"2026-09\"],\"values\":[140.25,null]}");
    }

    [Fact]
    public void A_page_that_could_not_be_built_has_no_chart_data()
    {
        var model = new ReportsViewModel(null, "No return types are set up yet.");

        model.FindingChartJson.ShouldBe("{}");
        model.PeriodLabels.ShouldBeEmpty();
    }

    private static ReportsViewModel Model(FindingTrend findings)
    {
        var mlr = new ReportReturnType(Guid.NewGuid(), "MLR", "Monthly Liquidity Return", ReturnFrequency.Monthly);
        var header = new ReportHeader(mlr, [mlr], [August, September], new DateOnly(2026, 10, 4), null);
        var compliance = new ComplianceReport([], new ComplianceTotals(0, 0, 0, 0), []);
        return new ReportsViewModel(new ReportsDashboard(header, compliance, findings, []), null);
    }
}
