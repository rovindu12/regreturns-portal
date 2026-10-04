using RegReturns.Domain.Identity;
using RegReturns.Domain.Institutions;
using RegReturns.Domain.Obligations;
using RegReturns.Domain.Submissions;
using RegReturns.Domain.Templates;

namespace RegReturns.Infrastructure.Persistence.Seeding;

/// <summary>The complete demo data set, built in memory before it is saved.</summary>
internal sealed record DemoDataSet(
    IReadOnlyList<Institution> Institutions,
    IReadOnlyList<AppUser> Users,
    IReadOnlyList<ReturnType> ReturnTypes,
    IReadOnlyList<TemplateVersion> Templates,
    IReadOnlyList<ReturnObligation> Obligations,
    IReadOnlyList<Submission> Submissions);
