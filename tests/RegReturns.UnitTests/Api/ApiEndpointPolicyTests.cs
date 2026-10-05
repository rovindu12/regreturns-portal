using System.Reflection;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

using NetArchTest.Rules;

using RegReturns.Api.Controllers;
using RegReturns.Api.Idempotency;
using RegReturns.Infrastructure.Identity.Authorization;

namespace RegReturns.UnitTests.Api;

public sealed class ApiEndpointPolicyTests
{
    private static readonly Assembly Api = typeof(MeController).Assembly;

    private static readonly IReadOnlyList<Type> Controllers =
        [.. Api.GetTypes().Where(t => t is { IsPublic: true, IsAbstract: false } && typeof(ControllerBase).IsAssignableFrom(t))];

    [Fact]
    public void Every_api_action_names_a_policy_or_allows_anonymous()
    {
        var offenders = Controllers
            .SelectMany(controller => Actions(controller)
                .Where(action => !NamesPolicy(controller) && !NamesPolicy(action)
                    && !AllowsAnonymous(controller) && !AllowsAnonymous(action))
                .Select(action => $"{controller.Name}.{action.Name}"))
            .ToList();

        Controllers.ShouldNotBeEmpty();
        offenders.ShouldBeEmpty();
    }

    [Fact]
    public void Every_policy_named_by_the_api_is_registered()
    {
        var options = new AuthorizationOptions();
        AuthorizationExtensions.Configure(options);

        var unknown = Controllers
            .SelectMany(controller => Actions(controller).Cast<MemberInfo>().Append(controller))
            .SelectMany(member => member.GetCustomAttributes<AuthorizeAttribute>(inherit: true))
            .Select(attribute => attribute.Policy)
            .OfType<string>()
            .Where(policy => options.GetPolicy(policy) is null)
            .ToList();

        unknown.ShouldBeEmpty();
    }

    [Fact]
    public void Every_post_action_needs_an_idempotency_key()
    {
        var offenders = Controllers
            .SelectMany(controller => Actions(controller)
                .Where(action => action.GetCustomAttribute<HttpPostAttribute>() is not null
                    && action.GetCustomAttribute<IdempotentAttribute>() is null)
                .Select(action => $"{controller.Name}.{action.Name}"))
            .ToList();

        offenders.ShouldBeEmpty();
    }

    [Fact]
    public void Api_controllers_do_not_use_the_database_directly()
    {
        var result = Types.InAssembly(Api).That().Inherit(typeof(ControllerBase)).ShouldNot()
            .HaveDependencyOnAny(typeof(DbContext).Namespace!, "RegReturns.Infrastructure.Persistence")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue("Violations: " + string.Join(", ", result.FailingTypeNames ?? []));
    }

    private static IEnumerable<MethodInfo> Actions(Type controller) =>
        controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(method => !method.IsSpecialName && method.GetCustomAttribute<NonActionAttribute>() is null);

    private static bool NamesPolicy(MemberInfo member) =>
        member.GetCustomAttributes<AuthorizeAttribute>(inherit: true).Any(a => !string.IsNullOrWhiteSpace(a.Policy));

    private static bool AllowsAnonymous(MemberInfo member) =>
        member.GetCustomAttributes<AllowAnonymousAttribute>(inherit: true).Any();
}
