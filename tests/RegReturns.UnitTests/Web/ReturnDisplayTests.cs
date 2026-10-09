using RegReturns.Application.Returns;
using RegReturns.Domain.Submissions;
using RegReturns.Domain.Templates;
using RegReturns.Web.Models.Returns;

namespace RegReturns.UnitTests.Web;

public sealed class ReturnDisplayTests
{
    private static ReturnFormField Field(FieldDataType type, int precision = 2, string unit = "") =>
        new("F", "Field", type, unit, precision, null, []);

    [Fact]
    public void A_clean_validation_reads_as_no_problems()
    {
        ReturnDisplay.Describe(new ValidationOutcome(0, 0, 0)).ShouldBe("Validation found no problems.");
    }

    [Fact]
    public void Justified_warnings_read_as_ready()
    {
        ReturnDisplay.Describe(new ValidationOutcome(0, 2, 0)).ShouldBe("Validation found no errors and every warning is justified.");
    }

    [Fact]
    public void Open_findings_are_counted_with_singular_and_plural_nouns()
    {
        ReturnDisplay.Describe(new ValidationOutcome(1, 3, 2)).ShouldBe("Validation found 1 error and 2 warnings to justify.");
    }

    [Fact]
    public void Only_unjustified_warnings_are_mentioned_when_there_are_no_errors()
    {
        ReturnDisplay.Describe(new ValidationOutcome(0, 2, 1)).ShouldBe("Validation found 1 warning to justify.");
    }

    [Theory]
    [InlineData(FieldDataType.Amount, 2, "VLD millions", "Number, up to 2 decimal places, VLD millions")]
    [InlineData(FieldDataType.Percentage, 2, "", "Number, up to 2 decimal places")]
    [InlineData(FieldDataType.Amount, 0, "", "Whole number")]
    [InlineData(FieldDataType.WholeNumber, 0, "accounts", "Whole number, accounts")]
    [InlineData(FieldDataType.Date, 0, "", "Date (YYYY-MM-DD)")]
    [InlineData(FieldDataType.Boolean, 0, "", "Yes or no")]
    [InlineData(FieldDataType.Text, 0, "", "Text")]
    public void Format_hints_describe_what_each_field_accepts(FieldDataType type, int precision, string unit, string expected)
    {
        ReturnDisplay.FormatHint(Field(type, precision, unit)).ShouldBe(expected);
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("2026-09-30", true)]
    [InlineData("30/09/2026", false)]
    public void Only_blank_or_iso_dates_go_in_a_date_picker_so_bad_values_stay_visible(string? value, bool fits)
    {
        ReturnDisplay.FitsDatePicker(value).ShouldBe(fits);
    }

    [Theory]
    [InlineData(512, "512 bytes")]
    [InlineData(2048, "2 KB")]
    [InlineData(5 * 1024 * 1024, "5 MB")]
    public void File_sizes_use_the_largest_sensible_unit(int bytes, string expected)
    {
        ReturnDisplay.FileSize(bytes).ShouldBe(expected);
    }

    [Fact]
    public void Workflow_steps_read_in_the_past_tense()
    {
        ReturnDisplay.Label(WorkflowAction.StartReview).ShouldBe("Review started");
        ReturnDisplay.Label(WorkflowAction.ReturnForCorrection).ShouldBe("Returned for correction");
    }

    [Fact]
    public void A_migrated_step_reads_as_approved_in_the_legacy_system()
    {
        ReturnDisplay.Label(WorkflowAction.Migrate).ShouldBe("Migrated from the legacy system, approved");
    }

    [Fact]
    public void Every_workflow_step_has_its_own_label()
    {
        var labels = Enum.GetValues<WorkflowAction>().Select(ReturnDisplay.Label).ToList();

        labels.ShouldBeUnique();
        labels.ShouldNotContain(label => Enum.GetNames<WorkflowAction>().Contains(label));
    }

    [Fact]
    public void A_migrated_return_names_the_legacy_migration_as_its_source()
    {
        ReturnDisplay.Label(SubmissionSource.Migration).ShouldBe("Legacy migration");
    }

    [Fact]
    public void Times_are_shown_in_utc()
    {
        ReturnDisplay.Utc(new DateTimeOffset(2026, 10, 5, 16, 5, 0, TimeSpan.FromHours(2))).ShouldBe("5 Oct 2026 14:05 UTC");
    }
}
