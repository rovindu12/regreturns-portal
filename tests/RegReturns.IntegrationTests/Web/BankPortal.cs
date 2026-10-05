using System.Net;
using System.Text.RegularExpressions;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;

using RegReturns.Application.Identity;
using RegReturns.Domain.Identity;
using RegReturns.Domain.Obligations;
using RegReturns.Domain.Periods;
using RegReturns.Domain.Submissions;
using RegReturns.Infrastructure.Persistence.Seeding;
using RegReturns.IntegrationTests.Infrastructure;

namespace RegReturns.IntegrationTests.Web;

/// <summary>
/// Signs bank users in to the portal under test and prepares data for bank page tests: links seeded demo users to
/// test subjects (as first sign-in does) and adds obligations for future periods, so every test owns its return.
/// </summary>
/// <param name="factory">The portal host.</param>
/// <param name="connectionString">The test class's own database.</param>
internal sealed partial class BankPortal(WebApplicationFactory<Program> factory, string connectionString)
{
    /// <summary>Valid figures for the Monthly Liquidity Return: every rule passes and there is no prior period.</summary>
    public static readonly Dictionary<string, string?> ValidMlr = new(StringComparer.Ordinal)
    {
        [MlrTemplate.L1Hqla] = "1000",
        [MlrTemplate.L2aHqla] = "200",
        [MlrTemplate.L2bHqla] = "100",
        [MlrTemplate.TotalHqla] = "1300",
        [MlrTemplate.Outflows] = "1000",
        [MlrTemplate.Inflows] = "400",
        [MlrTemplate.NetOutflows] = "600",
        [MlrTemplate.Lcr] = "216.67",
        [MlrTemplate.TotalDeposits] = "5000",
        [MlrTemplate.LiquidAssets] = "1500",
        [MlrTemplate.LiquidAssetsRatio] = "30",
    };

    /// <summary>Gets a client signed in as Harbourline's maker.</summary>
    public Task<HttpClient> MakerAsync() => SignInAsync(DemoBank.Harbourline, Role.BankMaker);

    /// <summary>Gets a client signed in as Harbourline's checker.</summary>
    public Task<HttpClient> CheckerAsync() => SignInAsync(DemoBank.Harbourline, Role.BankChecker);

    /// <summary>Gets a client signed in as another bank's maker.</summary>
    public Task<HttpClient> OtherBankMakerAsync() => SignInAsync(DemoBank.Crestmont, Role.BankMaker);

    /// <summary>Gets a client signed in as a maker whose WSO2 account is not linked to a portal user.</summary>
    public HttpClient UnlinkedMaker() => Client("unlinked-maker", DemoBank.Harbourline, RoleNames.BankMaker);

    /// <summary>Adds an open Monthly Liquidity Return obligation for Harbourline for a future month.</summary>
    /// <param name="month">The month, unique per test of the class.</param>
    /// <param name="year">The year.</param>
    public async Task<Guid> NewMlrObligationAsync(int month, int year = 2027)
    {
        await using var context = SqlServerFixture.CreateContext(connectionString);
        var bank = await context.Institutions.SingleAsync(i => i.Code == DemoBank.Harbourline);
        var returnType = await context.ReturnTypes.SingleAsync(r => r.Code == MlrTemplate.Code);
        var obligation = ReturnObligation.Create(bank.Id, returnType, ReportingPeriod.Monthly(year, month));
        await context.Obligations.AddAsync(obligation);
        await context.SaveChangesAsync();
        return obligation.Id;
    }

    /// <summary>Returns Harbourline's seeded Monthly Liquidity Return obligation for the month being filed (nothing filed yet).</summary>
    public async Task<Guid> CurrentMlrObligationAsync()
    {
        await using var context = SqlServerFixture.CreateContext(connectionString);
        var bank = await context.Institutions.SingleAsync(i => i.Code == DemoBank.Harbourline);
        var returnType = await context.ReturnTypes.SingleAsync(r => r.Code == MlrTemplate.Code);
        return await context.Obligations
            .Where(o => o.InstitutionId == bank.Id && o.ReturnTypeId == returnType.Id && o.Status == ObligationStatus.Open)
            .Where(o => !context.Submissions.Any(s => s.ObligationId == o.Id))
            .Where(o => o.Period.Year == 2026)
            .Select(o => o.Id)
            .SingleAsync();
    }

    /// <summary>Loads a submission with its values and findings.</summary>
    public async Task<Submission> SubmissionAsync(Guid submissionId)
    {
        await using var context = SqlServerFixture.CreateContext(connectionString);
        return await context.Submissions.AsNoTracking()
            .Include(s => s.Values)
            .Include(s => s.Findings)
            .SingleAsync(s => s.Id == submissionId);
    }

