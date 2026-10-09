using System.Text.RegularExpressions;

namespace RegReturns.UnitTests.Deployment;

/// <summary>
/// Third-party images are pinned by tag in several files (ADR 0034); Dependabot bumps some of them, so these tests fail
/// when one copy moves without the others.
/// </summary>
public sealed partial class ImagePinTests
{
    private const string SolutionFile = "RegReturns.slnx";

    private static readonly string[] SqlServerPins =
    [
        "docker-compose.yml",
        "docker-compose.prod.yml",
        "deploy/wso2/db-init/Dockerfile",
        "tests/RegReturns.IntegrationTests/Infrastructure/SqlServerFixture.cs",
    ];

    private static readonly string[] Wso2Pins = ["deploy/wso2/Dockerfile", "deploy/wso2/db-init/Dockerfile", "scripts/dev-certs.sh"];

    [Fact]
    public void Every_file_runs_the_same_sql_server_image()
    {
        var tags = SqlServerPins.SelectMany(file => SqlServerImage().Matches(Read(file)).Select(m => (File: file, Tag: m.Groups["tag"].Value))).ToList();

        tags.Select(t => t.File).Distinct().ShouldBe(SqlServerPins, ignoreOrder: true);
        tags.Select(t => t.Tag).Distinct().ShouldHaveSingleItem().ShouldNotContain("latest");
    }

    [Fact]
    public void Every_file_builds_on_the_same_wso2_release()
    {
        var tags = Wso2Pins.SelectMany(file => Wso2Image().Matches(Read(file)).Select(m => (File: file, Tag: m.Groups["tag"].Value))).ToList();

        tags.Select(t => t.File).Distinct().ShouldBe(Wso2Pins, ignoreOrder: true);
        tags.Select(t => t.Tag).Distinct().ShouldHaveSingleItem();
    }

    [Fact]
    public void The_edge_check_tests_the_caddy_image_production_runs()
    {
        var production = CaddyImage().Match(Read("docker-compose.prod.yml"));
        var check = CaddyImage().Match(Read("scripts/check-caddy.sh"));

        production.Success.ShouldBeTrue();
        check.Groups["tag"].Value.ShouldBe(production.Groups["tag"].Value);
    }

    [Fact]
    public void The_portal_api_and_tools_share_one_runtime_image()
    {
        DotnetRuntimeImage().Matches(Read("Dockerfile")).ShouldHaveSingleItem();
    }

    [Theory]
    [InlineData("docker-compose.yml")]
    [InlineData("docker-compose.prod.yml")]
    public void Compose_files_never_run_a_floating_tag(string file)
    {
        var images = ComposeImage().Matches(Read(file)).Select(m => m.Groups["image"].Value).ToList();

        images.ShouldNotBeEmpty();
        images.ShouldAllBe(image => image.Contains(':') && !image.EndsWith(":latest", StringComparison.Ordinal));
    }

    private static string Read(string relativePath) => File.ReadAllText(Path.Combine(RepositoryRoot(), relativePath));

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

    [GeneratedRegex(@"mcr\.microsoft\.com/mssql/server:(?<tag>[A-Za-z0-9._-]+)", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex SqlServerImage();

    [GeneratedRegex(@"wso2/wso2is:(?<tag>[A-Za-z0-9._-]+)", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex Wso2Image();

    [GeneratedRegex(@"\bcaddy:(?<tag>[0-9][A-Za-z0-9._-]+)", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex CaddyImage();

    [GeneratedRegex(@"^FROM mcr\.microsoft\.com/dotnet/(?:aspnet|runtime):", RegexOptions.CultureInvariant | RegexOptions.Multiline, matchTimeoutMilliseconds: 1000)]
    private static partial Regex DotnetRuntimeImage();

    // Literal image references only: the apps' own images are ${REGISTRY}/...:${IMAGE_TAG} and locally built ones have none.
    [GeneratedRegex(@"^\s+image:\s+(?:&[a-z-]+\s+)?(?<image>[a-z0-9][^\s$]*)\s*$", RegexOptions.CultureInvariant | RegexOptions.Multiline, matchTimeoutMilliseconds: 1000)]
    private static partial Regex ComposeImage();
}
