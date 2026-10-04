namespace RegReturns.Application.Messaging;

/// <summary>Handles a command that changes state.</summary>
/// <typeparam name="TCommand">The command type.</typeparam>
/// <typeparam name="TResult">The result type, usually a <c>Result</c>.</typeparam>
public interface ICommandHandler<in TCommand, TResult>
{
    /// <summary>Runs the command.</summary>
    /// <param name="command">The command.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The result.</returns>
    Task<TResult> HandleAsync(TCommand command, CancellationToken cancellationToken);
}
