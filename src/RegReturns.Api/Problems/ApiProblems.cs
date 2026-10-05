using System.Net.Mime;

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;

using RegReturns.Application.Abstractions;
using RegReturns.Application.Idempotency;
using RegReturns.Application.Identity;
using RegReturns.Application.Returns;
using RegReturns.Domain.Common;
using RegReturns.Domain.Submissions;

namespace RegReturns.Api.Problems;

/// <summary>
/// Turns refused use cases into RFC 9457 problem details (ADR 0027): the status follows the meaning of the error, and
/// the stable error <c>code</c> rides along so clients can branch on it without parsing messages.
/// </summary>
internal static class ApiProblems
{
    /// <summary>The problem member that carries the stable error code.</summary>
    public const string CodeMember = "code";

    /// <summary>The code of a request the model validation refused.</summary>
    public const string InvalidRequestCode = "Request.Invalid";

    /// <summary>The code of a request refused by the rate limiter.</summary>
    public const string RateLimitedCode = "RateLimit.Exceeded";

    // 403: the caller, not the content, is the problem.
    private static readonly HashSet<string> Forbidden = new(StringComparer.Ordinal)
    {
        ActorErrors.NotLinked.Code,
        SubmissionErrors.RoleRequired.Code,
        SubmissionErrors.WrongInstitution.Code,
        SubmissionErrors.RegulatorOnly.Code,
    };

    // 409: the request is fine, the state of the return or of an earlier request does not allow it now.
    private static readonly HashSet<string> Conflicts = new(StringComparer.Ordinal)
    {
        PersistenceErrors.Conflict.Code,
        DeliveryErrors.Concurrent.Code,
        SubmissionErrors.EditConflict.Code,
        SubmissionErrors.InvalidTransition.Code,
        SubmissionErrors.NotEditable.Code,
        SubmissionErrors.ObligationClosed.Code,
        IdempotencyErrors.InProgress.Code,
    };

    // 400: the request itself is malformed.
    private static readonly HashSet<string> BadRequests = new(StringComparer.Ordinal)
    {
        IdempotencyErrors.KeyRequired.Code,
        IdempotencyErrors.KeyInvalid.Code,
        InvalidRequestCode,
    };

    /// <summary>Returns the HTTP status for an error: 400, 403, 404, 409 or, for content that breaks a rule, 422.</summary>
    /// <param name="error">The error.</param>
    /// <returns>The status code.</returns>
    public static int StatusFor(Error error)
    {
        ArgumentNullException.ThrowIfNull(error);
        if (BadRequests.Contains(error.Code))
        {
            return StatusCodes.Status400BadRequest;
        }

        if (Forbidden.Contains(error.Code))
        {
            return StatusCodes.Status403Forbidden;
        }

        if (error.Code.EndsWith(".NotFound", StringComparison.Ordinal))
        {
            return StatusCodes.Status404NotFound;
        }

        return Conflicts.Contains(error.Code) ? StatusCodes.Status409Conflict : StatusCodes.Status422UnprocessableEntity;
    }

    /// <summary>Builds the problem response for an error.</summary>
    /// <param name="factory">The framework's problem factory, which adds the type link and trace id.</param>
    /// <param name="httpContext">The current request.</param>
    /// <param name="error">The error.</param>
    /// <returns>The response.</returns>
    public static ObjectResult Create(ProblemDetailsFactory factory, HttpContext httpContext, Error error)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(error);
        var status = StatusFor(error);
        var problem = factory.CreateProblemDetails(httpContext, statusCode: status, detail: error.Message);
        problem.Extensions[CodeMember] = error.Code;
        return new ObjectResult(problem)
        {
            StatusCode = status,
            ContentTypes = { MediaTypeNames.Application.ProblemJson },
        };
    }

    /// <summary>Builds the problem response for an error from a controller.</summary>
    /// <param name="controller">The controller.</param>
    /// <param name="error">The error.</param>
    /// <returns>The response.</returns>
    public static ObjectResult Problem(this ControllerBase controller, Error error)
    {
        ArgumentNullException.ThrowIfNull(controller);
        return Create(controller.ProblemDetailsFactory, controller.HttpContext, error);
    }

    /// <summary>
    /// Builds the 400 validation problem for a request the model validation (or an action's own checks) refused,
    /// with the <c>Request.Invalid</c> code. The framework's <c>ValidationProblem()</c> leaves the code out.
    /// </summary>
    /// <param name="context">The action context, whose model state holds the errors.</param>
    /// <returns>The response.</returns>
    public static BadRequestObjectResult InvalidRequest(ActionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var factory = context.HttpContext.RequestServices.GetRequiredService<ProblemDetailsFactory>();
        var problem = factory.CreateValidationProblemDetails(context.HttpContext, context.ModelState);
        problem.Extensions[CodeMember] = InvalidRequestCode;
        return new BadRequestObjectResult(problem) { ContentTypes = { MediaTypeNames.Application.ProblemJson } };
    }

    /// <summary>Builds the 400 validation problem for the errors in a controller's model state.</summary>
    /// <param name="controller">The controller.</param>
    /// <returns>The response.</returns>
    public static BadRequestObjectResult InvalidRequest(this ControllerBase controller)
    {
        ArgumentNullException.ThrowIfNull(controller);
        return InvalidRequest(controller.ControllerContext);
    }
}
