namespace RegReturns.Application.Auditing;

/// <summary>Appends entries to the tamper-evident audit chain.</summary>
public interface IAuditTrail
{
    /// <summary>Appends one entry in its own transaction.</summary>
    /// <param name="record">What to record.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The sequence number of the new entry.</returns>
    Task<long> RecordAsync(AuditRecord record, CancellationToken cancellationToken);
}
