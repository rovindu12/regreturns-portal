namespace RegReturns.Domain.Common;

/// <summary>
/// Thrown when code tries to construct an entity or value object in an invalid state.
/// This signals a programming error; expected business failures are returned as <see cref="Result"/>.
/// </summary>
public sealed class DomainException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="DomainException"/> class.</summary>
    public DomainException()
    {
    }

    /// <summary>Initializes a new instance of the <see cref="DomainException"/> class.</summary>
    /// <param name="message">The error message.</param>
    public DomainException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="DomainException"/> class.</summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The underlying exception.</param>
    public DomainException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
