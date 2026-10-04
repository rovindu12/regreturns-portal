namespace RegReturns.Application.Messaging;

/// <summary>Handles a read-only query.</summary>
/// <typeparam name="TQuery">The query type.</typeparam>
/// <typeparam name="TResult">The result type.</typeparam>
public interface IQueryHandler<in TQuery, TResult>
{
    /// <summary>Runs the query.</summary>
    /// <param name="query">The query.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The result.</returns>
    Task<TResult> HandleAsync(TQuery query, CancellationToken cancellationToken);
}
