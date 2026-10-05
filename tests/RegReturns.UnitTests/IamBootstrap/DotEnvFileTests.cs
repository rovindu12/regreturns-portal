extern alias IamBootstrapTool;

using System.Runtime.Versioning;

using IamBootstrapTool::RegReturns.IamBootstrap;

namespace RegReturns.UnitTests.IamBootstrap;

public sealed class DotEnvFileTests : IDisposable
{
    private static readonly string[] KnownNames = ["WSO2_ADMIN_USERNAME", "WSO2_ADMIN_PASSWORD", "DEMO_USER_PASSWORD", "WSO2_HOSTNAME"];

    private readonly string _directory = Directory.CreateTempSubdirectory("regreturns-dotenv-").FullName;
    private readonly EnvironmentScope _environment = new(KnownNames);

    private string EnvPath => Path.Combine(_directory, ".env");

    [Fact]
    public void Read_returns_nothing_for_a_missing_file()
    {
        DotEnvFile.Read(Path.Combine(_directory, "missing.env")).ShouldBeEmpty();
    }

    [Fact]
    public void Read_parses_key_value_lines()
    {
        var values = ReadLines("WSO2_HOSTNAME=localhost", "WSO2_ADMIN_USERNAME=iamadmin");

        values.ShouldBe(new Dictionary<string, string> { ["WSO2_HOSTNAME"] = "localhost", ["WSO2_ADMIN_USERNAME"] = "iamadmin" });
    }

    [Fact]
    public void Read_skips_comments_and_blank_lines()
    {
        var values = ReadLines("# a comment", string.Empty, "   ", "  # indented comment", "KEY=value");

        values.Keys.ShouldBe(["KEY"]);
    }

    [Theory]
    [InlineData("KEY=\"quoted value\"", "quoted value")]
    [InlineData("KEY='single quoted'", "single quoted")]
    [InlineData("KEY=\"\"", "")]
    public void Read_removes_matching_surrounding_quotes(string line, string expected)
    {
        ReadLines(line)["KEY"].ShouldBe(expected);
    }

    [Theory]
    [InlineData("KEY=\"unterminated", "\"unterminated")]
    [InlineData("KEY='mixed\"", "'mixed\"")]
    [InlineData("KEY=\"", "\"")]
    [InlineData("KEY=in\"side\"", "in\"side\"")]
    public void Read_keeps_quotes_that_do_not_surround_the_value(string line, string expected)
    {
        ReadLines(line)["KEY"].ShouldBe(expected);
    }

    [Fact]
    public void Read_keeps_equals_signs_inside_the_value()
    {
        ReadLines("AUDIT_HMAC_KEY=dGVzdA==")["AUDIT_HMAC_KEY"].ShouldBe("dGVzdA==");
    }

    [Fact]
    public void Read_trims_spaces_around_keys_and_values()
    {
        ReadLines("  KEY  =  value  ")["KEY"].ShouldBe("value");
    }

    [Fact]
    public void Read_skips_lines_without_a_key()
    {
        ReadLines("=orphan", "no-equals-sign", "KEY=value").Keys.ShouldBe(["KEY"]);
    }

    [Fact]
    public void Read_keeps_the_last_value_of_a_repeated_key()
    {
        ReadLines("KEY=first", "KEY=second")["KEY"].ShouldBe("second");
    }

    [Fact]
    public void Read_allows_an_empty_value()
    {
        ReadLines("KEY=")["KEY"].ShouldBeEmpty();
    }

    [Fact]
    public void ToConfiguration_maps_the_known_names_to_bootstrap_settings()
    {
        var config = DotEnvFile.ToConfiguration(new Dictionary<string, string>
        {
            ["WSO2_ADMIN_USERNAME"] = "iamadmin",
            ["WSO2_ADMIN_PASSWORD"] = "admin-secret",
            ["DEMO_USER_PASSWORD"] = "demo-secret-123",
        });

        config.ShouldBe(new Dictionary<string, string?>
        {
            ["IamBootstrap:AdminUserName"] = "iamadmin",
            ["IamBootstrap:AdminPassword"] = "admin-secret",
            ["IamBootstrap:DemoUserPassword"] = "demo-secret-123",
        });
    }

    [Fact]
    public void ToConfiguration_ignores_names_the_tool_does_not_use()
    {
        var config = DotEnvFile.ToConfiguration(new Dictionary<string, string>
        {
            ["MSSQL_SA_PASSWORD"] = "sa-secret",
            ["AUDIT_HMAC_KEY"] = "key",
        });

        config.ShouldBeEmpty();
    }

    [Fact]
    public void ToConfiguration_turns_the_wso2_host_name_into_the_authority()
    {
        var config = DotEnvFile.ToConfiguration(new Dictionary<string, string> { ["WSO2_HOSTNAME"] = "iam.valoria.test" });

        config["Wso2:Authority"].ShouldBe("https://iam.valoria.test:9443/");
    }

    [Fact]
    public void ToConfiguration_ignores_a_blank_host_name()
    {
        DotEnvFile.ToConfiguration(new Dictionary<string, string> { ["WSO2_HOSTNAME"] = " " }).ShouldNotContainKey("Wso2:Authority");
    }

    [Fact]
    public void ToConfiguration_prefers_real_environment_variables_over_the_file()
    {
        _environment.Set("WSO2_ADMIN_PASSWORD", "from-environment");

        var config = DotEnvFile.ToConfiguration(new Dictionary<string, string> { ["WSO2_ADMIN_PASSWORD"] = "from-file" });

        config["IamBootstrap:AdminPassword"].ShouldBe("from-environment");
    }

