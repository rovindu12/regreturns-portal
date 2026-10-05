# 26. Bank systems deliver returns through the API as makers; people submit them

- Status: accepted
- Date: 2026-10-05

## Context

Phase 5 opens the REST API to bank systems (client-credentials tokens, one WSO2 application per bank, ADR 0018).
The plan's `POST /v1/submissions` lets a bank system send a return. The workflow rules (ADR 0025) say a checker
submits and must not be the person who prepared or last edited the values. A machine client has no person behind
it, and every user column on a return (`PreparedByUserId`, `LastEditedByUserId`, ...) references `iam.Users`.

Letting the API submit straight to the regulator would either drop the four-eyes rule for API returns or need a
second machine identity to play the checker, which only moves the problem.

## Decision

- **The API is another maker channel, like the upload.** `POST /v1/submissions` names the return type and period,
  and carries every value of the return. It opens the obligation's live return (or starts a draft with the template
  version that applies, ADR 0009), replaces its values, validates them with the same engine and saves, with
  `Source = Api`. Validation findings are part of the answer, not a refusal: the draft is kept, as in the portal.
  A bank checker reviews and submits it in the portal, so segregation of duties holds for every channel.
- **Each API client acts through a client user.** An `AppUser` with `ApiClientId` set represents one registered
  `ApiClient`. It holds the bank maker role only (never checker), has no e-mail and no WSO2 user, and sign-in refuses
  to link a person to it. IamBootstrap creates it with the client's `iam.ApiClients` row. In the API,
  `ICurrentActor` resolves the caller from the token's client id to that user, so the API runs the same use cases,
  institution scoping and domain rules as the portal. A client without one is answered 403 (`User.NotLinked`).
- **Reads use the same visibility.** A bank system sees its own bank's returns, drafts included; another bank's
  ids answer 404, the same as unknown ids (ADR 0018).
- **The scope keeps its name.** `returns:submit` means "deliver returns", which is what a bank system does; the
  regulator receives a return when a checker submits it.

## Consequences

- The portal shows API drafts like any other, attributed to the client user ("RegReturns Bank HLB"); the audit trail
  records the API client as the actor (ADR 0024).
- A bank that wants straight-through submission needs a later decision: for example, a checker approval recorded
  in the bank's own system and presented as a signed assertion. That is backlog, not phase 5.
- Client users must be left out of user administration (phase 10) and cannot sign in to the portal.
- Justifying warnings stays in the portal for now; the API reports which warnings still need one.
