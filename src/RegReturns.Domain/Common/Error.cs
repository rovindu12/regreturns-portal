namespace RegReturns.Domain.Common;

/// <summary>
/// Describes an expected business failure, such as a rule violation, without using exceptions for control flow.
/// </summary>
/// <param name="Code">A stable, dotted code (for example <c>Submission.NotEditable</c>) safe to log and show to API clients.</param>
/// <param name="Message">A human-readable description of the failure.</param>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Naming",
    "CA1716:Identifiers should not match keywords",
    Justification = "C#-only codebase; 'Error' is the conventional name for this type.")]
public sealed record Error(string Code, string Message)
{
    /// <summary>Returns a copy of this error with a more specific message.</summary>
    /// <param name="message">The replacement message.</param>
    /// <returns>A new <see cref="Error"/> with the same code.</returns>
    public Error WithMessage(string message) => this with { Message = message };
}
