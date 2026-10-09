extern alias IamBootstrapTool;

using IamBootstrapTool::RegReturns.IamBootstrap;
using IamBootstrapTool::RegReturns.IamBootstrap.Steps;

using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace RegReturns.IntegrationTests.Wso2Smoke;

/// <summary>
/// Runs the IAM setup tool against the live WSO2 and SQL Server from <c>docker compose</c>. Opt in with
/// <c>REGRETURNS_WSO2_SMOKE=1</c>; it needs the repository's <c>.env</c> and <c>.env.generated</c> (from an earlier
/// <c>apply</c>), the dev CA in <c>.certs/</c> and the seeded database.
/// </summary>
public sealed class IamBootstrapSmokeTests
{
    private const string OptInVariable = "REGRETURNS_WSO2_SMOKE";
    private const string ConnectionStringVariable = "ConnectionStrings__RegReturns";
    private const string SolutionFile = "RegReturns.slnx";
    private const string ToolSettings = "tools/RegReturns.IamBootstrap/appsettings.json";
    private const string DevCertificate = ".certs/regreturns-dev-ca.crt";
    private const string GeneratedSettings = ".env.generated";

    [Fact]
    [Trait("Category", "Wso2Smoke")]
    public async Task Second_apply_finds_nothing_to_create_or_update()
    {
        Assert.SkipUnless(
            Environment.GetEnvironmentVariable(OptInVariable) == "1",
            $"Live WSO2 smoke test: set {OptInVariable}=1 with WSO2, SQL Server and the seeded database running.");

        var root = RepositoryRoot();
        var output = Directory.CreateTempSubdirectory("regreturns-wso2-smoke-");
        try
        {
            // The runs read and write a copy: the real file holds the secrets the running portal and the approver's
            // authenticator use, and without the known TOTP secret the tool would re-enrol the MFA demo user.
            var source = Path.Combine(root, GeneratedSettings);
            File.Exists(source).ShouldBeTrue($"Run the IamBootstrap `apply` command once so {source} exists.");
            var envFile = Path.Combine(output.FullName, GeneratedSettings);
            File.Copy(source, envFile);
            var overrides = Overrides(root, envFile);

            Report("First", await ApplyAsync(overrides, envFile));
            var second = await ApplyAsync(overrides, envFile);
            Report("Second", second);

            second.Changes.ShouldNotBeEmpty("the run checked no objects at all");
            Changed(second).ShouldBeEmpty();
        }
        finally
        {
            output.Delete(recursive: true);
        }
    }

    private static async Task<BootstrapState> ApplyAsync(Dictionary<string, string?> overrides, string envFile)
    {
        using var host = BootstrapCommands.BuildHost(overrides);
        await using var scope = host.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<BootstrapRunner>()
            .RunAsync([], envFile, resetDemoUsers: false, TestContext.Current.CancellationToken);
    }

    // Kinds, names and outcomes only: the state's generated settings hold secrets and are never printed.
    private static List<string> Changed(BootstrapState state) =>
        [.. state.Changes.Where(c => c.Outcome != Outcome.Unchanged).Select(c => $"{c.Kind} {c.Name}: {c.Outcome}")];

    private static void Report(string run, BootstrapState state) =>
        TestContext.Current.TestOutputHelper?.WriteLine(string.Join(
            Environment.NewLine,
            [
                $"{run} run: {state.Changes.Count(c => c.Outcome == Outcome.Created)} created, "
                    + $"{state.Changes.Count(c => c.Outcome == Outcome.Updated)} updated, "
                    + $"{state.Changes.Count(c => c.Outcome == Outcome.Unchanged)} unchanged.",
                .. Changed(state).Select(change => $"  {change}"),
            ]));

    private static Dictionary<string, string?> Overrides(string root, string envFile)
    {
        var dotEnvPath = Path.Combine(root, ".env");
        File.Exists(dotEnvPath).ShouldBeTrue($"Copy .env.example to .env in {root} and fill in the secrets first.");
        var dotEnv = DotEnvFile.Read(dotEnvPath);

        // The test bin holds several appsettings.json files (portal, API, tool); load the tool's own explicitly.
        var overrides = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(root, ToolSettings), optional: false)
            .Build()
            .AsEnumerable()
            .Where(pair => pair.Value is not null)
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);

        foreach (var (key, value) in DotEnvFile.ToConfiguration(dotEnv))
        {
            overrides[key] = value;
        }

        overrides["ConnectionStrings:RegReturns"] = ConnectionString(dotEnv, root);
        overrides["Wso2:TrustedCaPath"] = Path.Combine(root, DevCertificate);
        overrides["IamBootstrap:EnvFilePath"] = envFile;
        return overrides;
    }

    /// <summary>The connection string from the environment, or the one <c>scripts/dev-secrets.sh</c> builds from <c>.env</c>.</summary>
    private static string ConnectionString(Dictionary<string, string> dotEnv, string root)
    {
        var fromEnvironment = Environment.GetEnvironmentVariable(ConnectionStringVariable);
        if (!string.IsNullOrEmpty(fromEnvironment))
        {
            return fromEnvironment;
        }

        var password = dotEnv.GetValueOrDefault("MSSQL_SA_PASSWORD");
        string.IsNullOrEmpty(password).ShouldBeFalse($"Set {ConnectionStringVariable} or MSSQL_SA_PASSWORD in .env.");
        return new SqlConnectionStringBuilder
        {
            DataSource = "localhost,1433",
            InitialCatalog = "RegReturns",
            UserID = "sa",
            Password = password,
            Encrypt = SqlConnectionEncryptOption.Strict,
            ServerCertificate = Path.Combine(root, ".certs", "sqlserver", "mssql.crt"),
        }.ConnectionString;
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
