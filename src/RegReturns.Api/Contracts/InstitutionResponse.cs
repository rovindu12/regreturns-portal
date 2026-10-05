namespace RegReturns.Api.Contracts;

/// <summary>An institution as the API returns it.</summary>
/// <param name="Code">The institution code, for example <c>HLB</c>.</param>
/// <param name="Name">The registered name.</param>
/// <param name="Type">The licence category: <c>Commercial</c>, <c>Savings</c> or <c>Development</c>.</param>
public sealed record InstitutionResponse(string Code, string Name, string Type);
