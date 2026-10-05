using System.Globalization;
using System.Net.Mime;

using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;

using RegReturns.Api.Authentication;
using RegReturns.Api.Hosting;
using RegReturns.Api.Problems;
using RegReturns.Application.Idempotency;
using RegReturns.Domain.Common;

namespace RegReturns.Api.Idempotency;

/// <summary>
/// Runs an <see cref="IdempotentAttribute"/> action at most once per client and key (ADR 0027). It reads the body once
/// to fingerprint the request, claims the key, and then either replays the stored response (<c>Idempotent-Replayed:
/// true</c>), refuses a key reused for another request (422) or one still running (409 with <c>Retry-After</c>), or
/// runs the action with its response buffered and stores it. Responses that ask for a retry (409, 429 and 5xx) and
/// failed requests are not stored: the key is released, so the retry runs again.
/// </summary>
/// <param name="store">The idempotency records.</param>
/// <param name="problems">The framework's problem factory.</param>
/// <param name="logger">The logger.</param>
internal sealed partial class IdempotencyFilter(
    IIdempotencyStore store, ProblemDetailsFactory problems, ILogger<IdempotencyFilter> logger) : IAsyncResourceFilter
{
    /// <summary>The largest body an idempotent request may have.</summary>
    public const int MaxBodyBytes = 1024 * 1024;

    /// <summary>How long a client should wait before retrying a request that is still running.</summary>
    public static readonly TimeSpan RetryAfter = TimeSpan.FromSeconds(2);

    /// <summary>The error for a body over <see cref="MaxBodyBytes"/>.</summary>
    public static readonly Error BodyTooLarge = new(
        "Request.TooLarge", $"The request body is larger than {MaxBodyBytes / 1024} KB.");

    /// <inheritdoc />
    public async Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);
        var http = context.HttpContext;
        var keyError = ReadKey(http.Request, out var key);
        if (keyError is not null)
        {
            context.Result = ApiProblems.Create(problems, http, keyError);
            return;
        }

        // The action's policy requires a registered client, so a principal without one never gets here.
        var clientId = InstitutionClaimsTransformation.ClientIdOf(http.User)
            ?? throw new InvalidOperationException("An idempotent action ran for a caller without a client id.");
        var body = await ReadBodyAsync(http.Request, http.RequestAborted);
        if (body is null)
        {
            context.Result = Problem(http, BodyTooLarge, StatusCodes.Status413PayloadTooLarge);
            return;
        }

        var pathAndQuery = string.Concat(
            http.Request.PathBase.ToUriComponent(), http.Request.Path.ToUriComponent(), http.Request.QueryString.ToUriComponent());
        var fingerprint = RequestFingerprint.Compute(http.Request.Method, pathAndQuery, body);
        var start = await store.BeginAsync(new IdempotentRequest(clientId, key!, fingerprint), http.RequestAborted);
        Count(start.Outcome);
        switch (start.Outcome)
        {
            case IdempotencyOutcome.Replay:
                LogReplayed(logger, clientId, key!, start.Response!.StatusCode);
                context.Result = new StoredResponseResult(start.Response);
                return;
            case IdempotencyOutcome.KeyReused:
                LogKeyReused(logger, clientId, key!);
                context.Result = ApiProblems.Create(problems, http, IdempotencyErrors.KeyReused);
                return;
            case IdempotencyOutcome.InProgress:
                LogInProgress(logger, clientId, key!);
                http.Response.Headers.RetryAfter = ((int)RetryAfter.TotalSeconds).ToString(CultureInfo.InvariantCulture);
                context.Result = ApiProblems.Create(problems, http, IdempotencyErrors.InProgress);
                return;
            default:
                await RunAndStoreAsync(context, next, start.RecordId!.Value);
                return;
        }
    }

    /// <summary>
    /// Returns whether a response is kept for replay. Answers that depend on the moment rather than the request are not:
    /// 403 (the client's set-up, which an administrator can fix), 409 (busy or a concurrent change), 429 and 5xx.
    /// </summary>
    /// <param name="statusCode">The response status.</param>
    /// <returns><see langword="true"/> if a retry with the same key should get this response again.</returns>
    internal static bool IsStored(int statusCode) =>
        statusCode < StatusCodes.Status500InternalServerError
        && statusCode is not (StatusCodes.Status403Forbidden or StatusCodes.Status409Conflict or StatusCodes.Status429TooManyRequests);

    private static Error? ReadKey(HttpRequest request, out string? key)
    {
        key = null;
        if (!request.Headers.TryGetValue(IdempotentAttribute.KeyHeader, out var values) || values.Count == 0)
        {
            return IdempotencyErrors.KeyRequired;
        }

        if (values.Count > 1 || !IdempotencyErrors.IsValidKey(values[0]))
        {
            return IdempotencyErrors.KeyInvalid;
        }

        key = values[0];
        return null;
    }

    /// <summary>
    /// Reads the whole body (at most <see cref="MaxBodyBytes"/>) and puts it back for model binding. Returns
    /// <see langword="null"/> for a larger body, whether the declared length, this reader or Kestrel's request size
    /// limit catches it.
    /// </summary>
    private static async Task<byte[]?> ReadBodyAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        if (request.ContentLength > MaxBodyBytes)
        {
            return null;
        }

        using var copy = new MemoryStream();
        var buffer = new byte[16 * 1024];
        try
        {
            int read;
            while ((read = await request.Body.ReadAsync(buffer, cancellationToken)) > 0)
            {
                if (copy.Length + read > MaxBodyBytes)
                {
                    return null;
                }

                await copy.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            }
        }
        catch (BadHttpRequestException exception) when (exception.StatusCode == StatusCodes.Status413PayloadTooLarge)
        {
            return null;
        }

        var body = copy.ToArray();
        request.Body = new MemoryStream(body, writable: false);
        return body;
    }

    private static void Count(IdempotencyOutcome outcome) =>
        ApiTelemetry.IdempotentRequests.Add(1, new KeyValuePair<string, object?>("outcome", outcome switch
        {
            IdempotencyOutcome.Replay => "replayed",
            IdempotencyOutcome.KeyReused => "key_reused",
            IdempotencyOutcome.InProgress => "in_progress",
            _ => "started",
        }));

    private async Task RunAndStoreAsync(ResourceExecutingContext context, ResourceExecutionDelegate next, Guid recordId)
    {
        var http = context.HttpContext;
        var original = http.Features.Get<IHttpResponseBodyFeature>()!;
        using var buffer = new MemoryStream();
        var buffered = new StreamResponseBodyFeature(buffer, original);
        http.Features.Set<IHttpResponseBodyFeature>(buffered);
        ResourceExecutedContext executed;
        try
        {
            executed = await next();
            await buffered.CompleteAsync();
        }
        catch
        {
            await store.ReleaseAsync(recordId, CancellationToken.None);
            throw;
        }
        finally
        {
            http.Features.Set(original);
        }

        // An exception the action did not handle goes on to the exception handler, which answers 500.
        if (executed.Exception is not null && !executed.ExceptionHandled)
        {
            await store.ReleaseAsync(recordId, CancellationToken.None);
            return;
        }

        var response = http.Response;
        if (IsStored(response.StatusCode))
        {
            var location = response.Headers.Location.Count > 0 ? response.Headers.Location[0] : null;
            await store.CompleteAsync(
                recordId, new StoredResponse(response.StatusCode, response.ContentType, location, buffer.ToArray()), CancellationToken.None);
        }
        else
        {
            await store.ReleaseAsync(recordId, CancellationToken.None);
        }

        buffer.Position = 0;
        await buffer.CopyToAsync(response.Body, http.RequestAborted);
    }

    private ObjectResult Problem(HttpContext http, Error error, int statusCode)
    {
        var problem = problems.CreateProblemDetails(http, statusCode: statusCode, detail: error.Message);
        problem.Extensions[ApiProblems.CodeMember] = error.Code;
        return new ObjectResult(problem) { StatusCode = statusCode, ContentTypes = { MediaTypeNames.Application.ProblemJson } };
    }

    [LoggerMessage(EventId = 3301, Level = LogLevel.Information,
        Message = "Replayed the stored {StatusCode} response to client {ClientId} for idempotency key {IdempotencyKey}")]
    private static partial void LogReplayed(ILogger logger, string clientId, string idempotencyKey, int statusCode);

    [LoggerMessage(EventId = 3302, Level = LogLevel.Warning,
        Message = "Client {ClientId} reused idempotency key {IdempotencyKey} for a different request; refused")]
    private static partial void LogKeyReused(ILogger logger, string clientId, string idempotencyKey);

    [LoggerMessage(EventId = 3303, Level = LogLevel.Information,
        Message = "Client {ClientId} retried idempotency key {IdempotencyKey} while the first request was still running")]
    private static partial void LogInProgress(ILogger logger, string clientId, string idempotencyKey);

    /// <summary>Writes a stored response again, marked as a replay.</summary>
    /// <param name="stored">The stored response.</param>
    private sealed class StoredResponseResult(StoredResponse stored) : IActionResult
    {
        public async Task ExecuteResultAsync(ActionContext context)
        {
            var response = context.HttpContext.Response;
            response.StatusCode = stored.StatusCode;
            if (stored.ContentType is not null)
            {
                response.ContentType = stored.ContentType;
            }

            if (stored.Location is not null)
            {
                response.Headers.Location = stored.Location;
            }

            response.Headers[IdempotentAttribute.ReplayedHeader] = "true";
            response.ContentLength = stored.Body.Length;
            await response.Body.WriteAsync(stored.Body, context.HttpContext.RequestAborted);
        }
    }
}
