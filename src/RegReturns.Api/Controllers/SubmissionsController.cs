using System.ComponentModel.DataAnnotations;
using System.Net.Mime;

using Asp.Versioning;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using RegReturns.Api.Contracts;
using RegReturns.Api.Idempotency;
using RegReturns.Api.Paging;
using RegReturns.Api.Problems;
using RegReturns.Application.Authorization;
using RegReturns.Application.Messaging;
using RegReturns.Application.Paging;
using RegReturns.Application.Returns;
using RegReturns.Domain.Common;
using RegReturns.Domain.Periods;
using RegReturns.Domain.Submissions;

namespace RegReturns.Api.Controllers;

/// <summary>
/// The calling bank's returns: read them, and deliver a return as a draft that a bank checker submits in the portal
/// (ADR 0026). Another bank's ids answer 404, the same as unknown ids.
/// </summary>
[ApiController]
[ApiVersion(1)]
[Route("v{version:apiVersion}/submissions")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, MediaTypeNames.Application.ProblemJson)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, MediaTypeNames.Application.ProblemJson)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests, MediaTypeNames.Application.ProblemJson)]
public sealed class SubmissionsController : ControllerBase
{
    /// <summary>The name of the route to one return, used for the <c>Location</c> of a new draft.</summary>
    internal const string GetByIdRoute = "GetSubmission";

    private const string StatusMessage =
        "The status is one of Draft, Submitted, UnderReview, ReturnedForCorrection, Approved or Rejected.";

