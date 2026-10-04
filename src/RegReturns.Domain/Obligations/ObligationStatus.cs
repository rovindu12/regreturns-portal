namespace RegReturns.Domain.Obligations;

/// <summary>Filing status of an obligation.</summary>
public enum ObligationStatus
{
    /// <summary>Nothing has been submitted yet (or the last submission was rejected).</summary>
    Open = 1,

    /// <summary>A submission is with the regulator (submitted, under review or returned for correction).</summary>
    InProgress = 2,

    /// <summary>A submission has been approved.</summary>
    Fulfilled = 3,
}
