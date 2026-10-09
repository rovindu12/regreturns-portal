using System.Reflection;

using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

using NetArchTest.Rules;

namespace RegReturns.UnitTests.Architecture;

public sealed class LayeringTests
{
    private static readonly Assembly Domain = typeof(RegReturns.Domain.Common.Entity).Assembly;
    private static readonly Assembly Application = typeof(RegReturns.Application.DependencyInjection).Assembly;
    private static readonly Assembly Infrastructure = typeof(RegReturns.Infrastructure.DependencyInjection).Assembly;
    private static readonly Assembly Web = typeof(RegReturns.Web.Controllers.HomeController).Assembly;

    [Fact]
    public void Domain_depends_on_no_other_layer_or_framework()
    {
        var result = Types.InAssembly(Domain).ShouldNot()
            .HaveDependencyOnAny(
                "RegReturns.Application", "RegReturns.Infrastructure", "RegReturns.Web", "RegReturns.Api",
                "Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore", "Serilog")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(Describe(result));
    }

    [Fact]
    public void Application_does_not_depend_on_infrastructure_or_hosts()
    {
        var result = Types.InAssembly(Application).ShouldNot()
            .HaveDependencyOnAny("RegReturns.Infrastructure", "RegReturns.Web", "RegReturns.Api", "Microsoft.AspNetCore")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(Describe(result));
    }

    [Fact]
    public void Infrastructure_does_not_depend_on_hosts()
    {
        var result = Types.InAssembly(Infrastructure).ShouldNot()
            .HaveDependencyOnAny("RegReturns.Web", "RegReturns.Api")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(Describe(result));
    }

    [Fact]
    public void Only_infrastructure_talks_to_the_ai_provider()
    {
        var result = Types.InAssemblies([Domain, Application, Web]).ShouldNot()
            .HaveDependencyOn("Anthropic")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(Describe(result));
    }

    [Fact]
    public void Controllers_do_not_use_the_database_directly()
    {
        var result = Types.InAssembly(Web).That().Inherit(typeof(ControllerBase)).ShouldNot()
            .HaveDependencyOnAny(typeof(DbContext).Namespace!, "RegReturns.Infrastructure.Persistence")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(Describe(result));
    }

    [Fact]
    public void Domain_entities_have_no_public_setters()
    {
        var offenders = Domain.GetTypes()
            .Where(t => t.IsClass && t.Namespace?.StartsWith("RegReturns.Domain", StringComparison.Ordinal) == true)
            .SelectMany(t => t.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(p => p.SetMethod?.IsPublic == true && !IsInitOnly(p))
                .Select(p => $"{t.Name}.{p.Name}"))
            .ToList();

        offenders.ShouldBeEmpty();
    }

    private static bool IsInitOnly(PropertyInfo property) =>
        property.SetMethod!.ReturnParameter.GetRequiredCustomModifiers()
            .Any(m => m.FullName == "System.Runtime.CompilerServices.IsExternalInit");

    private static string Describe(NetArchTest.Rules.TestResult result) =>
        "Violations: " + string.Join(", ", result.FailingTypeNames ?? []);
}