    /// <summary>Lists the bank's returns, newest first, one page at a time.</summary>
    /// <param name="handler">Reads the returns.</param>
    /// <param name="page">The page, from 1.</param>
    /// <param name="pageSize">The page size, from 1 to 100.</param>
    /// <param name="status">Only returns in this status, such as <c>Draft</c> or <c>ReturnedForCorrection</c>.</param>
    /// <param name="returnType">Only returns of this type, such as <c>MLR</c>.</param>
    /// <param name="period">Only returns for this period, such as <c>2027-03</c> or <c>2027-Q1</c>.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The page, with first, prev, next and last links in the <c>Link</c> header.</returns>
    [HttpGet]
    [Authorize(Policy = Policies.ApiReturnsRead)]
    [ProducesResponseType<PagedResponse<SubmissionResponse>>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, MediaTypeNames.Application.ProblemJson)]
    public async Task<ActionResult<PagedResponse<SubmissionResponse>>> ListAsync(
        [FromServices] IQueryHandler<GetSubmissions, Result<PagedList<SubmissionSummary>>> handler,
        [FromQuery, Range(1, int.MaxValue)] int page = 1,
        [FromQuery, Range(1, PageRequest.MaxPageSize)] int pageSize = PageRequest.DefaultPageSize,
        [FromQuery] string? status = null,
        [FromQuery, RegularExpression("^[A-Za-z0-9]{1,10}$")] string? returnType = null,
        [FromQuery, RegularExpression(ReturnTypesController.PeriodPattern, ErrorMessage = ReturnTypesController.PeriodMessage)] string? period = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(handler);
        SubmissionStatus? statusFilter = null;
        if (status is not null)
        {
            if (!TryParseStatus(status, out var parsedStatus))
            {
                ModelState.AddModelError(nameof(status), StatusMessage);
                return this.InvalidRequest();
            }

            statusFilter = parsedStatus;
        }

        ReportingPeriod? periodFilter = null;
        if (period is not null && !ReportingPeriod.TryParse(period, out periodFilter))
        {
            ModelState.AddModelError(nameof(period), ReturnTypesController.PeriodMessage);
            return this.InvalidRequest();
        }

        var result = await handler.HandleAsync(
            new GetSubmissions(new PageRequest(page, pageSize), statusFilter, returnType, periodFilter), cancellationToken);
        if (result.IsFailure)
        {
            return this.Problem(result.Error!);
        }

        var list = result.Value;
        Response.Headers.Link = PagingLinks.Build(Request, list.Page, list.PageSize, list.TotalPages);
        return list.ToResponse();
    }

    /// <summary>Returns one of the bank's returns with its values and workflow history.</summary>
    /// <param name="id">The submission id.</param>
    /// <param name="handler">Reads the return.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The return.</returns>
    [HttpGet("{id:guid}", Name = GetByIdRoute)]
    [Authorize(Policy = Policies.ApiReturnsRead)]
    [ProducesResponseType<SubmissionDetailResponse>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, MediaTypeNames.Application.ProblemJson)]
    public async Task<ActionResult<SubmissionDetailResponse>> GetAsync(
        Guid id, [FromServices] IQueryHandler<GetSubmission, Result<SubmissionDetail>> handler, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(handler);
        var result = await handler.HandleAsync(new GetSubmission(id), cancellationToken);
        return result.IsSuccess ? result.Value.ToResponse() : this.Problem(result.Error!);
    }

    /// <summary>Returns the validation findings of a return's current revision.</summary>
    /// <param name="id">The submission id.</param>
    /// <param name="handler">Reads the findings.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The findings and what still blocks submission.</returns>
    [HttpGet("{id:guid}/validation")]
    [Authorize(Policy = Policies.ApiReturnsRead)]
    [ProducesResponseType<ValidationResponse>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, MediaTypeNames.Application.ProblemJson)]
    public async Task<ActionResult<ValidationResponse>> GetValidationAsync(
        Guid id, [FromServices] IQueryHandler<GetSubmissionValidation, Result<SubmissionValidation>> handler, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(handler);
        var result = await handler.HandleAsync(new GetSubmissionValidation(id), cancellationToken);
        return result.IsSuccess ? result.Value.ToResponse() : this.Problem(result.Error!);
    }

    /// <summary>
    /// Delivers a complete return for a period: starts a draft (201) or replaces the values of the open return (200),
    /// and validates it. Findings are part of the answer; a bank checker justifies warnings and submits the return in
    /// the portal. Fields left out become blank.
    /// </summary>
    /// <param name="request">The return type, period and every value.</param>
    /// <param name="handler">Delivers the return.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>What the delivery did and the findings.</returns>
    [HttpPost]
    [Idempotent]
    [Authorize(Policy = Policies.ApiReturnsSubmit)]
    [RequestSizeLimit(IdempotencyFilter.MaxBodyBytes)]
    [Consumes(MediaTypeNames.Application.Json)]
    [ProducesResponseType<DeliveryResponse>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<DeliveryResponse>(StatusCodes.Status201Created, MediaTypeNames.Application.Json)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status413PayloadTooLarge, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity, MediaTypeNames.Application.ProblemJson)]
    public async Task<ActionResult<DeliveryResponse>> DeliverAsync(
        [FromBody] DeliverReturnRequest request,
        [FromServices] ICommandHandler<DeliverReturn, Result<DeliveryOutcome>> handler,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(handler);
        var values = request.Values!.ToRawValues(out var invalid);
        foreach (var code in invalid)
        {
            ModelState.AddModelError($"values.{code}", "A value is a string, a number, true, false or null.");
        }

        if (!ReportingPeriod.TryParse(request.Period, out var period))
        {
            ModelState.AddModelError(nameof(request.Period), ReturnTypesController.PeriodMessage);
        }

        if (!ModelState.IsValid)
        {
            return this.InvalidRequest();
        }

        var result = await handler.HandleAsync(new DeliverReturn(request.ReturnType!, period!, values), cancellationToken);
        if (result.IsFailure)
        {
            return this.Problem(result.Error!);
        }

        var body = result.Value.ToResponse();
        if (!result.Value.Created)
        {
            return body;
        }

        var version = HttpContext.RequestedApiVersion?.ToString() ?? "1";
        return CreatedAtRoute(GetByIdRoute, new { version, id = body.SubmissionId }, body);
    }

    /// <summary>Parses a status name, refusing numbers (which <see cref="Enum.TryParse{TEnum}(string, bool, out TEnum)"/> accepts).</summary>
    private static bool TryParseStatus(string value, out SubmissionStatus status) =>
        Enum.TryParse(value, ignoreCase: true, out status)
        && Enum.IsDefined(status)
        && !value.Any(char.IsDigit);
}
