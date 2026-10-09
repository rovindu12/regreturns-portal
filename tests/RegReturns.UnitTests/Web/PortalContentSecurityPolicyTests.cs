using System.Text.RegularExpressions;

using RegReturns.Web.Security;

namespace RegReturns.UnitTests.Web;

/// <summary>
/// The portal's Content Security Policy (ADR 0033) and the rule that makes it hold: views render no inline scripts,
/// styles or event handlers, so the nonce on each <c>&lt;script src&gt;</c> is the only way a script runs.
/// </summary>
public sealed partial class PortalContentSecurityPolicyTests
{
    private const string SolutionFile = "RegReturns.slnx";

    [Fact]
    public void Scripts_run_only_with_the_nonce()
    {
        var policy = PortalSecurityHeaders.ContentSecurityPolicy("abc123==", "https://iam.example");

        policy.ShouldContain("script-src 'nonce-abc123==';");
        policy.ShouldNotContain("unsafe-inline");
        policy.ShouldNotContain("unsafe-eval");
        policy.ShouldNotContain("*");
    }

    [Fact]
    public void Forms_may_lead_only_to_the_portal_and_the_identity_provider()
    {
        var policy = PortalSecurityHeaders.ContentSecurityPolicy("n", "https://iam.example");

        policy.ShouldContain("form-action 'self' https://iam.example;");
        policy.ShouldContain("frame-ancestors 'none';");
        policy.ShouldContain("base-uri 'none';");
        policy.ShouldContain("object-src 'none';");
    }

    [Fact]
    public void Views_render_no_inline_scripts_styles_or_event_handlers()
    {
        var views = Directory.EnumerateFiles(Path.Combine(RepositoryRoot(), "src", "RegReturns.Web", "Views"), "*.cshtml", SearchOption.AllDirectories)
            .ToList();
        views.ShouldNotBeEmpty();

        var offenders = views
            .SelectMany(path => Offences(File.ReadAllText(path)).Select(offence => $"{Path.GetFileName(path)}: {offence}"))
            .ToList();

        offenders.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("<script>alert(1)</script>")]
    [InlineData("<script type=\"module\">go()</script>")]
    [InlineData("<style>p { color: red }</style>")]
    [InlineData("<p style=\"color: red\">x</p>")]
    [InlineData("<button onclick=\"go()\">x</button>")]
    [InlineData("<a href=\"javascript:go()\">x</a>")]
    public void The_view_check_finds_inline_code(string markup)
    {
        Offences(markup).ShouldNotBeEmpty();
    }

    [Theory]
    [InlineData("<script src=\"~/js/site.js\" asp-append-version=\"true\"></script>")]
    [InlineData("<div data-chart=\"{}\" class=\"d-none\"></div>")]
    [InlineData("<p>Click on the button</p>")]
    public void The_view_check_allows_files_and_data(string markup)
    {
        Offences(markup).ShouldBeEmpty();
    }

    private static IEnumerable<string> Offences(string markup) =>
        new[] { InlineScript(), StyleElement(), StyleAttribute(), EventHandler(), ScriptUrl() }
            .SelectMany(pattern => pattern.Matches(markup).Select(match => match.Value));

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, SolutionFile)))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException($"{SolutionFile} was not found above {AppContext.BaseDirectory}.");
    }

    [GeneratedRegex("<script(?![^>]*\\bsrc=)[^>]*>", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 1000)]
    private static partial Regex InlineScript();

    [GeneratedRegex("<style\\b", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 1000)]
    private static partial Regex StyleElement();

    [GeneratedRegex("\\sstyle\\s*=", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 1000)]
    private static partial Regex StyleAttribute();

    [GeneratedRegex("<[^>]+\\son[a-z]+\\s*=", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 1000)]
    private static partial Regex EventHandler();

    [GeneratedRegex("javascript:", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 1000)]
    private static partial Regex ScriptUrl();
}
