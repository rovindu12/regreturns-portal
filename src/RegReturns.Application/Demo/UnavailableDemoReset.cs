using RegReturns.Domain.Common;

namespace RegReturns.Application.Demo;

/// <summary>The reset of a host that cannot reset the demo (the API); <c>AddDemo</c> in Infrastructure replaces it.</summary>
public sealed class UnavailableDemoReset : IDemoReset
{
    /// <inheritdoc />
    public Task<Result<DemoResetReport>> ResetAsync(DemoResetRequest request, CancellationToken cancellationToken) =>
        Task.FromResult<Result<DemoResetReport>>(DemoErrors.Disabled);
}
