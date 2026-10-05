using RegReturns.Application.Reference;

namespace RegReturns.Api.Contracts;

/// <summary>Maps application results to API contracts, so wire names never follow internal renames by accident.</summary>
internal static class ContractMappings
{
    /// <summary>Maps an institution.</summary>
    /// <param name="institution">The institution.</param>
    /// <returns>The API representation.</returns>
    public static InstitutionResponse ToResponse(this InstitutionReference institution) =>
        new(institution.Code, institution.Name, institution.LicenceCategory.ToString());
}
