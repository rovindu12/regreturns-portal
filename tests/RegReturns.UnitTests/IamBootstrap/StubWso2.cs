extern alias IamBootstrapTool;

using System.Net;
using System.Net.Http.Headers;
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

    /// <summary>A factory whose clients all reach this stub (callers set their own base address).</summary>
    public IHttpClientFactory Factory() => new StubFactory(this);

    public IEnumerable<RecordedRequest> Writes() => Requests.Where(r => r.Method != HttpMethod.Get);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        var mediaType = request.Content?.Headers.ContentType?.MediaType;
        var isJson = mediaType?.EndsWith("json", StringComparison.OrdinalIgnoreCase) == true;
        var pathAndQuery = request.RequestUri!.PathAndQuery.TrimStart('/');
        var path = pathAndQuery.Split('?', 2)[0];
        Requests.Add(new RecordedRequest(
            request.Method,
            pathAndQuery,
            body is null || !isJson ? null : JsonNode.Parse(body),
            mediaType,
            string.Join(',', request.Headers.Accept.Select(a => a.MediaType)),
            body,
            request.Headers.Authorization));

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

    private sealed class StubFactory(StubWso2 stub) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(stub, disposeHandler: false);
    }
}

/// <summary>A request the stub received; <see cref="Body"/> is set for JSON content only, <see cref="Text"/> always.</summary>
internal sealed record RecordedRequest(
    HttpMethod Method, string PathAndQuery, JsonNode? Body, string? ContentType, string Accept, string? Text, AuthenticationHeaderValue? Authorization);
