namespace RegReturns.Api.Contracts;

/// <summary>Who the API thinks the caller is: lets a bank check its client registration end to end.</summary>
/// <param name="ClientId">The OAuth client id of the token.</param>
/// <param name="Institution">The institution the client acts for.</param>
/// <param name="Scopes">The scopes granted to the token, sorted.</param>
public sealed record MeResponse(string ClientId, InstitutionResponse Institution, IReadOnlyList<string> Scopes);
