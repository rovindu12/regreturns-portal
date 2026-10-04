namespace RegReturns.IamBootstrap.Steps;

/// <summary>One idempotent part of the WSO2 setup. Steps run in registration order and share a <see cref="BootstrapState"/>.</summary>
internal interface IBootstrapStep
{
    /// <summary>Gets a short name for logs.</summary>
    string Name { get; }

    /// <summary>Brings WSO2 (and the app database, where relevant) to the desired state.</summary>
    /// <param name="state">State shared between steps.</param>
    /// <param name="cancellationToken">Cancels the step.</param>
    /// <returns>A task that completes when the step is done.</returns>
    Task RunAsync(BootstrapState state, CancellationToken cancellationToken);
}