    /// <summary>Returns the id of one of Harbourline's seeded returns in the given status.</summary>
    public async Task<Guid> HarbourlineSubmissionAsync(SubmissionStatus status)
    {
        await using var context = SqlServerFixture.CreateContext(connectionString);
        return await context.Submissions
            .Where(s => s.Status == status && context.Institutions.Any(i => i.Id == s.InstitutionId && i.Code == DemoBank.Harbourline))
            .Select(s => s.Id)
            .FirstAsync();
    }

    /// <summary>Counts the files stored for an obligation's returns.</summary>
    public async Task<int> StoredFileCountAsync(Guid obligationId)
    {
        await using var context = SqlServerFixture.CreateContext(connectionString);
        return await context.StoredFiles.CountAsync(f => context.Submissions.Any(s => s.Id == f.SubmissionId && s.ObligationId == obligationId));
    }

    /// <summary>Starts a draft for an obligation through the overview page's form.</summary>
    /// <returns>The submission id.</returns>
    public static async Task<Guid> StartDraftAsync(HttpClient client, Guid obligationId)
    {
        var token = await TokenFromAsync(client, "/bank");
        var response = await client.PostAsync(
            $"/bank/obligations/{obligationId}/start", Form(token), TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        return SubmissionIdFrom(response);
    }

    /// <summary>Posts the entry form of a return.</summary>
    public static async Task<HttpResponseMessage> SaveAsync(
        HttpClient client, Guid submissionId, IReadOnlyDictionary<string, string?> values, int? editVersion = null)
    {
        var page = await client.GetStringAsync($"/bank/returns/{submissionId}", TestContext.Current.CancellationToken);
        var fields = values.Select(v => new KeyValuePair<string, string>($"Values[{v.Key}]", v.Value ?? string.Empty))
            .Append(new("EditVersion", editVersion?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? EditVersionIn(page)));
        return await client.PostAsync($"/bank/returns/{submissionId}", Form(PortalForms.TokenFrom(page), fields), TestContext.Current.CancellationToken);
    }

    /// <summary>Uploads a file through an obligation's upload page, as the browser does.</summary>
    public static async Task<HttpResponseMessage> UploadAsync(HttpClient client, Guid obligationId, string fileName, byte[] content)
    {
        var path = $"/bank/obligations/{obligationId}/upload";
        var token = await TokenFromAsync(client, path);
        using var form = new MultipartFormDataContent
        {
            { new StringContent(token), PortalForms.TokenField },
            { new ByteArrayContent(content), "file", fileName },
        };
        return await client.PostAsync(path, form, TestContext.Current.CancellationToken);
    }

    /// <summary>Reads the antiforgery token of a page.</summary>
    public static async Task<string> TokenFromAsync(HttpClient client, string path) =>
        PortalForms.TokenFrom(await PortalForms.GetHtmlAsync(client, path));

    /// <summary>Builds a form post with the antiforgery token.</summary>
    public static FormUrlEncodedContent Form(string token, IEnumerable<KeyValuePair<string, string>>? fields = null) =>
        new((fields ?? []).Append(new(PortalForms.TokenField, token)));

    /// <summary>Reads the submission id from a redirect to a return page.</summary>
    public static Guid SubmissionIdFrom(HttpResponseMessage response)
    {
        var location = response.Headers.Location!.OriginalString;
        var match = ReturnPath().Match(location);
        match.Success.ShouldBeTrue($"Expected a redirect to a return, got {location}.");
        return Guid.Parse(match.Groups[1].Value);
    }

    private static string EditVersionIn(string html)
    {
        var match = EditVersionInput().Match(html);
        match.Success.ShouldBeTrue("The page has no edit version.");
        return match.Groups[1].Value;
    }

    private async Task<HttpClient> SignInAsync(string bankCode, Role role)
    {
        var userName = role == Role.BankMaker ? DemoUsers.MakerUserName(bankCode) : DemoUsers.CheckerUserName(bankCode);
        var subject = $"it-{userName}";
        await using (var context = SqlServerFixture.CreateContext(connectionString))
        {
            var user = await context.Users.SingleAsync(u => u.UserName == userName);
            if (user.Wso2UserId != subject)
            {
                user.LinkIdentity(subject);
                await context.SaveChangesAsync();
            }
        }

        return Client(subject, bankCode, role == Role.BankMaker ? RoleNames.BankMaker : RoleNames.BankChecker);
    }

    private HttpClient Client(string subject, string bankCode, string roleName)
    {
        var client = factory.CreateClientAs(
            (ClaimNames.Subject, subject),
            (ClaimNames.Name, subject),
            (ClaimNames.Roles, roleName),
            (ClaimNames.InstitutionId, bankCode));
        client.BaseAddress = PortalHost.BaseAddress;
        return client;
    }

    [GeneratedRegex("<input[^>]*name=\"EditVersion\"[^>]*value=\"(-?[0-9]+)\"")]
    private static partial Regex EditVersionInput();

    [GeneratedRegex("^/bank/returns/([0-9a-f-]{36})")]
    private static partial Regex ReturnPath();
}
