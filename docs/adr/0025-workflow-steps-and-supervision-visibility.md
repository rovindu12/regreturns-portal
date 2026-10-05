# 25. Workflow steps through one command; supervisors see returns once submitted

- Status: accepted
- Date: 2026-10-05

## Context

Phase 4 adds the submission workflow to the portal: a bank checker submits, a supervisor reviewer picks the return up,
a reviewer or approver sends it back for correction, and an approver who did not review it approves or rejects it.
The rules (state, role, organisation, segregation of duties, comments) already live on `Submission`. The portal must
offer each user only the steps they can take, the API (phase 5) must apply exactly the same rules, and supervisors
need a worklist without seeing the banks' unfinished work.

## Decision

- **One command for every step.** `TransitionReturn(SubmissionId, Action, Comment)` loads the return the caller may
  see, calls the matching domain method and saves. Every step is logged (5301, or 5302 with the error code when
  refused), counted (`regreturns.workflow.transitions` by action and outcome, `regreturns.workflow.late_submissions`)
  and traced (`returns.transition` span). The audit trail records the change itself (ADR 0024).
- **The domain says what is offered.** `Submission.Permits(action, actor)` checks state, role, organisation and
  segregation of duties; `ActionsFor(actor)` lists the steps that pass. The domain methods use the same check, so a
  button the page shows is a step the command accepts. Data rules for submitting (values, a current validation, no
  errors, every warning justified) and the comment are checked by the step itself, so the page can explain them.
- **TOTP stays a host policy.** Approve and reject endpoints require `Supervision.Approve` (approver role plus a TOTP
  sign-in when `Iam:EnforceMfa` is on). The review page checks the same policy and, when it fails, asks the approver to
  sign in again instead of showing buttons that would answer 403.
- **Visibility.** Bank staff see their own bank's returns, drafts included. Regulator staff see any bank's return once
  it has been submitted (`FirstSubmittedAt` is set), including while it is back with the bank. Anything else answers
  404, so ids reveal nothing.
- **No separate "recommended" state.** The approved plan's state machine is kept: a return under review can be
  decided by any approver who did not review it. The worklist shows who is reviewing each return.

## Consequences

- The web and API hosts stay thin: they map a route to an action and a policy, and show `Error.Message` when a step
  is refused.
- A supervisor cannot comment on a draft or see a bank's work in progress; banks decide when the regulator sees it.
- Adding a step means a domain method, a row in `SubmissionWorkflow`, a branch in `Permits` and a case in
  `TransitionReturn`; the transition matrix and permission tests list every state and action, so a missing row fails.
