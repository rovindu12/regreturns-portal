using RegReturns.Domain.Common;
using RegReturns.Domain.Periods;

namespace RegReturns.Domain.Templates;

/// <summary>A kind of regulatory return, such as the Monthly Liquidity Return.</summary>
public sealed class ReturnType : Entity
{
    /// <summary>Maximum length of a return type code.</summary>
    public const int CodeMaxLength = 10;

    /// <summary>Maximum length of a return type name.</summary>
    public const int NameMaxLength = 128;

    /// <summary>Maximum length of a description.</summary>
    public const int DescriptionMaxLength = 1000;

    private ReturnType()
    {
        Code = string.Empty;
        Name = string.Empty;
        Description = string.Empty;
    }

    /// <summary>Gets the short code (for example <c>MLR</c>).</summary>
    public string Code { get; private set; }

    /// <summary>Gets the name.</summary>
    public string Name { get; private set; }

    /// <summary>Gets a description of what the return covers.</summary>
    public string Description { get; private set; }

    /// <summary>Gets how often the return is filed.</summary>
    public ReturnFrequency Frequency { get; private set; }

    /// <summary>Gets the number of calendar days after period end by which the return is due.</summary>
    public int DueDaysAfterPeriodEnd { get; private set; }

    /// <summary>Gets a value indicating whether banks currently file this return.</summary>
    public bool IsActive { get; private set; }

    /// <summary>Creates an active return type.</summary>
    /// <param name="code">Short code.</param>
    /// <param name="name">Name.</param>
    /// <param name="description">Description.</param>
    /// <param name="frequency">Filing frequency.</param>
    /// <param name="dueDaysAfterPeriodEnd">Days after period end the return is due (1-120).</param>
    /// <returns>The new return type.</returns>
    public static ReturnType Create(
        string code, string name, string description, ReturnFrequency frequency, int dueDaysAfterPeriodEnd)
    {
        if (dueDaysAfterPeriodEnd is < 1 or > 120)
        {
            throw new DomainException("Due days must be between 1 and 120.");
        }

        return new ReturnType
        {
            Code = Guard.Code(code, CodeMaxLength),
            Name = Guard.NotBlank(name, NameMaxLength),
            Description = Guard.NotBlank(description, DescriptionMaxLength),
            Frequency = frequency,
            DueDaysAfterPeriodEnd = dueDaysAfterPeriodEnd,
            IsActive = true,
        };
    }

    /// <summary>Returns the due date for a period.</summary>
    /// <param name="period">The reporting period; must match this return's frequency.</param>
    /// <returns>The last day on which a submission is on time.</returns>
    public DateOnly DueDateFor(ReportingPeriod period)
    {
        ArgumentNullException.ThrowIfNull(period);
        return period.Frequency != Frequency
            ? throw new DomainException($"{Code} is {Frequency}; period {period.Label} is {period.Frequency}.")
            : period.End.AddDays(DueDaysAfterPeriodEnd);
    }

    /// <summary>Stops banks filing this return.</summary>
    public void Deactivate() => IsActive = false;
}
