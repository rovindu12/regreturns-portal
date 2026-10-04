extern alias IamBootstrapTool;

using System.Net;
using System.Text;
using System.Text.Json.Nodes;

using IamBootstrapTool::RegReturns.IamBootstrap.Wso2;

using Microsoft.Extensions.Logging.Abstractions;

namespace RegReturns.UnitTests.IamBootstrap;

/// <summary>
/// A scripted WSO2: answers requests from a list of routes and records every request with its body.
/// Routes match on method and path (and on the query too when the route includes one); a route added with
/// <c>once: true</c> is used for one request only. An unexpected request fails the test.
/// </summary>
internal sealed class StubWso2 : HttpMessageHandler
{
    public static readonly Uri Authority = new("https://iam.valoria.test/");

    private readonly List<Route> _routes = [];

    public List<RecordedRequest> Requests { get; } = [];

    public StubWso2 On(HttpMethod method, string path, HttpStatusCode status, JsonNode? body = null, string? location = null, bool once = false)
    {
        _routes.Add(new Route(method, path, status, body?.ToJsonString(), location, once));
        return this;
    }

    public StubWso2 OnJson(HttpMethod method, string path, string json, bool once = false) =>
        On(method, path, HttpStatusCode.OK, JsonNode.Parse(json), once: once);

    public Wso2AdminClient Client() =>
        new(new HttpClient(this, disposeHandler: false) { BaseAddress = Authority }, NullLogger<Wso2AdminClient>.Instance);

    public IEnumerable<RecordedRequest> Writes() => Requests.Where(r => r.Method != HttpMethod.Get);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        var pathAndQuery = request.RequestUri!.PathAndQuery.TrimStart('/');
        var path = pathAndQuery.Split('?', 2)[0];
        Requests.Add(new RecordedRequest(
            request.Method,
            pathAndQuery,
            body is null ? null : JsonNode.Parse(body),
            request.Content?.Headers.ContentType?.MediaType,
            string.Join(',', request.Headers.Accept.Select(a => a.MediaType))));

        var route = _routes.FirstOrDefault(r => r.Method == request.Method && (r.Path.Contains('?', StringComparison.Ordinal) ? r.Path == pathAndQuery : r.Path == path))
            ?? throw new InvalidOperationException($"Unexpected WSO2 call: {request.Method} {pathAndQuery}");
        if (route.Once)
        {
            _routes.Remove(route);
        }

        var response = new HttpResponseMessage(route.Status);
        if (route.Json is not null)
        {
            response.Content = new StringContent(route.Json, Encoding.UTF8, "application/json");
        }

        if (route.Location is not null)
        {
            response.Headers.Location = new Uri(route.Location, UriKind.RelativeOrAbsolute);
        }

        return response;
    }

    private sealed record Route(HttpMethod Method, string Path, HttpStatusCode Status, string? Json, string? Location, bool Once);
}

/// <summary>A request the stub received.</summary>
internal sealed record RecordedRequest(HttpMethod Method, string PathAndQuery, JsonNode? Body, string? ContentType, string Accept);
