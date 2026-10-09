namespace RegReturns.Domain.Submissions;

/// <summary>Workflow states of a submission.</summary>
public enum SubmissionStatus
{
    /// <summary>Being prepared by the bank's maker.</summary>
    Draft = 1,

    /// <summary>Submitted by the bank's checker; waiting for a reviewer.</summary>
    Submitted = 2,

    /// <summary>Picked up by a supervisor reviewer.</summary>
    UnderReview = 3,

    /// <summary>Sent back to the bank to correct.</summary>
    ReturnedForCorrection = 4,

    /// <summary>Accepted by a supervisor approver. Final.</summary>
    Approved = 5,

    /// <summary>Refused by a supervisor approver. Final.</summary>
    Rejected = 6,
}

/// <summary>Actions that move a submission through its workflow.</summary>
public enum WorkflowAction
{
    /// <summary>The draft was created.</summary>
    Create = 1,

    /// <summary>The bank checker submits (or resubmits) the return.</summary>
    Submit = 2,

    /// <summary>A supervisor reviewer picks the return up.</summary>
    StartReview = 3,

    /// <summary>A supervisor sends the return back for correction.</summary>
    ReturnForCorrection = 4,

    /// <summary>A supervisor approver approves the return.</summary>
    Approve = 5,

    /// <summary>A supervisor approver rejects the return.</summary>
    Reject = 6,

    /// <summary>
    /// The return was loaded, already approved, from the legacy returns system (ADR 0029). It is the only step of a
    /// migrated return and no workflow transition leads to or from it.
    /// </summary>
    Migrate = 7,
}

/// <summary>Where a submission's data came from.</summary>
public enum SubmissionSource
{
    /// <summary>Entered in the web form.</summary>
    Web = 1,

    /// <summary>Uploaded as Excel or CSV.</summary>
    Upload = 2,

    /// <summary>Sent by a bank system through the API.</summary>
    Api = 3,

    /// <summary>Migrated from the legacy system.</summary>
    Migration = 4,
}
