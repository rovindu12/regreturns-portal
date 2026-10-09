using System.Security.Cryptography;
using System.Text.Json.Nodes;

using RegReturns.Application.Migration;
using RegReturns.Domain.Common;
using RegReturns.Infrastructure.Legacy;

namespace RegReturns.UnitTests.Infrastructure.Legacy;

public sealed class LegacyMappingTests
{
    [Fact]
    public void A_valid_mapping_is_read_with_its_comments_and_trailing_commas()
    {
        var (mapping, _) = Parse(LegacyTestData.MappingJson).Value;

        mapping.System.ShouldBe("VRRS");
        mapping.Institutions.Keys.ShouldBe(["HLB", "CCB"]);
        var file = mapping.Files.ShouldHaveSingleItem();
        file.File.ShouldBe("returns.csv");
        file.ReturnType.ShouldBe("MLR");
        file.Fields.ShouldBe(new Dictionary<string, string> { ["Assets"] = "ASSETS", ["Ratio %"] = "RATIO" });
        file.IgnoredColumns.ShouldBe(["Remarks"]);
    }

    [Fact]
    public void The_cleansing_rules_come_from_the_mapping()
    {
        var rules = Parse(LegacyTestData.MappingJson).Value.Mapping.Rules;

        rules.DateFormats.ShouldBe(["dd/MM/yyyy", "yyyy-MM-dd HH:mm"]);
        rules.ExcelSerialDates.ShouldBeTrue();
        rules.NullTokens.ShouldBe(["N/A"]);
        rules.CurrencyCodes.ShouldBe(["VLD"]);
    }

    [Fact]
    public void The_digest_is_the_lowercase_hex_sha256_of_the_file_bytes()
    {
        var bytes = LegacyTestData.Utf8(LegacyTestData.MappingJson);

        var (_, sha256) = LegacyMapping.Parse(bytes).Value;

        sha256.ShouldBe(Convert.ToHexStringLower(SHA256.HashData(bytes)));
    }

    [Fact]
    public void The_file_columns_are_the_role_columns_then_the_value_columns_then_the_ignored_ones()
    {
        var file = Parse(LegacyTestData.MappingJson).Value.Mapping.Files[0];

        file.Columns.ShouldBe(["Bank", "Period End", "Filed", "Approved", "Assets", "Ratio %", "Remarks"]);
    }

    [Fact]
    public void The_institution_lookup_holds_every_cleansed_spelling_and_each_bank_code()
    {
        var lookup = Parse(LegacyTestData.MappingJson).Value.Mapping.InstitutionLookup();

        lookup.ShouldBe(new Dictionary<string, string>
        {
            ["HARBOURLINE BANK PLC"] = "HLB",
            ["HLB"] = "HLB",
            ["CRESTMONT COMMERCIAL BANK"] = "CCB",
            ["CCB"] = "CCB",
        }, ignoreOrder: true);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{ \"files\": [ }")]
    public void A_file_that_is_not_json_is_refused(string json)
    {
        var error = Parse(json).Error!;

        error.Code.ShouldBe(MigrationErrors.MappingInvalid.Code);
        error.Message.ShouldStartWith("The mapping file is not valid JSON:");
    }

    [Fact]
    public void A_null_mapping_is_refused_as_empty()
    {
        Problems("null").ShouldBe("The mapping file is empty.");
    }

    [Fact]
    public void A_misspelt_member_is_refused_instead_of_being_ignored()
    {
        var json = Edit(m => m["dateFormat"] = new JsonArray("dd/MM/yyyy"));

        Problems(json).ShouldStartWith("The mapping file is not valid JSON:");
    }

    [Fact]
    public void A_misspelt_file_member_is_refused_instead_of_being_ignored()
    {
        var json = Edit(m => m["files"]![0]!["ignoreColumns"] = new JsonArray("Remarks"));

        Problems(json).ShouldStartWith("The mapping file is not valid JSON:");
    }

    [Fact]
    public void A_mapping_needs_a_date_format()
    {
        Problems(Edit(m => m["dateFormats"] = new JsonArray())).ShouldContain("List at least one date format.");
    }

    [Theory]
    [InlineData("yyyy-MM")]
    [InlineData("HH:mm")]
    [InlineData("%")]
    [InlineData(" ")]
    public void A_date_format_that_cannot_read_back_a_date_is_refused(string format)
    {
        Problems(Edit(m => m["dateFormats"] = new JsonArray("dd/MM/yyyy", format)))
            .ShouldContain($"Date format '{format}' cannot read back a date it writes.");
    }

    [Fact]
    public void A_mapping_needs_a_bank()
    {
        Problems(Edit(m => m["institutions"] = new JsonObject())).ShouldContain("Map at least one bank.");
    }

    [Fact]
    public void A_spelling_mapped_to_two_banks_is_refused_after_cleansing()
    {
        var json = Edit(m => m["institutions"]!["CCB"] = new JsonArray("Crestmont Commercial Bank", "HARBOURLINE BANK PLC."));

        Problems(json).ShouldContain("The name 'HARBOURLINE BANK PLC' is mapped to both HLB and CCB.");
    }

