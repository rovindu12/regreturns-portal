using Microsoft.EntityFrameworkCore;

using RegReturns.Domain.Obligations;
using RegReturns.Domain.Submissions;
using RegReturns.Domain.Templates;
using RegReturns.Infrastructure.Persistence;
using RegReturns.Infrastructure.Persistence.Seeding;
using RegReturns.IntegrationTests.Infrastructure;
using RegReturns.IntegrationTests.Web;

namespace RegReturns.IntegrationTests.Database;

public sealed class LiveSubmissionIndexTests(PortalDatabaseFixture database) : IClassFixture<PortalDatabaseFixture>
{
    [Fact]
    public async Task The_database_refuses_a_second_live_return_for_an_obligation()
    {
        var ct = TestContext.Current.CancellationToken;
        var obligationId = await OpenMlrObligationAsync(ct);
        await AddDraftAsync(obligationId, ct);

        var second = await Should.ThrowAsync<DbUpdateException>(() => AddDraftAsync(obligationId, ct));

        second.InnerException!.Message.ShouldContain("UX_Submissions_LiveObligation");
    }

    private async Task AddDraftAsync(Guid obligationId, CancellationToken ct)
    {
        await using var context = SqlServerFixture.CreateContext(database.ConnectionString);
        var obligation = await context.Obligations.SingleAsync(o => o.Id == obligationId, ct);
        var template = await PublishedMlrTemplateAsync(context, ct);
        var maker = await context.Users.SingleAsync(u => u.UserName == DemoUsers.MakerUserName(DemoBank.Harbourline), ct);
        var draft = Submission.CreateDraft(obligation, template, maker.ToActor(), SubmissionSource.Web, SqlServerFixture.SeedDate).Value;
        await context.Submissions.AddAsync(draft, ct);
        await context.SaveChangesAsync(ct);
    }

    private async Task<Guid> OpenMlrObligationAsync(CancellationToken ct)
    {
        await using var context = SqlServerFixture.CreateContext(database.ConnectionString);
        var bank = await context.Institutions.SingleAsync(i => i.Code == DemoBank.Harbourline, ct);
        var returnType = await context.ReturnTypes.SingleAsync(r => r.Code == MlrTemplate.Code, ct);
        return await context.Obligations
            .Where(o => o.InstitutionId == bank.Id && o.ReturnTypeId == returnType.Id && o.Status == ObligationStatus.Open)
            .Where(o => !context.Submissions.Any(s => s.ObligationId == o.Id))
            .Select(o => o.Id)
            .FirstAsync(ct);
    }

    private static async Task<TemplateVersion> PublishedMlrTemplateAsync(RegReturnsDbContext context, CancellationToken ct)
    {
        var returnType = await context.ReturnTypes.SingleAsync(r => r.Code == MlrTemplate.Code, ct);
        return await context.TemplateVersions
            .Include(v => v.Fields)
            .Include(v => v.Rules)
            .SingleAsync(v => v.ReturnTypeId == returnType.Id && v.Status == TemplateStatus.Published, ct);
    }
}
