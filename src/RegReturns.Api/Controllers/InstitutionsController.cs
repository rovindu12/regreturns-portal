using System.Net.Mime;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using RegReturns.Api.Authentication;
using RegReturns.Api.Contracts;
using RegReturns.Application.Authorization;
using RegReturns.Application.Messaging;
using RegReturns.Application.Reference;

namespace RegReturns.Api.Controllers;

/// <summary>Institution reference data, scoped to the calling client's own institution.</summary>
/// <param name="institutions">Reads institutions visible to the caller.</param>
[ApiController]
[Route("v1/institutions")]
[Authorize(Policy = Policies.ApiReferenceRead)]
public sealed class InstitutionsController(IQueryHandler<GetInstitution, InstitutionReference?> institutions) : ControllerBase
{
    /// <summary>Title of the response for a code the caller cannot see.</summary>
    internal const string NotFoundTitle = "Institution not found.";

    /// <summary>Detail of the response for a code the caller cannot see. It does not say whether the code exists.</summary>
    internal const string NotFoundDetail = "No institution with this code is available to the calling client.";

    /// <summary>
    /// Returns the caller's own institution. Any other code, including another bank's, gets the same 404 as an
    /// unknown code, so the response never reveals which institutions exist.
    /// </summary>
    /// <param name="code">The institution code (case-insensitive).</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The institution.</returns>
    [HttpGet("{code}")]
    [ProducesResponseType<InstitutionResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, MediaTypeNames.Application.ProblemJson)]
    public async Task<ActionResult<InstitutionResponse>> GetByCodeAsync(string code, CancellationToken cancellationToken)
    {
        var caller = ApiCaller.FromPrincipal(User);
        if (caller is null)
        {
            return Forbid();
        }

        var institution = await institutions.HandleAsync(new GetInstitution(caller.InstitutionId, code), cancellationToken);
        if (institution is null)
        {
            return Problem(statusCode: StatusCodes.Status404NotFound, title: NotFoundTitle, detail: NotFoundDetail);
        }

        return institution.ToResponse();
    }
}
