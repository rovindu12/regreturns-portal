using System.CommandLine;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.EnvironmentVariables;
using Microsoft.Extensions.Configuration.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using RegReturns.IamBootstrap.Steps;
using RegReturns.IamBootstrap.Wso2;
using RegReturns.Infrastructure;
using RegReturns.Infrastructure.Identity.Wso2;
using RegReturns.ServiceDefaults;

namespace RegReturns.IamBootstrap;

/// <summary>Command-line commands of the IAM setup tool.</summary>
internal static partial class BootstrapCommands
{
    private const int Success = 0;
    private const int Failure = 1;

    /// <summary><c>apply</c>: brings every RegReturns object in WSO2 to the desired state.</summary>
    /// <returns>The command.</returns>
    public static Command Apply()
    {
        var envFile = EnvFileOption();
        var command = new Command("apply", "Create or update all RegReturns objects in WSO2 (idempotent).") { envFile };
        command.SetAction((parse, ct) => RunAsync([], parse.GetValue(envFile), resetDemoUsers: false, ct));
        return command;
    }

    /// <summary><c>demo-users</c>: resets demo users (password, roles, institution, TOTP) only.</summary>
    /// <returns>The command.</returns>
    public static Command DemoUsers()
    {
        var envFile = EnvFileOption();
        var command = new Command("demo-users", "Reset the demo users' passwords, roles, institutions and TOTP secret.") { envFile };
        command.SetAction((parse, ct) => RunAsync([DemoUsersStep.StepName, DemoTotpStep.StepName], parse.GetValue(envFile), resetDemoUsers: true, ct));
        return command;
    }

    /// <summary>Builds the service provider used by the commands; internal so tests can run the tool in-process.</summary>
    /// <param name="overrides">Extra configuration (highest priority), for tests.</param>
    /// <returns>The host.</returns>
    internal static IHost BuildHost(IDictionary<string, string?>? overrides = null)
    {
        // Content root is the tool's own folder so its appsettings.json loads whatever the working directory is.
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { ContentRootPath = AppContext.BaseDirectory });

        InsertDotEnv(
            builder.Configuration.Sources,
            new MemoryConfigurationSource { InitialData = DotEnvFile.ToConfiguration(DotEnvFile.Read(".env")) });
        if (overrides is not null)
        {
            builder.Configuration.AddInMemoryCollection(overrides);
        }

        builder.AddObservability("regreturns-iam-bootstrap");
        builder.Services.AddInfrastructure(builder.Configuration);
        builder.Services.AddWso2Backchannel(builder.Configuration);
        builder.Services.AddOptions<BootstrapOptions>()
            .Bind(builder.Configuration.GetSection(BootstrapOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        builder.Services.AddHttpClient<Wso2AdminClient>((sp, client) =>
            {
                var settings = sp.GetRequiredService<IOptions<BootstrapOptions>>().Value;
                client.BaseAddress = sp.GetRequiredService<IOptions<Wso2Options>>().Value.Authority;
                client.Timeout = TimeSpan.FromSeconds(60);
                client.DefaultRequestHeaders.Authorization =
                    Wso2AdminClient.BasicCredentials(settings.AdminUserName!, settings.AdminPassword!);
            })
            .ConfigurePrimaryHttpMessageHandler(sp =>
                Wso2Backchannel.CreatePrimaryHandler(sp.GetRequiredService<IOptions<Wso2Options>>().Value))
            .AddHttpMessageHandler(sp =>
            {
                var options = sp.GetRequiredService<IOptions<Wso2Options>>().Value;
                return new Wso2BackchannelRewriteHandler(options.Authority!, options.EffectiveBackchannelAuthority);
            });

        builder.Services.AddScoped<Wso2Applications>();
        builder.Services.AddScoped<Wso2Scim>();
        builder.Services.AddScoped<BootstrapRunner>();

        // Order matters: each step relies on objects created by the ones before it.
        builder.Services.AddScoped<IBootstrapStep, ClaimsStep>();
        builder.Services.AddScoped<IBootstrapStep, ApiResourceStep>();
        builder.Services.AddScoped<IBootstrapStep, PortalAppStep>();
        builder.Services.AddScoped<IBootstrapStep, RolesStep>();
        builder.Services.AddScoped<IBootstrapStep, ApiClientsStep>();
        builder.Services.AddScoped<IBootstrapStep, ProvisionerStep>();
        builder.Services.AddScoped<IBootstrapStep, SelfServiceStep>();
        builder.Services.AddScoped<IBootstrapStep, AccountLockStep>();
        builder.Services.AddScoped<IBootstrapStep, DemoUsersStep>();
        builder.Services.AddScoped<IBootstrapStep, DemoTotpStep>();

        return builder.Build();
    }

    /// <summary>
    /// Inserts the values from <c>./.env</c> just before the application's (unprefixed) environment variables, so they
    /// override appsettings.json and user-secrets but not real environment variables or the command line. The
    /// <c>DOTNET_</c>-prefixed host source comes first in the list and must not be the anchor.
    /// </summary>
    /// <param name="sources">The configuration sources, in priority order (last wins).</param>
    /// <param name="dotEnv">The source holding the <c>.env</c> values.</param>
    internal static void InsertDotEnv(IList<IConfigurationSource> sources, IConfigurationSource dotEnv)
    {
        ArgumentNullException.ThrowIfNull(sources);
        var environment = sources.ToList().FindLastIndex(s => s is EnvironmentVariablesConfigurationSource { Prefix: null or "" });
        sources.Insert(environment < 0 ? sources.Count : environment, dotEnv);
    }

    private static Option<string?> EnvFileOption() => new("--env-file")
    {
        Description = "Where to write generated client secrets (default: IamBootstrap:EnvFilePath, .env.generated).",
    };

    private static async Task<int> RunAsync(
        IReadOnlyCollection<string> only, string? envFile, bool resetDemoUsers, CancellationToken cancellationToken)
    {
        using var host = BuildHost();
        var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("RegReturns.IamBootstrap");
        try
        {
            await using var scope = host.Services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<BootstrapRunner>().RunAsync(only, envFile, resetDemoUsers, cancellationToken);
            return Success;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogFailed(logger, ex);
            return Failure;
        }
    }

    [LoggerMessage(EventId = 4002, Level = LogLevel.Critical, Message = "IAM setup failed")]
    private static partial void LogFailed(ILogger logger, Exception exception);
}
