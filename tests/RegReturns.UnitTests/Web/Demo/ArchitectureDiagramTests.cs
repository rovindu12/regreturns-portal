namespace RegReturns.UnitTests.Web.Demo;

public sealed class ArchitectureDiagramTests
{
    private const string SolutionFile = "RegReturns.slnx";

    [Fact]
    public void The_landing_page_diagram_is_plain_svg_rendered_from_its_mermaid_source()
    {
        var root = RepositoryRoot();
        var svg = File.ReadAllText(Path.Combine(root, "src", "RegReturns.Web", "wwwroot", "img", "architecture.svg"));

        File.Exists(Path.Combine(root, "docs", "diagrams", "architecture.mmd")).ShouldBeTrue();
        svg.ShouldStartWith("<svg");
        svg.ShouldContain("role=\"graphics-document document\"");

        // Rendered by scripts/render-diagrams.sh without HTML labels or scripts, so it is safe and complete as an <img>.
        svg.ShouldNotContain("foreignObject");
        svg.ShouldNotContain("<script", Case.Insensitive);
        svg.ShouldContain("WSO2");
    }

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
}
