namespace RegReturns.IamBootstrap;

/// <summary>Reads and writes simple <c>KEY=value</c> env files (the format docker compose reads).</summary>
internal static class DotEnvFile
{
    /// <summary>Environment-style names in <c>.env</c> and the configuration keys they feed.</summary>
    public static readonly IReadOnlyDictionary<string, string> ConfigurationKeys = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["WSO2_ADMIN_USERNAME"] = $"{BootstrapOptions.SectionName}:{nameof(BootstrapOptions.AdminUserName)}",
        ["WSO2_ADMIN_PASSWORD"] = $"{BootstrapOptions.SectionName}:{nameof(BootstrapOptions.AdminPassword)}",
        ["DEMO_USER_PASSWORD"] = $"{BootstrapOptions.SectionName}:{nameof(BootstrapOptions.DemoUserPassword)}",
    };

    /// <summary>Parses an env file. Blank lines and <c>#</c> comments are skipped; surrounding quotes are removed.</summary>
    /// <param name="path">The file path.</param>
    /// <returns>The values, or an empty map if the file does not exist.</returns>
    public static Dictionary<string, string> Read(string path)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!File.Exists(path))
        {
            return values;
        }

        foreach (var raw in File.ReadAllLines(path))
        {
            var line = raw.Trim();
            var equals = line.IndexOf('=', StringComparison.Ordinal);
            if (line.Length == 0 || line[0] == '#' || equals <= 0)
            {
                continue;
            }

            var value = line[(equals + 1)..].Trim();
            if (value.Length >= 2 && (value[0] == '"' || value[0] == '\'') && value[^1] == value[0])
            {
                value = value[1..^1];
            }

            values[line[..equals].Trim()] = value;
        }

        return values;
    }

    /// <summary>
    /// Maps the known <c>.env</c> names to configuration keys, skipping any already set in the process environment
    /// (real environment variables win over the file).
    /// </summary>
    /// <param name="values">The parsed file.</param>
    /// <returns>Configuration entries.</returns>
    public static Dictionary<string, string?> ToConfiguration(IReadOnlyDictionary<string, string> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        var config = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, key) in ConfigurationKeys)
        {
            var fromEnvironment = Environment.GetEnvironmentVariable(name);
            if (!string.IsNullOrEmpty(fromEnvironment))
            {
                config[key] = fromEnvironment;
            }
            else if (values.TryGetValue(name, out var value))
            {
                config[key] = value;
            }
        }

        var host = Environment.GetEnvironmentVariable("WSO2_HOSTNAME") ?? values.GetValueOrDefault("WSO2_HOSTNAME");
        if (!string.IsNullOrWhiteSpace(host))
        {
            config["Wso2:Authority"] = $"https://{host}:9443/";
        }

        return config;
    }

    /// <summary>
    /// Writes values into an env file, keeping keys it does not set, readable only by the owner. Values are never logged.
    /// </summary>
    /// <param name="path">The file path.</param>
    /// <param name="values">The values to set.</param>
    /// <param name="header">A comment written at the top.</param>
    public static void Write(string path, IReadOnlyDictionary<string, string> values, string header)
    {
        ArgumentNullException.ThrowIfNull(values);
        var merged = new SortedDictionary<string, string>(Read(path), StringComparer.Ordinal);
        foreach (var (key, value) in values)
        {
            merged[key] = value;
        }

        var lines = new List<string> { $"# {header}" };
        lines.AddRange(merged.Select(p => $"{p.Key}={p.Value}"));

        var temp = path + ".tmp";
        File.WriteAllLines(temp, lines);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(temp, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }

        File.Move(temp, path, overwrite: true);
    }
}
