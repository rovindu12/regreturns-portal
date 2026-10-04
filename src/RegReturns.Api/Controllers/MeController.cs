using System.Net.Mime;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using RegReturns.Api.Authentication;
using RegReturns.Api.Contracts;
using RegReturns.Application.Authorization;
using RegReturns.Application.Messaging;
using RegReturns.Application.Reference;

namespace RegReturns.Api.Controllers;

/// <summary>Describes the calling client.</summary>
/// <param name="institutions">Reads the caller's institution.</param>
[ApiController]
[Route("v1/me")]
[Authorize(Policy = Policies.ApiReferenceRead)]
public sealed class MeController(IQueryHandler<GetInstitution, InstitutionReference?> institutions) : ControllerBase
{
    /// <summary>Returns the client id, its institution and the scopes granted to the token.</summary>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The caller.</returns>
    [HttpGet]
    [ProducesResponseType<MeResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, MediaTypeNames.Application.ProblemJson)]
    public async Task<ActionResult<MeResponse>> GetAsync(CancellationToken cancellationToken)
    {
        var caller = ApiCaller.FromPrincipal(User);
        if (caller is null)
        {
            return Forbid();
        }

        // The institution was resolved when the request was authenticated; it can only be gone if it was
        // deactivated within the last minute, and then the client has lost access.
        var institution = await institutions.HandleAsync(new GetInstitution(caller.InstitutionId, caller.InstitutionCode), cancellationToken);
        if (institution is null)
        {
            return Forbid();
        }

        return new MeResponse(caller.ClientId, institution.ToResponse(), caller.Scopes);
    }
}
