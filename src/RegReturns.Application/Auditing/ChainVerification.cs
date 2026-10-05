using System.Globalization;

namespace RegReturns.Application.Auditing;

/// <summary>Walks the audit hash chain and reports the first entry that breaks it (ADR 0016, ADR 0024).</summary>
public interface IAuditChainVerifier
{
    /// <summary>
    /// Checks every entry up to the newest one present when the check starts, in sequence order and in batches:
    /// sequence numbers run from 1 without gaps, each entry links to the hash of the one before it, and each entry's
    /// hash matches its contents.
    /// </summary>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The outcome.</returns>
    Task<ChainVerification> VerifyAsync(CancellationToken cancellationToken);
}

/// <summary>How the chain is broken.</summary>
public enum ChainBreakKind
{
    /// <summary>Sequence numbers skip: one or more entries were deleted.</summary>
    MissingEntries = 1,

    /// <summary>An entry does not carry the hash of the entry before it: entries were re-ordered or one was replaced.</summary>
    BrokenLink = 2,

    /// <summary>An entry's contents no longer match its hash: it was edited after it was written.</summary>
    EditedEntry = 3,
}

/// <summary>The first place the chain is broken.</summary>
/// <param name="Sequence">The sequence number where the break is: the first missing number, or the offending entry.</param>
/// <param name="Kind">What is wrong there.</param>
/// <param name="Explanation">A plain-English explanation for the auditor.</param>
public sealed record ChainBreak(long Sequence, ChainBreakKind Kind, string Explanation)
{
    /// <summary>Entries are missing before <paramref name="found"/>.</summary>
    /// <param name="expected">The sequence number that should have come next.</param>
    /// <param name="found">The sequence number that came instead.</param>
    /// <returns>The break.</returns>
    public static ChainBreak Missing(long expected, long found)
    {
        var missing = found - expected;
        var range = missing == 1
            ? Format($"Entry {expected} is missing")
            : Format($"Entries {expected} to {found - 1} are missing");
        var context = expected == 1
            ? Format($": the chain starts at entry {found} instead of entry 1")
            : Format($": entry {found} follows entry {expected - 1}");
        return new ChainBreak(expected, ChainBreakKind.MissingEntries, $"{range}{context}. They were deleted from the database.");
    }

    /// <summary>An entry does not link to the entry before it.</summary>
    /// <param name="sequence">The entry's sequence number.</param>
    /// <returns>The break.</returns>
    public static ChainBreak Unlinked(long sequence) => new(
        sequence,
        ChainBreakKind.BrokenLink,
        sequence == 1
            ? "Entry 1 does not start from the genesis hash: the start of the chain was replaced."
            : Format($"Entry {sequence} does not carry the hash of entry {sequence - 1}: entries were re-ordered, or one was replaced."));

    /// <summary>An entry's contents do not match its hash.</summary>
    /// <param name="sequence">The entry's sequence number.</param>
    /// <returns>The break.</returns>
    public static ChainBreak Edited(long sequence) => new(
        sequence,
        ChainBreakKind.EditedEntry,
        Format($"Entry {sequence} does not match its hash: its contents were changed after it was written."));

    private static string Format(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);
}

/// <summary>The outcome of a chain verification.</summary>
/// <param name="EntriesChecked">How many entries were checked (up to and including a broken one).</param>
/// <param name="HeadSequence">The sequence number of the newest entry when the check started; 0 for an empty trail.</param>
/// <param name="HeadHash">The hash of that entry, or <see langword="null"/> for an empty trail.</param>
/// <param name="FirstBreak">The first break, or <see langword="null"/> when the chain is intact.</param>
public sealed record ChainVerification(long EntriesChecked, long HeadSequence, string? HeadHash, ChainBreak? FirstBreak)
{
    /// <summary>Gets a value indicating whether every entry checked out.</summary>
    public bool IsIntact => FirstBreak is null;

    /// <summary>Describes the outcome in one or two sentences, for the auditor and the <c>ChainVerified</c> entry.</summary>
    /// <returns>The description.</returns>
    public string Describe()
    {
        var head = HeadHash is null
            ? "The audit trail is empty."
            : string.Create(CultureInfo.InvariantCulture, $"The newest entry is {HeadSequence}, with hash {HeadHash}.");
        return FirstBreak is null
            ? string.Create(CultureInfo.InvariantCulture, $"The audit chain is intact: {EntriesChecked} entries checked. {head}")
            : string.Create(
                CultureInfo.InvariantCulture,
                $"The audit chain is broken at entry {FirstBreak.Sequence}. {FirstBreak.Explanation} {EntriesChecked} entries checked. {head}");
    }
}
