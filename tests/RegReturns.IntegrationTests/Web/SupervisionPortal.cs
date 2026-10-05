using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;

using RegReturns.Application.Identity;
using RegReturns.Domain.Identity;
using RegReturns.Domain.Obligations;
using RegReturns.Infrastructure.Persistence.Seeding;
using RegReturns.IntegrationTests.Infrastructure;

namespace RegReturns.IntegrationTests.Web;

/// <summary>
/// Signs regulator staff (supervisors and the auditor) in to the portal under test, linking the seeded demo users as
/// first sign-in does, creates users holding two roles for segregation-of-duties tests, and posts the workflow forms.
/// </summary>
/// <param name="factory">The portal host.</param>
/// <param name="connectionString">The test class's own database.</param>
internal sealed class SupervisionPortal(WebApplicationFactory<Program> factory, string connectionString)
{
    /// <summary>The <c>amr</c> value of a TOTP sign-in, which the approval policy needs.</summary>
    public const string Totp = "totp";

    /// <summary>Gets a client signed in as the supervisor reviewer.</summary>
    public Task<HttpClient> ReviewerAsync() => SignInAsync(DemoUsers.Reviewer, [Role.SupervisorReviewer], bankCode: null, withTotp: false);

    /// <summary>Gets a client signed in as a supervisor approver.</summary>
    /// <param name="withTotp">Whether the sign-in included the TOTP step.</param>
    public Task<HttpClient> ApproverAsync(bool withTotp = true) =>
        SignInAsync(DemoUsers.Approver, [Role.SupervisorApprover], bankCode: null, withTotp);

    /// <summary>Gets a client signed in as the auditor.</summary>
    public Task<HttpClient> AuditorAsync() => SignInAsync(DemoUsers.Auditor, [Role.Auditor], bankCode: null, withTotp: false);

    /// <summary>
    /// Gets a client signed in as a user holding several roles, created on first use: the way to test the
    /// segregation-of-duties rules, since no demo user holds two roles.
    /// </summary>
    /// <param name="userName">A user name unique to the test.</param>
    /// <param name="roles">The roles.</param>
    /// <param name="bankCode">The bank, for bank roles.</param>
    /// <param name="withTotp">Whether the sign-in included the TOTP step.</param>
    public async Task<HttpClient> MultiRoleUserAsync(string userName, Role[] roles, string? bankCode, bool withTotp = false)
    {
        await using (var context = SqlServerFixture.CreateContext(connectionString))
        {
            if (!await context.Users.AnyAsync(u => u.UserName == userName))
            {
                Guid? institutionId = bankCode is null ? null : await context.Institutions.Where(i => i.Code == bankCode).Select(i => i.Id).SingleAsync();
                var user = AppUser.Create(userName, $"Test {userName}", $"{userName}@test.example", institutionId, roles).Value;
                await context.Users.AddAsync(user);
                await context.SaveChangesAsync();
            }
        }

        return await SignInAsync(userName, roles, bankCode, withTotp);
    }

    /// <summary>Returns an obligation's status.</summary>
    public async Task<ObligationStatus> ObligationStatusAsync(Guid obligationId)
    {
        await using var context = SqlServerFixture.CreateContext(connectionString);
        return await context.Obligations.Where(o => o.Id == obligationId).Select(o => o.Status).SingleAsync();
    }

    /// <summary>Posts a workflow step from the page it is offered on.</summary>
    /// <param name="client">The signed-in client.</param>
    /// <param name="page">The page holding the form.</param>
    /// <param name="action">The step's endpoint.</param>
    /// <param name="comment">The comment, if any.</param>
    public static Task<HttpResponseMessage> PostStepAsync(HttpClient client, string page, string action, string? comment = null) =>
        comment is null
            ? PortalForms.PostAsync(client, page, action)
            : PortalForms.PostAsync(client, page, action, ("comment", comment));

    private async Task<HttpClient> SignInAsync(string userName, Role[] roles, string? bankCode, bool withTotp)
    {
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

        var claims = new List<(string Type, string Value)> { (ClaimNames.Subject, subject), (ClaimNames.Name, subject) };
        claims.AddRange(roles.Select(r => (ClaimNames.Roles, RoleNames.For(r))));
        if (bankCode is not null)
        {
            claims.Add((ClaimNames.InstitutionId, bankCode));
        }

        claims.Add((ClaimNames.AuthenticationMethods, "BasicAuthenticator"));
        if (withTotp)
        {
            claims.Add((ClaimNames.AuthenticationMethods, Totp));
        }

        var client = factory.CreateClientAs([.. claims]);
        client.BaseAddress = PortalHost.BaseAddress;
        return client;
    }
}
