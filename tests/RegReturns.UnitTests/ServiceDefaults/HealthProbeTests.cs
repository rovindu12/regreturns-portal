using RegReturns.ServiceDefaults.Web;

namespace RegReturns.UnitTests.ServiceDefaults;

public sealed class HealthProbeTests
{
    [Theory]
    [InlineData(new[] { "--health-probe" }, true)]
    [InlineData(new[] { "--health-probe", "/health/ready" }, true)]
    [InlineData(new string[0], false)]
    [InlineData(new[] { "--urls", "--health-probe" }, false)]
    public void Only_a_first_argument_of_health_probe_asks_for_a_probe(string[] args, bool requested)
    {
        HealthProbe.IsRequested(args).ShouldBe(requested);
    }

    [Theory]
    [InlineData(null, null, "http://127.0.0.1:8080/health/live")]
    [InlineData("", "/health/ready", "http://127.0.0.1:8080/health/ready")]
    [InlineData("5000", null, "http://127.0.0.1:5000/health/live")]
    [InlineData("8081;8082", "/health/ready", "http://127.0.0.1:8081/health/ready")]
    [InlineData("not-a-port", null, "http://127.0.0.1:8080/health/live")]
    [InlineData("70000", null, "http://127.0.0.1:8080/health/live")]
    public void The_probe_calls_the_first_http_port_over_loopback(string? ports, string? path, string expected)
    {
        HealthProbe.Target(ports, path).ShouldBe(new Uri(expected));
    }

    [Theory]
    [InlineData("health/ready")]
    [InlineData("//evil.example/health")]
    [InlineData("http://evil.example/")]
    public void The_path_must_be_a_path_on_the_app_itself(string path)
    {
        Should.Throw<ArgumentException>(() => HealthProbe.Target("8080", path));
    }
}
