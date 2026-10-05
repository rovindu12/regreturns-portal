using RegReturns.Application.Returns;

namespace RegReturns.Web.Models.Bank;

/// <summary>The upload page of an obligation.</summary>
/// <param name="Obligation">The obligation and its latest return.</param>
/// <param name="Error">Why the last upload was refused.</param>
public sealed record UploadViewModel(BankReturnRow Obligation, string? Error = null)
{
    /// <summary>Gets the largest file the portal accepts.</summary>
    public static int MaxFileBytes => Domain.Submissions.StoredFile.MaxSizeBytes;
}
