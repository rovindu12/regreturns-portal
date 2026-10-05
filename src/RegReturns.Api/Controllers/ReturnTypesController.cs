using System.ComponentModel.DataAnnotations;
using System.Net.Mime;

using Asp.Versioning;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using RegReturns.Api.Contracts;
using RegReturns.Api.Problems;
using RegReturns.Application.Authorization;
using RegReturns.Application.Messaging;
using RegReturns.Application.Templates;
using RegReturns.Domain.Common;
using RegReturns.Domain.Periods;

namespace RegReturns.Api.Controllers;

/// <summary>The return types banks file and the templates they are filed with (reference data).</summary>
[ApiController]
[ApiVersion(1)]
[Route("v{version:apiVersion}/return-types")]
[Authorize(Policy = Policies.ApiReferenceRead)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, MediaTypeNames.Application.ProblemJson)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, MediaTypeNames.Application.ProblemJson)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests, MediaTypeNames.Application.ProblemJson)]
public sealed class ReturnTypesController : ControllerBase
{
    /// <summary>The pattern of a reporting period in a query string.</summary>
    internal const string PeriodPattern = "^[0-9]{4}-(0[1-9]|1[0-2]|Q[1-4])$";

    /// <summary>The message for a malformed period.</summary>
    internal const string PeriodMessage = "The period is a month such as 2027-03 or a quarter such as 2027-Q1.";

    /// <summary>Lists the return types banks file, with their published template versions.</summary>
    /// <param name="returnTypes">Reads the return types.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The return types, by code.</returns>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<ReturnTypeResponse>>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    public async Task<ActionResult<IReadOnlyList<ReturnTypeResponse>>> ListAsync(
        [FromServices] IQueryHandler<GetReturnTypes, IReadOnlyList<ReturnTypeInfo>> returnTypes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(returnTypes);
        var list = await returnTypes.HandleAsync(new GetReturnTypes(), cancellationToken);
        return Ok(list.Select(r => r.ToResponse()).ToList());
    }

    /// <summary>
    /// Returns the template a return is filed with: its fields (the codes values are keyed by) and validation rules.
    /// The version is the one that applies to the period, or to the current period when none is given.
    /// </summary>
    /// <param name="code">The return type code, such as <c>MLR</c>.</param>
    /// <param name="period">The reporting period, such as <c>2027-03</c> or <c>2027-Q1</c>.</param>
    /// <param name="templates">Reads the template.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The template.</returns>
    [HttpGet("{code}/template")]
    [ProducesResponseType<TemplateResponse>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity, MediaTypeNames.Application.ProblemJson)]
    public async Task<ActionResult<TemplateResponse>> GetTemplateAsync(
        string code,
        [FromQuery, RegularExpression(PeriodPattern, ErrorMessage = PeriodMessage)] string? period,
        [FromServices] IQueryHandler<GetReturnTypeTemplate, Result<ReturnTypeTemplate>> templates,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(templates);
        ReportingPeriod? parsed = null;
        if (period is not null && !ReportingPeriod.TryParse(period, out parsed))
        {
            ModelState.AddModelError(nameof(period), PeriodMessage);
            return this.InvalidRequest();
        }

        var template = await templates.HandleAsync(new GetReturnTypeTemplate(code, parsed), cancellationToken);
        return template.IsSuccess ? template.Value.ToResponse() : this.Problem(template.Error!);
    }
}
