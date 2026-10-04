using System.Net;
using System.Text.Json.Nodes;

namespace RegReturns.IamBootstrap.Wso2;

/// <summary>The outcome of one WSO2 management call.</summary>
/// <param name="Method">The HTTP method, for error messages.</param>
/// <param name="Path">The request path without query string, for error messages.</param>
/// <param name="StatusCode">The HTTP status.</param>
/// <param name="Body">The parsed JSON body, if there was one.</param>
/// <param name="Location">The <c>Location</c> header, if any (WSO2 returns new ids only there for some resources).</param>
internal sealed record Wso2Response(string Method, string Path, HttpStatusCode StatusCode, JsonNode? Body, Uri? Location)
{
    /// <summary>Gets a value indicating whether the status is 2xx.</summary>
    public bool IsSuccess => (int)StatusCode is >= 200 and < 300;

    /// <summary>Gets the WSO2 error code (<c>APP-60007</c>, <c>CMT-50039</c>, ...) or the SCIM status, if the body has one.</summary>
    public string? ErrorCode => Body is JsonObject obj
        ? obj["code"]?.ToString() ?? obj["scimType"]?.ToString()
        : null;

    /// <summary>Gets the human-readable error description, if any. WSO2 masks user names in SCIM errors.</summary>
    public string? ErrorDescription => Body is JsonObject obj
        ? obj["description"]?.ToString() ?? obj["detail"]?.ToString() ?? obj["message"]?.ToString()
        : null;

    /// <summary>Gets the last path segment of <see cref="Location"/> (the new resource id).</summary>
    public string? LocationId => Location?.Segments.LastOrDefault()?.TrimEnd('/');

    /// <summary>Throws unless the status is 2xx or one of <paramref name="alsoAccepted"/>.</summary>
    /// <param name="alsoAccepted">Non-success statuses the caller handles itself.</param>
    /// <returns>The same response.</returns>
    public Wso2Response EnsureSuccess(params HttpStatusCode[] alsoAccepted)
    {
        if (IsSuccess || alsoAccepted.Contains(StatusCode))
        {
            return this;
        }

        throw new Wso2ApiException(this);
    }
}

/// <summary>A WSO2 management call failed unexpectedly. The message never contains request bodies or secrets.</summary>
public sealed class Wso2ApiException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="Wso2ApiException"/> class.</summary>
    /// <param name="response">The failed response.</param>
    internal Wso2ApiException(Wso2Response response)
        : base($"WSO2 {response.Method} {response.Path} returned {(int)response.StatusCode} {response.ErrorCode}: {response.ErrorDescription}")
    {
        StatusCode = response.StatusCode;
        ErrorCode = response.ErrorCode;
    }

    /// <summary>Initializes a new instance of the <see cref="Wso2ApiException"/> class.</summary>
    public Wso2ApiException()
        : base("WSO2 management call failed.")
    {
    }

    /// <summary>Initializes a new instance of the <see cref="Wso2ApiException"/> class.</summary>
    /// <param name="message">The message.</param>
    public Wso2ApiException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="Wso2ApiException"/> class.</summary>
    /// <param name="message">The message.</param>
    /// <param name="innerException">The cause.</param>
    public Wso2ApiException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Gets the HTTP status of the failed call, if any.</summary>
    public HttpStatusCode? StatusCode { get; }

    /// <summary>Gets the WSO2 error code of the failed call, if any.</summary>
    public string? ErrorCode { get; }
}
