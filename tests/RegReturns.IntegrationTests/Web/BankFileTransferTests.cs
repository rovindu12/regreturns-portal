using System.Net;
using System.Text;

using ClosedXML.Excel;

using Microsoft.AspNetCore.Mvc.Testing;

using RegReturns.Application.Returns;
using RegReturns.Domain.Submissions;
using RegReturns.Infrastructure.Files;
using RegReturns.Infrastructure.Persistence.Seeding;
using RegReturns.IntegrationTests.Hosting;

namespace RegReturns.IntegrationTests.Web;

[Collection(HostedAppsDefinition.Name)]
public sealed class BankFileTransferTests : IClassFixture<PortalDatabaseFixture>, IDisposable
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly BankPortal _portal;

    public BankFileTransferTests(PortalDatabaseFixture database)
    {
        _factory = PortalHost.Create(database.ConnectionString);
        _portal = new BankPortal(_factory, database.ConnectionString);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_csv_template_lists_every_field_under_a_descriptive_file_name()
    {
        var obligationId = await _portal.NewMlrObligationAsync(1);
        using var client = await _portal.CheckerAsync();

        var response = await client.GetAsync($"/bank/obligations/{obligationId}/download?format=csv", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.ShouldBe(ReturnFileFormats.CsvContentType);
        response.Content.Headers.ContentDisposition!.FileNameStar.ShouldBe($"{DemoBank.Harbourline}_{MlrTemplate.Code}_2027-01_v1.csv");
        response.Headers.GetValues("X-Content-Type-Options").ShouldBe(["nosniff"]);
        var csv = await response.Content.ReadAsStringAsync(Ct);
        csv.ShouldContain("FieldCode,Section,Label,Unit,Type,Value");
        csv.Split("\r\n").Count(line => line.Length > 0).ShouldBe(1 + BankPortal.ValidMlr.Count);
    }

    [Fact]
    public async Task A_filled_in_excel_template_uploads_into_a_new_draft_and_is_kept_as_evidence()
    {
        var obligationId = await _portal.NewMlrObligationAsync(2);
        using var client = await _portal.MakerAsync();
        var template = await client.GetByteArrayAsync($"/bank/obligations/{obligationId}/download?format=xlsx", Ct);

        var response = await BankPortal.UploadAsync(client, obligationId, "mlr-2027-02.xlsx", Fill(template, BankPortal.ValidMlr));

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var html = await client.GetStringAsync(response.Headers.Location!.OriginalString, Ct);
        html.ShouldContain("Loaded 11 of 11 fields from the file. Validation found no problems.");
        html.ShouldContain("mlr-2027-02.xlsx");
        var submission = await _portal.SubmissionAsync(BankPortal.SubmissionIdFrom(response));
        submission.Source.ShouldBe(SubmissionSource.Upload);
        submission.FindValue(MlrTemplate.Lcr)!.NumericValue.ShouldBe(216.67m);
        (await _portal.StoredFileCountAsync(obligationId)).ShouldBe(1);
    }

    [Fact]
    public async Task A_csv_upload_changes_only_the_fields_it_lists()
    {
        var obligationId = await _portal.NewMlrObligationAsync(3);
        using var client = await _portal.MakerAsync();
        var submissionId = await BankPortal.StartDraftAsync(client, obligationId);
        await BankPortal.SaveAsync(client, submissionId, BankPortal.ValidMlr);
        var csv = Encoding.UTF8.GetBytes($"FieldCode,Value\r\n{MlrTemplate.TotalDeposits},6000\r\n{MlrTemplate.LiquidAssets},\"1,800\"\r\n");

        var response = await BankPortal.UploadAsync(client, obligationId, "deposits.csv", csv);

        BankPortal.SubmissionIdFrom(response).ShouldBe(submissionId);
        var submission = await _portal.SubmissionAsync(submissionId);
        submission.FindValue(MlrTemplate.TotalDeposits)!.NumericValue.ShouldBe(6000m);
        submission.FindValue(MlrTemplate.LiquidAssets)!.NumericValue.ShouldBe(1800m);
        submission.FindValue(MlrTemplate.L1Hqla)!.NumericValue.ShouldBe(1000m);
        submission.CurrentFindings.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_file_whose_content_does_not_match_its_extension_is_refused_and_not_kept()
    {
        var obligationId = await _portal.NewMlrObligationAsync(4);
        using var client = await _portal.MakerAsync();
        var csv = Encoding.UTF8.GetBytes($"FieldCode,Value\r\n{MlrTemplate.L1Hqla},1000\r\n");

        var response = await BankPortal.UploadAsync(client, obligationId, "return.xlsx", csv);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync(Ct)).ShouldContain($"The file was not loaded. {UploadErrors.ContentMismatch.Message}");
        (await _portal.StoredFileCountAsync(obligationId)).ShouldBe(0);
    }

    [Fact]
    public async Task An_upload_naming_fields_the_return_does_not_have_lists_them()
    {
        var obligationId = await _portal.NewMlrObligationAsync(5);
        using var client = await _portal.MakerAsync();
        var csv = Encoding.UTF8.GetBytes("FieldCode,Value\r\nCET1_RATIO,12\r\n");

        var response = await BankPortal.UploadAsync(client, obligationId, "qcar.csv", csv);

        WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync(Ct)).ShouldContain("not in this return: CET1_RATIO.");
    }

    [Fact]
    public async Task A_checker_cannot_upload()
    {
        var obligationId = await _portal.NewMlrObligationAsync(6);
        using var maker = await _portal.MakerAsync();
        using var checker = await _portal.CheckerAsync();
        var token = await BankPortal.TokenFromAsync(maker, $"/bank/obligations/{obligationId}/upload");
        using var form = new MultipartFormDataContent
        {
            { new StringContent(token), PortalForms.TokenField },
            { new ByteArrayContent("FieldCode,Value\r\n"u8.ToArray()), "file", "return.csv" },
        };

        var response = await checker.PostAsync($"/bank/obligations/{obligationId}/upload", form, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    public void Dispose() => _factory.Dispose();

    private static byte[] Fill(byte[] template, Dictionary<string, string?> values)
    {
        using var workbook = new XLWorkbook(new MemoryStream(template));
        var sheet = workbook.Worksheet(ReturnFileWriter.SheetName);
        var header = sheet.Row(ReturnFileWriter.HeaderRow).CellsUsed().ToDictionary(c => c.GetString(), c => c.Address.ColumnNumber);
        var codeColumn = header[ReturnFileFormats.FieldCodeColumn];
        var valueColumn = header[ReturnFileFormats.ValueColumn];
        foreach (var row in sheet.RowsUsed().Where(r => r.RowNumber() > ReturnFileWriter.HeaderRow))
        {
            if (values.TryGetValue(row.Cell(codeColumn).GetString(), out var value))
            {
                row.Cell(valueColumn).Value = value;
            }
        }

        using var output = new MemoryStream();
        workbook.SaveAs(output);
        return output.ToArray();
    }
}