    [Fact]
    public void ToConfiguration_uses_environment_variables_missing_from_the_file()
    {
        _environment.Set("DEMO_USER_PASSWORD", "from-environment");

        DotEnvFile.ToConfiguration(new Dictionary<string, string>())["IamBootstrap:DemoUserPassword"].ShouldBe("from-environment");
    }

    [Fact]
    public void ToConfiguration_prefers_the_host_name_from_the_environment()
    {
        _environment.Set("WSO2_HOSTNAME", "wso2");

        var config = DotEnvFile.ToConfiguration(new Dictionary<string, string> { ["WSO2_HOSTNAME"] = "localhost" });

        config["Wso2:Authority"].ShouldBe("https://wso2:9443/");
    }

    [Fact]
    public void ToConfiguration_falls_back_to_the_file_when_an_environment_variable_is_empty()
    {
        _environment.Set("WSO2_ADMIN_USERNAME", string.Empty);

        var config = DotEnvFile.ToConfiguration(new Dictionary<string, string> { ["WSO2_ADMIN_USERNAME"] = "iamadmin" });

        config["IamBootstrap:AdminUserName"].ShouldBe("iamadmin");
    }

    [Fact]
    public void ToConfiguration_falls_back_to_the_file_host_name_when_the_environment_one_is_empty()
    {
        _environment.Set("WSO2_HOSTNAME", string.Empty);

        var config = DotEnvFile.ToConfiguration(new Dictionary<string, string> { ["WSO2_HOSTNAME"] = "localhost" });

        config["Wso2:Authority"].ShouldBe("https://localhost:9443/");
    }

    [Fact]
    public void Write_creates_the_file_with_a_header_and_sorted_keys()
    {
        DotEnvFile.Write(EnvPath, new Dictionary<string, string> { ["B_KEY"] = "2", ["A_KEY"] = "1" }, "Generated for a test.");

        File.ReadAllLines(EnvPath).ShouldBe(["# Generated for a test.", "A_KEY=1", "B_KEY=2"]);
    }

    [Fact]
    public void Write_keeps_existing_keys_it_does_not_set()
    {
        File.WriteAllLines(EnvPath, ["KEEP=me", "REPLACE=old"]);

        DotEnvFile.Write(EnvPath, new Dictionary<string, string> { ["REPLACE"] = "new", ["ADD"] = "added" }, "header");

        DotEnvFile.Read(EnvPath).ShouldBe(new Dictionary<string, string> { ["ADD"] = "added", ["KEEP"] = "me", ["REPLACE"] = "new" });
    }

    [Fact]
    public void Written_values_read_back_unchanged()
    {
        var values = new Dictionary<string, string> { ["Oidc__ClientId"] = "regreturns-portal", ["Oidc__ClientSecret"] = "a-b_c=d" };

        DotEnvFile.Write(EnvPath, values, "header");

        DotEnvFile.Read(EnvPath).ShouldBe(values);
    }

    [Fact]
    [UnsupportedOSPlatform("windows")]
    public void Write_makes_the_file_readable_by_the_owner_only()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Unix file modes only.");

        DotEnvFile.Write(EnvPath, new Dictionary<string, string> { ["SECRET"] = "x" }, "header");

        File.GetUnixFileMode(EnvPath).ShouldBe(UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    [Fact]
    [UnsupportedOSPlatform("windows")]
    public void Write_restricts_an_existing_world_readable_file()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Unix file modes only.");
        File.WriteAllText(EnvPath, "OLD=1\n");
        File.SetUnixFileMode(EnvPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead);

        DotEnvFile.Write(EnvPath, new Dictionary<string, string> { ["SECRET"] = "x" }, "header");

        File.GetUnixFileMode(EnvPath).ShouldBe(UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    [Fact]
    public void Write_leaves_no_temporary_file_behind()
    {
        DotEnvFile.Write(EnvPath, new Dictionary<string, string> { ["KEY"] = "value" }, "header");

        Directory.GetFiles(_directory).Select(Path.GetFileName).ShouldBe([".env"]);
    }

    [Fact]
    [UnsupportedOSPlatform("windows")]
    public void Write_replaces_the_file_instead_of_rewriting_it_in_place()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Replacing an open file is a Unix rename.");
        File.WriteAllText(EnvPath, "OLD=1\n");
        using var reader = new StreamReader(EnvPath);

        DotEnvFile.Write(EnvPath, new Dictionary<string, string> { ["NEW"] = "2" }, "header");

        // A reader that opened the old file still sees it whole: the new content arrived by an atomic rename.
        reader.ReadToEnd().ShouldBe("OLD=1\n");
        File.ReadAllText(EnvPath).ShouldContain("NEW=2");
    }

    public void Dispose()
    {
        _environment.Dispose();
        Directory.Delete(_directory, recursive: true);
    }

    private Dictionary<string, string> ReadLines(params string[] lines)
    {
        File.WriteAllLines(EnvPath, lines);
        return DotEnvFile.Read(EnvPath);
    }

    /// <summary>Clears the given environment variables for a test and restores them afterwards.</summary>
    private sealed class EnvironmentScope : IDisposable
    {
        private readonly Dictionary<string, string?> _saved;

        public EnvironmentScope(IEnumerable<string> names)
        {
            _saved = names.ToDictionary(n => n, Environment.GetEnvironmentVariable);
            foreach (var name in _saved.Keys)
            {
                Environment.SetEnvironmentVariable(name, null);
            }
        }

        public void Set(string name, string value)
        {
            if (!_saved.ContainsKey(name))
            {
                throw new ArgumentException($"{name} is not restored by this scope.", nameof(name));
            }

            Environment.SetEnvironmentVariable(name, value);
        }

        public void Dispose()
        {
            foreach (var (name, value) in _saved)
            {
                Environment.SetEnvironmentVariable(name, value);
            }
        }
    }
}
