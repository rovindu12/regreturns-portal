# 24. Every saved data change joins the audit chain in the same transaction

- Status: accepted
- Date: 2026-10-05

## Context

ADR 0016 built the hash-chained audit trail for sign-in, sign-out and refused access. Phase 4 adds what a regulator's
auditors care about most: who changed a return, a template, a user or a bank, what the values were before and after,
and proof that nobody has edited that record since. Writing an audit call into every use case would be easy to forget
and would record what the code meant to change rather than what reached the database. An audit row written in a
separate transaction could also exist for a change that rolled back, or be missing for one that committed.

## Decision

- **The context audits its own saves.** In the hosts, `RegReturnsDbContext` is built with an `IDataChangeAuditor`
  (registered by `AddAuditTrail`). Its `SaveChanges` and `SaveChangesAsync` collect the pending changes and save them
  with their audit entries in one transaction: begin (or join the caller's transaction), take the chain lock
  (`sp_getapplock`, shared with `AuditTrail`), seal the entries after the head, save, commit. The whole step runs in
  the execution strategy, so a transient failure retries it from the start and seals afresh after the head it then
  finds. A save that fails or a transaction that rolls back leaves no entry; the entries are never left tracked.
- **One entry per aggregate root.** `DataChangeCollector` groups changed rows by aggregate root using the EF Core
  model: an entity reached from its principal through a collection navigation (or owned by it) is a child. The entry's
  `EntityType` and `EntityId` name the root; the action is `Created`, `Deleted`, `StateChanged` (when the root's
  `Status` changes, with "Status changed from Draft to Submitted." in `Details`) or `Updated`.
- **Before and after values in the entry.** The new `Changes` column holds a compact JSON document: the root first,
  then its child rows by readable path (`Values[TOTAL_HQLA]`, `Fields[TOTAL_DEPOSITS]`, by the unique index that pairs
  the parent key with one text property, or by id otherwise), each with `Added`, `Modified` or `Deleted` and only the
  values that changed. Values are written culture-invariantly (ISO dates, plain decimals, enum names).
- **What is not recorded.** Keys and parent keys, concurrency tokens, `AuditEntry` itself, and properties marked
  `[NotAudited]`: `AppUser.Email` (personal contact data; WSO2 holds its history) and `StoredFile.Content` (the size
  and SHA-256 identify the file). A save that changes only such properties writes no entry.
- **Who acted.** Each host implements `IAuditContext`: the portal takes the signed-in user, IP address and trace id
  from the request, and during sign-in names the user from the validated id token (`PortalAuditContext.ActAs`) so the
  first link of their record is theirs; the API takes the calling client or user. Outside a request the actor is
  `System`. The migrator, seeding, design time and test helpers build the context without an auditor, so seeding a
  database writes no entries.
- **Hash compatibility.** `Changes` joins the canonical form after `PreviousHash` only when it is not null, so every
  entry written before this change keeps its hash and the chain verifies across the upgrade.
- **Verification.** `AuditChainVerifier` walks the chain in batches of 500 up to the head present when it starts and
  reports the first break: missing sequence numbers (deleted entries), an entry that does not carry the previous hash
  (re-ordered or replaced entries; the link is checked first so a moved entry does not read as edited), or an entry
  whose hash does not match (edited). It logs 3003 with the head sequence and hash when intact, 3004 with the break.
  The auditor's screen runs it on demand and the run is itself recorded as a `ChainVerified` entry.

## Consequences

- Use cases stay free of audit calls, and the trail records what was committed, not what was intended. A new entity
  is audited from its first save; a property that must not be recorded needs `[NotAudited]` and a unit test.
- Every audited save in the application is serialised on the chain lock, like audit appends already were (ADR 0016).
  That is acceptable at a regulator's volumes; a high-volume system would append asynchronously from an outbox.
- Deleting entries from the end of the chain leaves no gap and no broken link, so the verifier cannot see it alone.
  The head sequence and hash that each verification logs (3003) and records in its `ChainVerified` entry are the
  anchor: a later head older than a logged one reveals the loss. Shipping those to write-once storage is backlog.
- Changes made outside EF Core (raw SQL, a DBA in the database) are not audited; the append-only trigger and the
  chain only protect the trail itself.
- The audit trail page shows each entry's change document; it is for auditors and administrators only.
