using RegReturns.ServiceDefaults.Logging;

using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace RegReturns.UnitTests.ServiceDefaults;

public sealed class SensitiveDataRedactionTests
{
    [Fact]
    public void Scalar_properties_with_sensitive_names_are_masked()
    {
        var events = Capture(log => log.Information(
            "Login for {UserName} with {Password} and {access_token} from {Email}", "maker.hlb", "hunter2", "eyJ...", "m@x.example"));

        var e = events.Single();
        e.Properties["UserName"].ToString().ShouldBe("\"maker.hlb\"");
        e.Properties["Password"].ToString().ShouldContain(SensitiveData.Mask);
        e.Properties["access_token"].ToString().ShouldContain(SensitiveData.Mask);
        e.Properties["Email"].ToString().ShouldContain(SensitiveData.Mask);
    }

    [Theory]
    [InlineData("client_secret")]
    [InlineData("id_token_hint")]
    [InlineData("logout_token")]
    [InlineData("code")]
    [InlineData("client-assertion")]
    [InlineData("PrivateKey")]
    [InlineData("HmacKey")]
    [InlineData("Credentials")]
    [InlineData("TotpSecret")]
    [InlineData("ConnectionString")]
    public void OAuth_parameters_keys_and_credentials_are_masked(string name)
    {
        SensitiveData.IsSensitiveName(name).ShouldBeTrue();
    }

    [Theory]
    [InlineData("ErrorCode")]
    [InlineData("StatusCode")]
    [InlineData("SubjectId")]
    [InlineData("AuditSequence")]
    [InlineData("ClientId")]
    public void Codes_ids_and_counters_stay_readable(string name)
    {
        SensitiveData.IsSensitiveName(name).ShouldBeFalse();
    }

    [Fact]
    public void Destructured_objects_mask_sensitive_and_attributed_properties()
    {
        var events = Capture(log => log.Information("Created {@User}", new SampleUser("maker.hlb", "m@x.example", "123-45")));

        var rendered = events.Single().Properties["User"].ToString();
        rendered.ShouldContain("maker.hlb");
        rendered.ShouldNotContain("m@x.example");
        rendered.ShouldNotContain("123-45");
    }

    private static List<LogEvent> Capture(Action<ILogger> write)
    {
        var sink = new ListSink();
        using var logger = new LoggerConfiguration()
            .Enrich.With<SensitiveDataEnricher>()
            .Destructure.With<SensitiveDataDestructuringPolicy>()
            .WriteTo.Sink(sink)
            .CreateLogger();
        write(logger);
        return sink.Events;
    }

    private sealed record SampleUser(string UserName, string Email, [property: PersonalData] string NationalId);

    [AttributeUsage(AttributeTargets.Property)]
    private sealed class PersonalDataAttribute : Attribute;

    private sealed class ListSink : ILogEventSink
    {
        public List<LogEvent> Events { get; } = [];

        public void Emit(LogEvent logEvent) => Events.Add(logEvent);
    }
}
