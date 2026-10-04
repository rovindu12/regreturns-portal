using RegReturns.Domain.Common;

namespace RegReturns.Domain.Institutions;

/// <summary>A licensed bank that files regulatory returns.</summary>
public sealed class Institution : Entity
{
    /// <summary>Maximum length of an institution code.</summary>
    public const int CodeMaxLength = 10;

    /// <summary>Maximum length of an institution name.</summary>
    public const int NameMaxLength = 128;

    private Institution()
    {
        Code = string.Empty;
        Name = string.Empty;
    }

    /// <summary>Gets the short code (for example <c>HLB</c>). This is the value of the WSO2 <c>institution_id</c> claim.</summary>
    public string Code { get; private set; }

    /// <summary>Gets the registered name.</summary>
    public string Name { get; private set; }

    /// <summary>Gets the licence category.</summary>
    public LicenceCategory LicenceCategory { get; private set; }

    /// <summary>Gets a value indicating whether the institution is currently licensed and filing.</summary>
    public bool IsActive { get; private set; }

    /// <summary>Creates an active institution.</summary>
    /// <param name="code">Short upper-case code.</param>
    /// <param name="name">Registered name.</param>
    /// <param name="licenceCategory">Licence category.</param>
    /// <returns>The new institution.</returns>
    public static Institution Create(string code, string name, LicenceCategory licenceCategory) => new()
    {
        Code = Guard.Code(code, CodeMaxLength),
        Name = Guard.NotBlank(name, NameMaxLength),
        LicenceCategory = licenceCategory,
        IsActive = true,
    };

    /// <summary>Changes the registered name.</summary>
    /// <param name="name">The new name.</param>
    public void Rename(string name) => Name = Guard.NotBlank(name, NameMaxLength);

    /// <summary>Stops the institution filing returns (for example after a licence is revoked).</summary>
    public void Deactivate() => IsActive = false;

    /// <summary>Re-activates the institution.</summary>
    public void Activate() => IsActive = true;
}
