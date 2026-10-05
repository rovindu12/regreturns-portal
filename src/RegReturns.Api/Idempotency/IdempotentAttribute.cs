using Microsoft.AspNetCore.Mvc.Filters;

namespace RegReturns.Api.Idempotency;

/// <summary>
/// Makes an action require an <c>Idempotency-Key</c> header and answer a retry with the stored response of the first
/// request instead of running it again (ADR 0027). Put it on every <c>POST</c> action.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class IdempotentAttribute : Attribute, IFilterFactory
{
    /// <summary>The request header that carries the key.</summary>
    public const string KeyHeader = "Idempotency-Key";

    /// <summary>The response header that marks a replayed response.</summary>
    public const string ReplayedHeader = "Idempotent-Replayed";

    /// <inheritdoc />
    public bool IsReusable => false;

    /// <inheritdoc />
    public IFilterMetadata CreateInstance(IServiceProvider serviceProvider) =>
        ActivatorUtilities.CreateInstance<IdempotencyFilter>(serviceProvider);
}
