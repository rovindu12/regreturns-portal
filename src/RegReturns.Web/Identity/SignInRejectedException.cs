namespace RegReturns.Web.Identity;

/// <summary>
/// Fails the OpenID Connect sign-in when the portal refuses a valid token (for example an unknown institution).
/// It carries only a stable error code, which the failure page and the audit trail use; the rejection has already
/// been audited when this is thrown.
/// </summary>
public sealed class SignInRejectedException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="SignInRejectedException"/> class.</summary>
    public SignInRejectedException()
        : this("User.SignInRejected")
    {
    }

    /// <summary>Initializes a new instance of the <see cref="SignInRejectedException"/> class.</summary>
    /// <param name="code">The stable error code, for example <c>User.UnknownInstitution</c>.</param>
    public SignInRejectedException(string code)
        : base($"The portal refused the sign-in ({code}).") => Code = code;

    /// <summary>Initializes a new instance of the <see cref="SignInRejectedException"/> class.</summary>
    /// <param name="code">The stable error code.</param>
    /// <param name="innerException">The underlying exception.</param>
    public SignInRejectedException(string code, Exception innerException)
        : base($"The portal refused the sign-in ({code}).", innerException) => Code = code;

    /// <summary>Gets the stable error code.</summary>
    public string Code { get; }
}