    [Fact]
    public void A_bank_code_used_as_another_banks_spelling_is_refused()
    {
        var json = Edit(m => m["institutions"]!["CCB"] = new JsonArray("hlb"));

        Problems(json).ShouldContain("The name 'HLB' is mapped to both HLB and CCB.");
    }

    [Fact]
    public void A_spelling_repeated_for_the_same_bank_is_allowed()
    {
        var json = Edit(m => m["institutions"]!["HLB"] = new JsonArray("Harbourline Bank PLC", "harbourline bank plc", "HLB"));

        Parse(json).IsSuccess.ShouldBeTrue();
    }

    [Theory]
    [InlineData("  ")]
    [InlineData("...")]
    public void A_blank_bank_name_is_refused(string name)
    {
        var json = Edit(m => m["institutions"]!["HLB"] = new JsonArray("Harbourline Bank PLC", name));

        Problems(json).ShouldContain("Bank HLB has a blank name.");
    }

    [Fact]
    public void A_mapping_needs_a_file()
    {
        Problems(Edit(m => m["files"] = new JsonArray())).ShouldContain("List at least one file.");
    }

    [Fact]
    public void A_file_listed_twice_in_any_case_is_refused()
    {
        var json = Edit(m =>
        {
            var copy = m["files"]![0]!.DeepClone();
            copy["file"] = "RETURNS.CSV";
            m["files"]!.AsArray().Add(copy);
        });

        Problems(json).ShouldContain("File 'returns.csv' is listed more than once.");
    }

    [Theory]
    [InlineData("data/returns.csv", "data/returns.csv")]
    [InlineData("..\\returns.csv", "..\\returns.csv")]
    [InlineData(" returns.csv", " returns.csv")]
    [InlineData("", "(unnamed)")]
    public void A_file_must_be_a_plain_file_name(string fileName, string shown)
    {
        Problems(Edit(m => m["files"]![0]!["file"] = fileName)).ShouldContain($"File '{shown}' must be a plain file name.");
    }

    [Fact]
    public void A_file_needs_a_return_type()
    {
        Problems(Edit(m => m["files"]![0]!["returnType"] = " ")).ShouldContain("File 'returns.csv' needs a return type.");
    }

    [Theory]
    [InlineData("institutionColumn")]
    [InlineData("periodEndColumn")]
    [InlineData("submittedColumn")]
    [InlineData("approvedColumn")]
    public void A_file_must_name_every_role_column(string role)
    {
        Problems(Edit(m => m["files"]![0]!.AsObject().Remove(role)))
            .ShouldContain("File 'returns.csv' must name its institution, period end, submitted and approved columns.");
    }

    [Fact]
    public void A_file_must_map_a_value_column()
    {
        Problems(Edit(m => m["files"]![0]!["fields"] = new JsonObject())).ShouldContain("File 'returns.csv' maps no value columns.");
    }

    [Fact]
    public void A_column_named_twice_in_any_case_is_refused()
    {
        var json = Edit(m => m["files"]![0]!["ignoredColumns"] = new JsonArray("Remarks", "ASSETS "));

        Problems(json).ShouldContain("File 'returns.csv' names column 'Assets' more than once.");
    }

    [Fact]
    public void A_role_column_mapped_as_a_value_column_is_refused()
    {
        var json = Edit(m => m["files"]![0]!["fields"]!["Bank"] = "BANK_NAME");

        Problems(json).ShouldContain("File 'returns.csv' names column 'Bank' more than once.");
    }

    [Fact]
    public void Two_columns_mapped_to_one_field_are_refused()
    {
        var json = Edit(m => m["files"]![0]!["fields"]!["Ratio %"] = "ASSETS");

        Problems(json).ShouldContain("File 'returns.csv' maps several columns to field ASSETS.");
    }

    [Fact]
    public void Every_problem_is_named_in_one_message()
    {
        var json = Edit(m =>
        {
            m["dateFormats"] = new JsonArray();
            m["files"]![0]!["returnType"] = string.Empty;
        });

        var message = Problems(json);

        message.ShouldStartWith("The mapping file is not valid: ");
        message.ShouldContain("List at least one date format.");
        message.ShouldContain("File 'returns.csv' needs a return type.");
    }

    private static Result<(LegacyMapping Mapping, string Sha256)> Parse(string json) => LegacyMapping.Parse(LegacyTestData.Utf8(json));

    private static string Problems(string json)
    {
        var error = Parse(json).Error.ShouldNotBeNull();
        error.Code.ShouldBe(MigrationErrors.MappingInvalid.Code);
        return error.Message;
    }

    private static string Edit(Action<JsonNode> change)
    {
        var mapping = JsonNode.Parse(LegacyTestData.MappingJson, documentOptions: new() { CommentHandling = System.Text.Json.JsonCommentHandling.Skip, AllowTrailingCommas = true })!;
        change(mapping);
        return mapping.ToJsonString();
    }
}
