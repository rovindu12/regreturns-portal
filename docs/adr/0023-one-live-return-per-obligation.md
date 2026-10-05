# 23. One live return per obligation, saved with optimistic concurrency

- Status: accepted
- Date: 2026-10-05

## Context

A return can be started from the web form, an upload or (later) the API, sometimes by two makers at once. Two drafts
for one obligation would split the bank's work and confuse supervisors, and two makers saving the same draft must not
silently overwrite each other.

## Decision

- A filtered unique index (`UX_Submissions_LiveObligation`) allows one submission per obligation whose status is not
  `Rejected`. Starting a return opens the live one if it exists; a race that loses on the index opens the winner's.
- The entry form carries the submission's `EditVersion`. A save from an older version is refused with
  `Submission.EditConflict`; the page keeps the user's entries and explains that saving again replaces the other
  change. A `rowversion` on the submission catches the remaining race between reading and writing
  (`Common.Conflict`).
- Saving a value that did not change is not an edit: it does not bump `EditVersion`, invalidate the validation or
  change the last editor (which matters for the checker-is-not-the-editor rule).

## Consequences

- No locks or check-outs; conflicts are rare and visible when they happen.
- A rejected return frees the obligation for a new submission; the rejected one stays as history.
