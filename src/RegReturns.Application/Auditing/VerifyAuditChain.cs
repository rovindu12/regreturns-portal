using System.Diagnostics.CodeAnalysis;

using RegReturns.Application.Messaging;
using RegReturns.Domain.Auditing;

namespace RegReturns.Application.Auditing;

/// <summary>Verifies the audit hash chain and records who verified it and the outcome.</summary>
[SuppressMessage(
    "Major Code Smell",
    "S2094:Classes should not be empty",
    Justification = "A command without inputs: the handler interface needs a type to dispatch on.")]
public sealed record VerifyAuditChain;

/// <summary>
/// Handles <see cref="VerifyAuditChain"/>: walks the chain, then appends a <see cref="AuditAction.ChainVerified"/> entry
/// naming the caller and the outcome, including the head sequence and hash that were checked.
/// </summary>
/// <param name="verifier">Walks the chain.</param>
/// <param name="auditTrail">Records the verification.</param>
/// <param name="auditContext">The caller.</param>
public sealed class VerifyAuditChainHandler(IAuditChainVerifier verifier, IAuditTrail auditTrail, IAuditContext auditContext)
    : ICommandHandler<VerifyAuditChain, ChainVerification>
{
    /// <inheritdoc />
    public async Task<ChainVerification> HandleAsync(VerifyAuditChain command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var result = await verifier.VerifyAsync(cancellationToken);
        await auditTrail.RecordAsync(auditContext.Current.ToRecord(AuditAction.ChainVerified, result.Describe()), cancellationToken);
        return result;
    }
}
