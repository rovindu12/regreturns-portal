using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using RegReturns.Application.Authorization;
using RegReturns.Application.Identity;

namespace RegReturns.Infrastructure.Identity.Authorization;

/// <summary>Registers the RegReturns authorization policies (plan §4.5) used by both the portal and the API.</summary>
public static class AuthorizationExtensions
{
    /// <summary>
    /// Adds every policy in <see cref="Policies"/>, their handlers and <see cref="IamOptions"/>, and makes
    /// "authenticated user" the fallback so an endpoint without a policy is never public by accident.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">Application configuration.</param>
    /// <returns>The same service collection.</returns>
    public static IServiceCollection AddRegReturnsAuthorization(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<IamOptions>().Bind(configuration.GetSection(IamOptions.SectionName)).ValidateOnStart();
        services.AddSingleton<IAuthorizationHandler, MfaRequirementHandler>();
        services.AddSingleton<IAuthorizationHandler, InstitutionMemberRequirementHandler>();
        services.AddSingleton<IAuthorizationHandler, ScopeRequirementHandler>();
        services.AddAuthorizationCore(Configure);
        return services;
    }

    /// <summary>
    /// Defines the policies; public so tests can evaluate them without a host. API policies need a scope and an
    /// institution: the API adds <c>institution_id</c> only for clients registered in <c>iam.ApiClients</c>.
    /// </summary>
    /// <param name="options">The authorization options.</param>
    public static void Configure(AuthorizationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var authenticated = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
        options.DefaultPolicy = authenticated;
        options.FallbackPolicy = authenticated;

        options.AddPolicy(Policies.BankAccess, p => p
            .RequireAuthenticatedUser()
            .RequireClaim(ClaimNames.Roles, RoleNames.BankMaker, RoleNames.BankChecker)
            .AddRequirements(new InstitutionMemberRequirement()));
        options.AddPolicy(Policies.BankPrepareReturn, p => p
            .RequireAuthenticatedUser()
            .RequireClaim(ClaimNames.Roles, RoleNames.BankMaker)
            .AddRequirements(new InstitutionMemberRequirement()));
        options.AddPolicy(Policies.BankSubmitReturn, p => p
            .RequireAuthenticatedUser()
            .RequireClaim(ClaimNames.Roles, RoleNames.BankChecker)
            .AddRequirements(new InstitutionMemberRequirement()));

        options.AddPolicy(Policies.SupervisionAccess, p => p
            .RequireAuthenticatedUser()
            .RequireClaim(ClaimNames.Roles, RoleNames.SupervisorReviewer, RoleNames.SupervisorApprover));
        options.AddPolicy(Policies.SupervisionReview, p => p
            .RequireAuthenticatedUser()
            .RequireClaim(ClaimNames.Roles, RoleNames.SupervisorReviewer));
        options.AddPolicy(Policies.SupervisionApprove, p => p
            .RequireAuthenticatedUser()
            .RequireClaim(ClaimNames.Roles, RoleNames.SupervisorApprover)
            .AddRequirements(new MfaRequirement()));

        options.AddPolicy(Policies.AdminManage, p => p
            .RequireAuthenticatedUser()
            .RequireClaim(ClaimNames.Roles, RoleNames.SystemAdmin)
            .AddRequirements(new MfaRequirement()));
        options.AddPolicy(Policies.AuditRead, p => p
            .RequireAuthenticatedUser()
            .RequireClaim(ClaimNames.Roles, RoleNames.Auditor, RoleNames.SystemAdmin));
        options.AddPolicy(Policies.ReportsView, p => p
            .RequireAuthenticatedUser()
            .RequireClaim(ClaimNames.Roles, RoleNames.All.ToArray()));

        options.AddPolicy(Policies.ApiReturnsRead, p => p
            .RequireAuthenticatedUser()
            .AddRequirements(new ScopeRequirement(ApiScopes.ReturnsRead), new InstitutionMemberRequirement()));
        options.AddPolicy(Policies.ApiReturnsSubmit, p => p
            .RequireAuthenticatedUser()
            .AddRequirements(new ScopeRequirement(ApiScopes.ReturnsSubmit), new InstitutionMemberRequirement()));
        options.AddPolicy(Policies.ApiReferenceRead, p => p
            .RequireAuthenticatedUser()
            .AddRequirements(new ScopeRequirement(ApiScopes.ReferenceRead), new InstitutionMemberRequirement()));
    }
}
