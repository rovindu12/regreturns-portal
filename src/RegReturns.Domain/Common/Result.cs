namespace RegReturns.Domain.Common;

/// <summary>
/// The outcome of an operation that can fail for an expected business reason.
/// </summary>
public class Result
{
    /// <summary>Initializes a new instance of the <see cref="Result"/> class.</summary>
    /// <param name="error">The failure, or <see langword="null"/> for success.</param>
    protected Result(Error? error) => Error = error;

    /// <summary>Gets a value indicating whether the operation succeeded.</summary>
    public bool IsSuccess => Error is null;

    /// <summary>Gets a value indicating whether the operation failed.</summary>
    public bool IsFailure => !IsSuccess;

    /// <summary>Gets the failure, or <see langword="null"/> when the operation succeeded.</summary>
    public Error? Error { get; }

    /// <summary>Creates a successful result.</summary>
    /// <returns>A successful <see cref="Result"/>.</returns>
    public static Result Success() => new(null);

    /// <summary>Creates a successful result carrying a value.</summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="value">The value.</param>
    /// <returns>A successful <see cref="Result{T}"/>.</returns>
    public static Result<T> Success<T>(T value) => new(value, null);

    /// <summary>Creates a failed result.</summary>
    /// <param name="error">The reason for the failure.</param>
    /// <returns>A failed <see cref="Result"/>.</returns>
    public static Result Failure(Error error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new Result(error);
    }

    /// <summary>Creates a failed result of the given value type.</summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="error">The reason for the failure.</param>
    /// <returns>A failed <see cref="Result{T}"/>.</returns>
    public static Result<T> Failure<T>(Error error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new Result<T>(default, error);
    }

    /// <summary>Converts an <see cref="Common.Error"/> into a failed result.</summary>
    /// <param name="error">The failure.</param>
    public static implicit operator Result(Error error) => Failure(error);
}

/// <summary>
/// The outcome of an operation that returns a value when it succeeds.
/// </summary>
/// <typeparam name="T">The value type.</typeparam>
public sealed class Result<T> : Result
{
    private readonly T? _value;

    internal Result(T? value, Error? error)
        : base(error) => _value = value;

    /// <summary>Gets the value. Throws when the result is a failure.</summary>
    /// <exception cref="InvalidOperationException">The result is a failure.</exception>
    public T Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException($"Cannot read the value of a failed result ({Error!.Code}).");

    /// <summary>Converts a value into a successful result.</summary>
    /// <param name="value">The value.</param>
    public static implicit operator Result<T>(T value) => new(value, null);

    /// <summary>Converts an <see cref="Common.Error"/> into a failed result.</summary>
    /// <param name="error">The failure.</param>
    public static implicit operator Result<T>(Error error) => new(default, error);
}
