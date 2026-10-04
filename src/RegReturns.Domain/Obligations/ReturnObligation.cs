using RegReturns.Domain.Common;
using RegReturns.Domain.Periods;
using RegReturns.Domain.Templates;

namespace RegReturns.Domain.Obligations;

/// <summary>
/// The requirement for one bank to file one return type for one reporting period by a due date.
/// Drives compliance, overdue and late-filing reporting.
/// </summary>
public sealed class ReturnObligation : Entity
{
    private ReturnObligation()
    {
        Period = null!;
    }

    /// <summary>Gets the bank that must file.</summary>
    public Guid InstitutionId { get; private set; }

    /// <summary>Gets the return type.</summary>
    public Guid ReturnTypeId { get; private set; }

    /// <summary>Gets the reporting period.</summary>
    public ReportingPeriod Period { get; private set; }

    /// <summary>Gets the last day on which a submission is on time.</summary>
    public DateOnly DueDate { get; private set; }

    /// <summary>Gets the filing status.</summary>
    public ObligationStatus Status { get; private set; }

    /// <summary>Gets when the first submission for this obligation reached the regulator.</summary>
    public DateTimeOffset? FirstSubmittedAt { get; private set; }

    /// <summary>Gets a value indicating whether the first submission arrived after the due date.</summary>
    public bool IsLate { get; private set; }

    /// <summary>Creates an open obligation, deriving the due date from the return type.</summary>
    /// <param name="institutionId">The bank.</param>
    /// <param name="returnType">The return type.</param>
    /// <param name="period">The reporting period.</param>
    /// <returns>The new obligation.</returns>
    public static ReturnObligation Create(Guid institutionId, ReturnType returnType, ReportingPeriod period)
    {
        ArgumentNullException.ThrowIfNull(returnType);
        ArgumentNullException.ThrowIfNull(period);
        return new ReturnObligation
        {
            InstitutionId = Guard.NotEmpty(institutionId),
            ReturnTypeId = returnType.Id,
            Period = period,
            DueDate = returnType.DueDateFor(period),
            Status = ObligationStatus.Open,
        };
    }

    /// <summary>Returns whether the obligation is unfilled after its due date.</summary>
    /// <param name="today">The current date.</param>
    /// <returns><see langword="true"/> if overdue.</returns>
    public bool IsOverdue(DateOnly today) => Status == ObligationStatus.Open && today > DueDate;

    /// <summary>Returns whether a submission made at the given time would be late.</summary>
    /// <param name="at">The submission time.</param>
    /// <returns><see langword="true"/> if after the due date (UTC).</returns>
    public bool WouldBeLate(DateTimeOffset at) => DateOnly.FromDateTime(at.UtcDateTime) > DueDate;

    /// <summary>Records that a submission reached the regulator.</summary>
    /// <param name="at">When it was submitted.</param>
    public void MarkSubmitted(DateTimeOffset at)
    {
        if (FirstSubmittedAt is null)
        {
            FirstSubmittedAt = at;
            IsLate = WouldBeLate(at);
        }

        Status = ObligationStatus.InProgress;
    }

    /// <summary>Records that a submission was approved.</summary>
    public void MarkFulfilled() => Status = ObligationStatus.Fulfilled;

    /// <summary>Re-opens the obligation after a submission was rejected.</summary>
    public void Reopen() => Status = ObligationStatus.Open;
}
