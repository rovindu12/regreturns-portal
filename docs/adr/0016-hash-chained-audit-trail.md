# 16. Tamper-evident audit trail as an HMAC hash chain

- Status: accepted
- Date: 2026-10-04

## Context

A regulator's audit trail must show who did what and when, and must make tampering detectable, including by someone
with write access to the database. Auth events (sign-in, sign-out, denied access) start in phase 2; workflow and
data changes join in phase 4.

## Decision

- `audit.AuditEntries` is append-only. Each entry has a gap-free `Sequence`, the hash of the previous entry and its own
  hash: HMAC-SHA256 over a fixed, culture-invariant canonical form of every field plus the previous hash.
- The HMAC key (`Audit:HmacKey`, at least 32 random bytes) lives only in user-secrets or the environment, never in the
  database, so database access alone cannot re-forge the chain. The app refuses to start without a valid key.
- Appends run in their own transaction on a dedicated `DbContext`, serialised with `sp_getapplock`, so concurrent
  requests cannot fork the chain and a caller's unsaved changes are never saved by the audit write.
- An `INSTEAD OF UPDATE, DELETE` trigger rejects changes to stored entries (error 51001). EF Core is told about the
  trigger so it does not use `OUTPUT` clauses.
- Access-denied and authentication-failed events are de-duplicated per actor, path and minute so a misbehaving client
  cannot flood the trail; a failed audit write is logged and never turns a 403 into a 500.

## Consequences

Editing, deleting or re-ordering an entry breaks verification from that point on, which the auditor's verify screen
(phase 4) reports. The trigger stops casual edits; a DBA can still disable it, but cannot produce valid hashes without
the key. Appends are serialised, which is acceptable at a regulator's volumes; a high-volume system would batch them.
Rotating the HMAC key needs a "key changed" entry and keeping the old key for verification (backlog).
