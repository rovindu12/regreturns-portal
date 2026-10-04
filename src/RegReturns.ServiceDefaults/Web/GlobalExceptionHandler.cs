using System.Diagnostics;

using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace RegReturns.ServiceDefaults.Web;

/// <summary>
/// Logs unhandled exceptions with the request's trace id and returns an RFC 9457 problem response
/// that contains the trace id but never the exception details.
/// </summary>
/// <param name="problemDetails">The problem details writer.</param>
/// <param name="logger">The logger.</param>
internal sealed partial class GlobalExceptionHandler(
    IProblemDetailsService problemDetails,
    ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is OperationCanceledException && httpContext.RequestAborted.IsCancellationRequested)
        {
            LogRequestAborted(logger, httpContext.Request.Path);
            return true;
        }

        LogUnhandled(logger, exception, httpContext.Request.Method, httpContext.Request.Path);

        httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = new ProblemDetails
            {
                Status = StatusCodes.Status500InternalServerError,
                Title = "An unexpected error occurred.",
                Detail = $"Quote error reference {Activity.Current?.TraceId.ToString() ?? httpContext.TraceIdentifier} when contacting support.",
            },
        });
    }

    [LoggerMessage(EventId = 9001, Level = LogLevel.Error, Message = "Unhandled exception for {Method} {Path}")]
    private static partial void LogUnhandled(ILogger logger, Exception exception, string method, PathString path);

    [LoggerMessage(EventId = 9002, Level = LogLevel.Information, Message = "Request to {Path} was aborted by the client")]
    private static partial void LogRequestAborted(ILogger logger, PathString path);
}
