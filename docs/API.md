# RegReturns API for bank systems

How a bank's own systems read reference data and their returns, and deliver returns to the Bank of Valoria, through
the RegReturns REST API. The conventions are decided in [ADR 0027](adr/0027-web-api-v1-conventions.md), the delivery
model in [ADR 0026](adr/0026-api-delivers-drafts-through-a-client-user.md) and token validation in
[ADR 0018](adr/0018-api-token-validation-and-institution-scoping.md). The code is in
[src/RegReturns.Api](../src/RegReturns.Api).

## Overview

The API is for the systems of licensed banks (Harbourline Bank PLC, Crestmont Commercial Bank and the other banks) that
produce regulatory returns. It is a **maker channel**, like the upload in the portal. A system delivers a complete
return; RegReturns stores it as a draft of the bank (`source` `Api`) and validates it. A bank user then justifies any
warnings and a bank checker submits the return to the regulator in the portal. The API never submits a return itself,
so the four-eyes rule holds for every channel. Each client acts for exactly one bank and sees only that bank's data.

| | Local development | Hosted |
|---|---|---|
| API | `https://localhost:7201` | `https://<API_HOST>` |
| Swagger UI | `https://localhost:7201/swagger` | `https://<API_HOST>/swagger` |
| OpenAPI document | `https://localhost:7201/openapi/v1.json` | `https://<API_HOST>/openapi/v1.json` |
| Token endpoint (WSO2) | `https://localhost:9443/oauth2/token` | `https://<IAM_HOST>/oauth2/token` |

The OpenAPI document and Swagger UI need no token; every operation does. *Authorize* in Swagger UI uses client
credentials and is pre-filled with the read-only demo client `regreturns-demo-api`, which acts for Harbourline. Its
secret is `DEMO_API_CLIENT_SECRET` in `.env.generated`, and is shown on `/demo` when demo mode is on
([DEMO.md](DEMO.md)). Only that client lets the browser ask WSO2 for a token from the API's origin, so try deliveries
from a script with a bank client. `/health/live` and `/health/ready` need no token either.

## Authentication

Clients use the OAuth 2.0 client credentials grant at WSO2's token endpoint. IamBootstrap creates one confidential
client per active bank, `regreturns-bank-<code>` (for example `regreturns-bank-hlb`), and records it in RegReturns'
registry of API clients. Locally the id and secret are `BANK_HLB_CLIENT_ID` and `BANK_HLB_CLIENT_SECRET` in
`.env.generated` (on a server, `generated/.env.generated`). How clients are created and registered is in
[IAM.md](IAM.md).

| Scope | Allows | Endpoints |
|---|---|---|
| `reference:read` | Reference data: the caller, its institution, return types and templates | `GET /v1/me`, `/v1/institutions/{code}`, `/v1/return-types`, `/v1/return-types/{code}/template` |
| `returns:read` | Your bank's returns, drafts included, and their findings | `GET /v1/submissions`, `/v1/submissions/{id}`, `/v1/submissions/{id}/validation` |
| `returns:submit` | Delivering returns for your bank, as drafts | `POST /v1/submissions` |

Bank clients are authorised for all three scopes; the demo client only for the two read scopes. Ask for the scopes you
need in the token request; a token without the scope of an endpoint gets 403.

```bash
WSO2=https://localhost:9443
CLIENT_ID=regreturns-bank-hlb
CLIENT_SECRET="$(grep -E '^BANK_HLB_CLIENT_SECRET=' .env.generated | cut -d= -f2-)"   # read, never typed
TOKEN="$(curl -sS --cacert .certs/regreturns-dev-ca.crt \
  -K <(printf 'user = "%s:%s"\n' "$CLIENT_ID" "$CLIENT_SECRET") \
  --data-urlencode grant_type=client_credentials \
  --data-urlencode 'scope=reference:read returns:read returns:submit' \
  "$WSO2/oauth2/token" | jq -r .access_token)"
```

- `-K <(printf ...)` hands curl the credentials (HTTP Basic) through a pipe. `printf` is a shell builtin, so the secret
  never appears in the process list. The token reaches the API the same way: `-H @<(printf 'Authorization: Bearer %s\n'
  "$TOKEN")`. This is the repository's rule for every script.
- Locally WSO2's certificate is issued by the development CA (`scripts/dev-certs.sh`), hence `--cacert`. The hosted
  services have public certificates: leave `--cacert` out there.
- The API accepts only access tokens from WSO2: a JWT of type `at+jwt`, signed with RS256, issued by
  `<WSO2>/oauth2/token` for the audience `https://api.regreturns`, with 30 seconds of clock skew. IamBootstrap sets the
  lifetime of client tokens to 300 seconds. Reuse a token until it expires, then ask for a new one.
- A refused token gets 401 with a `WWW-Authenticate: Bearer` challenge and no reason. The reason goes to the API's log
  and the audit trail.

**Institution scoping.** The bank comes from the registry, looked up by the token's client id (`azp`, else
`client_id`), never from a claim in the token: an `institution_id` claim in a token is removed. An unknown or inactive
client, or one whose bank is inactive, gets 403 on every endpoint. Lookups are cached for 60 seconds, so deactivating a
client takes effect within a minute. Another bank's institution code or return id gets the same 404 as one that does
not exist.

Each client acts through its own **client user**, which holds the bank maker role only and cannot sign in to the
portal. Drafts the client starts show that user as the preparer, and the audit trail records the client id as the
actor. A registered client without an active client user gets 403 `User.NotLinked` on the returns endpoints.

## Conventions

**Versions and content.** The version is a URL segment (`/v1/...`) and responses carry `api-supported-versions: 1.0`.
Requests and responses are JSON with camelCase names and enumerations as strings (`"Draft"`, `"Monthly"`). A `POST`
sends `Content-Type: application/json` and a body of at most 1 MiB (413 `Request.TooLarge` beyond).

**Trace ids.** Every response has an `X-Trace-Id` header with the request's W3C trace id. Quote it when you report a
problem; the operators find the request with it.

**Errors.** Every error is `application/problem+json` (RFC 9457) with the `status`, the `instance` (the path) and the
`traceId`. A refusal also explains itself in `detail` and carries a stable `code`: branch on `code`, never on
`detail`. A 400 `Request.Invalid` also has `errors`, keyed by parameter or field. 401 and 403 answers from the token
and scope checks carry no `code`. The statuses and codes are listed under [Errors](#errors).

```json
{ "status": 409, "detail": "The submission can only be changed while it is a draft or returned for correction.",
  "instance": "/v1/submissions", "traceId": "4bf92f3577b34da6a3ce929d0e0e4736", "code": "Submission.NotEditable" }
```

**Paging.** Lists take `page` (from 1, default 1) and `pageSize` (1 to 100, default 25); a value out of range is 400
`Request.Invalid`. The body has `items`, `page`, `pageSize`, `totalCount` and `totalPages` (0 for an empty list). The
`Link` header (RFC 8288) always has `first` and `last`, `prev` from page 2 and `next` while a later page exists. Its
targets are relative references that keep your other query parameters, with `page` and `pageSize` last:

```
Link: </v1/submissions?returnType=MLR&page=1&pageSize=5>; rel="first", </v1/submissions?returnType=MLR&page=1&pageSize=5>; rel="prev", </v1/submissions?returnType=MLR&page=3&pageSize=5>; rel="next", </v1/submissions?returnType=MLR&page=6&pageSize=5>; rel="last"
```

**Periods, dates and values.**
- A period is a month (`2026-09`) or a quarter (`2026-Q3`) and must match the return type's frequency: `MLR` and `MDA`
  are monthly, `QCAR` quarterly.
- Dates such as `dueDate` and `effectiveFrom` are `yyyy-MM-dd`. Timestamps such as `createdAt` are ISO 8601 with the
  UTC offset, for example `2026-10-06T09:14:22.481+00:00`.
- Values are keyed by field code (case-sensitive) and are a JSON string, number, `true`, `false` or `null`. A number is
  kept exactly as written, never through binary floating point: `159390.00` is stored as `159390.00`. Read back, every
  value is a string or `null`.
- Values are parsed as in the portal: a full stop for decimals, optionally a comma between groups of three digits, no
  exponent, and at most the field's `precision` decimal places. A percentage may end in `%`; dates are `yyyy-MM-dd`;
  booleans are true/false or yes/no. A field's `unit` says what a number means; amounts in the seeded templates are
  `VLD m`, millions of VLD. A value that does not parse is a `DataType` finding, not a refusal. A value longer than
  400 characters is refused (422 `Submission.ValueTooLong`).

**Rate limits.** Each client has a fixed window: 120 requests per 60 seconds by default (`Api:RateLimit:PermitLimit`
and `Api:RateLimit:WindowSeconds`). Callers without a valid token share a window per IP address. Every call to an
endpoint counts, refused ones included; the health checks, Swagger UI and the OpenAPI document do not. Over the limit
the answer is 429 with `Retry-After` (seconds) and code `RateLimit.Exceeded`. Requests are not queued.

**Idempotency.** Every `POST` needs an `Idempotency-Key` header: one value of 1 to 255 visible ASCII characters (no
spaces), such as a UUID. Without it the answer is 400 `Idempotency.KeyRequired`; a malformed one is 400
`Idempotency.KeyInvalid`.

- Keys belong to the client, so two banks can use the same key. A key is kept for 24 hours
  (`Api:Idempotency:RetentionHours`); after that it is free again.
- The first request with a key is recorded with a fingerprint: SHA-256 over the method, the path with its query string
  and the body bytes **exactly as sent**. A retry must send the same bytes; JSON serialised again with other spacing or
  key order is another request.
- A retry with the same key and request gets the stored answer (status, body and `Location`) with
  `Idempotent-Replayed: true`, and nothing runs again.
- The same key with another request is 422 `Idempotency.KeyReused`.
- A retry while the first request is still running is 409 `Idempotency.InProgress` with `Retry-After: 2`. If the first
  request stopped without an answer (the process ended), a retry runs it again after 60 seconds
  (`Api:Idempotency:InFlightSeconds`).
- Answers of 403, 409, 429 and 5xx are not stored: they release the key, so a retry with the same key runs afresh.
  Every other answer of the request, a 400 or 422 included, is stored and replayed.

Use a new key for each new delivery and reuse it only to retry the identical request.

## Endpoints

| Method | Path | Scope | What it does |
|---|---|---|---|
| `GET` | `/v1/me` | `reference:read` | The client id, its institution and the token's scopes |
| `GET` | `/v1/institutions/{code}` | `reference:read` | Your own institution; any other code is 404 |
| `GET` | `/v1/return-types` | `reference:read` | The return types banks file, with their published template versions |
| `GET` | `/v1/return-types/{code}/template` | `reference:read` | The fields and rules of the template in force for a period |
| `GET` | `/v1/submissions` | `returns:read` | Your bank's returns, newest first, one page at a time |
| `GET` | `/v1/submissions/{id}` | `returns:read` | One return with its values and workflow history |
| `GET` | `/v1/submissions/{id}/validation` | `returns:read` | The findings of a return's current revision |
| `POST` | `/v1/submissions` | `returns:submit` | Delivers a complete return as a draft and validates it |

The examples run locally with this helper. It trusts the ASP.NET Core development certificate and keeps the token off
the command line:

```bash
API=https://localhost:7201
APP_CA="${TMPDIR:-/tmp}/aspnet-dev.pem"
dotnet dev-certs https --export-path "$APP_CA" --format PEM     # the certificate only, never its key
api() { curl -sS --cacert "$APP_CA" -H @<(printf 'Authorization: Bearer %s\n' "$TOKEN") "$@"; }
```

### GET /v1/me

Checks a client's set-up end to end; `scopes` are sorted. `api "$API/v1/me"` answers:

```json
{ "clientId": "regreturns-bank-hlb", "institution": { "code": "HLB", "name": "Harbourline Bank PLC", "type": "Commercial" },
  "scopes": ["reference:read", "returns:read", "returns:submit"] }
```

### GET /v1/institutions/{code}

Returns your own institution; the code is case-insensitive. `type` is the licence category: `Commercial`, `Savings` or
`Development`. Any other code, another bank's included, gets the same 404 problem (without a `code`), so the API never
reveals which institutions exist.

```bash
api "$API/v1/institutions/hlb"      # {"code":"HLB","name":"Harbourline Bank PLC","type":"Commercial"}
```

### GET /v1/return-types

The active return types by code (`MDA`, `MLR`, `QCAR`), each with its published template versions, newest first.
`api "$API/v1/return-types"` answers (one item shown):

```json
[
  { "code": "MLR", "name": "Monthly Liquidity Return", "description": "High-quality liquid assets, 30-day stressed cash flows, ...",
    "frequency": "Monthly", "dueDaysAfterPeriodEnd": 15, "versions": [{ "version": 1, "effectiveFrom": "2024-01-01" }] }
]
```

### GET /v1/return-types/{code}/template

The template a return for `period` is filed with: the published version whose `effectiveFrom` is the latest on or
before the period's start. Without `period` it is the template for the current period. `fields` are in display order;
`rules` are the active rules, by field and then code. A rule's `type` is `Required`, `DataType`, `Range`, `CrossField`
or `Variance`; its `severity` is `Error` (blocks submission) or `Warning` (needs a justification). Variance rules
compare with your bank's last approved return for the previous period or the same period last year.
`api "$API/v1/return-types/MLR/template?period=2026-09"` answers (trimmed):

```json
{
  "returnType": "MLR", "version": 1, "effectiveFrom": "2024-01-01", "period": "2026-09",
  "fields": [
    { "code": "L1_HQLA", "label": "Level 1 HQLA", "section": "High-quality liquid assets", "dataType": "Amount",
      "unit": "VLD m", "precision": 2, "required": true },
    { "code": "LCR", "label": "Liquidity coverage ratio", "section": "Ratios", "dataType": "Percentage",
      "unit": "%", "precision": 2, "required": true }
  ],
  "rules": [
    { "code": "MLR_L2B_CAP", "type": "CrossField", "severity": "Warning", "field": "L2B_HQLA",
      "message": "Level 2B assets exceed the 15% cap of Total HQLA." }
  ]
}
```

A malformed period is 400 `Request.Invalid`, an unknown code 404 `ReturnType.NotFound`, a quarter for a monthly return
422 `ReturnType.PeriodMismatch`, and a period before any published version 422 `Template.NoApplicableVersion`.

### GET /v1/submissions

Your bank's returns from every channel, drafts included, newest first. Optional filters: `status` (`Draft`,
`Submitted`, `UnderReview`, `ReturnedForCorrection`, `Approved` or `Rejected`, any case, never a number),
`returnType` and `period`. `source` is `Web`, `Upload`, `Api` or `Migration`; `validation` counts the current
revision's findings. `api "$API/v1/submissions?returnType=MLR&period=2026-09"` answers:

```json
{
  "items": [
    { "id": "0199b2c4-6f1e-7a3b-9c55-2d8e4f1a7b30", "returnType": "MLR", "period": "2026-09", "dueDate": "2026-10-15",
      "templateVersion": 1, "status": "Draft", "revision": 1, "source": "Api", "isLate": false,
      "createdAt": "2026-10-06T09:14:22.481+00:00", "lastEditedAt": "2026-10-06T09:14:22.481+00:00",
      "firstSubmittedAt": null, "decidedAt": null,
      "validation": { "isValidated": true, "errors": 0, "warnings": 1, "unjustifiedWarnings": 1 } }
  ],
  "page": 1, "pageSize": 25, "totalCount": 1, "totalPages": 1
}
```

### GET /v1/submissions/{id}

One return: `submission` (as in the list), `editVersion` (goes up with every change of values), `values` (every field
of its template, `null` when blank) and `history`, oldest first. A step's `action` is `Create`, `Submit`,
`StartReview`, `ReturnForCorrection`, `Approve`, `Reject` or, for a return migrated from the legacy system, `Migrate`;
its `comment` says, for example, why the regulator returned it. Who took a step is left out. An unknown id and another
bank's id are both 404 `Submission.NotFound`. Trimmed:

```json
{
  "submission": { "id": "0199b2c4-6f1e-7a3b-9c55-2d8e4f1a7b30", "returnType": "MLR", "period": "2026-09", "status": "Draft" },
  "editVersion": 1,
  "values": { "L1_HQLA": "159390.00", "L2A_HQLA": "36225.00", "L2B_HQLA": "38500.00", "LCR": "248.57" },
  "history": [
    { "revision": 1, "action": "Create", "fromStatus": null, "toStatus": "Draft", "comment": null,
      "occurredAt": "2026-10-06T09:14:22.481+00:00" }
  ]
}
```

### GET /v1/submissions/{id}/validation

The findings of the current revision, in field order, then by rule type and code. `isValidated` is false when the
values changed after the last validation. `readyToSubmit` is true when a bank checker could submit the return as it
stands: still with the bank, validated, no errors and every warning justified. `justification` is the bank's text for a
warning, or `null`. The body is the same as `validation` in the delivery answer below.

### POST /v1/submissions

Delivers a complete return for one period. `returnType` is a code of 1 to 10 letters and digits (any case), `period` a
month or quarter, and `values` holds at most 500 values (`{}` is allowed).

```json
{
  "returnType": "MLR", "period": "2026-09",
  "values": {
    "L1_HQLA": 159390.00, "L2A_HQLA": 36225.00, "L2B_HQLA": 38500.00, "TOTAL_HQLA": 234115.00,
    "CASH_OUTFLOWS_30D": 130410.00, "CASH_INFLOWS_30D": 36225.00, "NET_CASH_OUTFLOWS": 94185.00,
    "LCR": 248.57, "TOTAL_DEPOSITS": 724500.00, "LIQUID_ASSETS": 217350.00, "LIQUID_ASSETS_RATIO": 30.00
  }
}
```

1. It finds your bank's obligation for the return type and period.
2. It opens the obligation's live return (the newest one not rejected) or, if there is none, starts a draft with the
   template version in force for the period.
3. It replaces **all** the values: a field you leave out becomes blank, so stale figures never survive. A code that
   is not a field of the template refuses the whole delivery (422 `Delivery.UnknownFields`, with up to ten codes in
   `detail`) and nothing is stored.
4. It validates with the same engine as the portal and saves.

A new draft answers **201 Created** with `Location: https://localhost:7201/v1/submissions/{id}`; an update of the open
return answers **200 OK**. `changed` is false when the values were already the same. Validation findings are part of a
successful answer, not a refusal: the draft is kept. Errors block submission (fix the figures and deliver again);
warnings need a justification from a bank user in the portal.

```json
{
  "submissionId": "0199b2c4-6f1e-7a3b-9c55-2d8e4f1a7b30", "created": true, "changed": true, "status": "Draft", "editVersion": 1,
  "validation": {
    "submissionId": "0199b2c4-6f1e-7a3b-9c55-2d8e4f1a7b30", "revision": 1, "isValidated": true, "readyToSubmit": false,
    "errors": 0, "warnings": 1, "unjustifiedWarnings": 1,
    "findings": [
      { "rule": "MLR_L2B_CAP", "field": "L2B_HQLA", "severity": "Warning",
        "message": "Level 2B assets exceed the 15% cap of Total HQLA.", "justification": null }
    ]
  }
}
```

You can deliver while the return is `Draft` or `ReturnedForCorrection`. Once a checker has submitted it, and while it
is under review or approved, a delivery is 409 `Submission.NotEditable`. After the regulator rejects a return, the
next delivery starts a new draft. Other refusals: an unknown return type (404 `ReturnType.NotFound`), a period of the
wrong frequency (422 `ReturnType.PeriodMismatch`), a period your bank owes nothing for (422 `Delivery.NoObligation`), a
value that is an object or an array (400 `Request.Invalid`, `errors` names it, such as `values.LCR`) and a delivery
racing another save of the same return (409 `Delivery.Concurrent`).

## Errors

| Status | Meaning | Codes |
|---|---|---|
| 400 | The request is malformed: query, body or idempotency key | `Request.Invalid`, `Idempotency.KeyRequired`, `Idempotency.KeyInvalid` |
| 401 | No token, or one that is expired, wrongly signed, from another issuer or for another audience | none |
| 403 | The caller is the problem: a scope is missing, the client or its bank is unknown or inactive, or its client user is missing | none, or `User.NotLinked` |
| 404 | Not found, or not yours (they look the same) | `ReturnType.NotFound`, `Submission.NotFound`; none for institutions |
| 409 | The request is fine, but the state of the return or of an earlier request does not allow it now | `Submission.NotEditable`, `Submission.ObligationClosed`, `Delivery.Concurrent`, `Idempotency.InProgress` |
| 413 | The body is over 1 MiB | `Request.TooLarge` |
| 422 | The content breaks a rule | `ReturnType.PeriodMismatch`, `Delivery.NoObligation`, `Delivery.UnknownFields`, `Template.NoApplicableVersion`, `Submission.ValueTooLong`, `Idempotency.KeyReused` |
| 429 | Too many requests in the client's window | `RateLimit.Exceeded` |
| 500 | An unexpected error; `detail` quotes the trace id | none |

| Code | Meaning |
|---|---|
| `Request.Invalid` | A parameter or the body failed validation: period, status, page, page size, return type code, a missing member, more than 500 values, or a value that is an object or array |
| `Request.TooLarge` | The request body is larger than 1 MiB |
| `Idempotency.KeyRequired` | A `POST` without an `Idempotency-Key` header |
| `Idempotency.KeyInvalid` | The key is empty, longer than 255 characters, not visible ASCII, or sent twice |
| `Idempotency.KeyReused` | The key was used in the last 24 hours for a different method, path or body |
| `Idempotency.InProgress` | The first request with this key is still running; retry after `Retry-After` |
| `User.NotLinked` | The client is registered but has no active client user |
| `ReturnType.NotFound` | No active return type has this code |
| `ReturnType.PeriodMismatch` | The period's frequency is not the return type's; `detail` gives an example period |
| `Template.NoApplicableVersion` | No published template applies to the period |
| `Delivery.NoObligation` | Your bank has no filing obligation for the return type and period |
| `Delivery.UnknownFields` | Some values are keyed by codes that are not fields of the template; `detail` lists them |
| `Delivery.Concurrent` | The return changed while the delivery was being saved; send it again |
| `Submission.NotEditable` | The return can only change while it is a draft or returned for correction |
| `Submission.ObligationClosed` | A return for the period is already approved; no new draft can be started |
| `Submission.ValueTooLong` | A value is longer than 400 characters |
| `Submission.NotFound` | No return of your bank has this id |
| `RateLimit.Exceeded` | The client made more requests than its window allows; wait `Retry-After` seconds |

## Walkthrough

An end-to-end session against a local stack, as Harbourline's system. The examples use September 2026, the last
completed month when this was written; use yours. The demo seed leaves Harbourline's MLR for the last completed month
unfiled, so the first delivery starts a draft.

```bash
# 1. Get a token for Harbourline's bank client with the block in "Authentication", then set API, APP_CA and api()
#    as in "Endpoints". Check who you are:
api "$API/v1/me" | jq -c '{clientId, bank: .institution.code, scopes}'

# 2. The field codes of the MLR template for the period.
api "$API/v1/return-types/MLR/template?period=2026-09" | jq -r '.fields[] | "\(.code)\t\(.dataType)\t\(.unit)"'

# 3. Has anything been filed for the period yet?
api "$API/v1/submissions?returnType=MLR&period=2026-09" | jq '.totalCount'      # 0

# 4. Deliver the return with a new key. Keep the body in a file so a retry sends the same bytes.
cat > mlr-2026-09.json <<'JSON'
{"returnType":"MLR","period":"2026-09","values":{"L1_HQLA":159390.00,"L2A_HQLA":36225.00,"L2B_HQLA":38500.00,
"TOTAL_HQLA":234115.00,"CASH_OUTFLOWS_30D":130410.00,"CASH_INFLOWS_30D":36225.00,"NET_CASH_OUTFLOWS":94185.00,
"LCR":248.57,"TOTAL_DEPOSITS":724500.00,"LIQUID_ASSETS":217350.00,"LIQUID_ASSETS_RATIO":30.00}}
JSON
KEY="$(uuidgen)"
api -X POST "$API/v1/submissions" -H 'Content-Type: application/json' -H "Idempotency-Key: $KEY" \
  --data-binary @mlr-2026-09.json -D first.headers -o first.json -w '%{http_code}\n'           # 201

# 5. Read the findings: one warning (Level 2B over 15% of HQLA) that a bank user justifies in the portal.
jq '.validation | {readyToSubmit, errors, warnings, findings: [.findings[] | {rule, field, severity}]}' first.json

# 6. The connection dropped before you saw the answer? Retry with the same key and the same file.
api -X POST "$API/v1/submissions" -H 'Content-Type: application/json' -H "Idempotency-Key: $KEY" \
  --data-binary @mlr-2026-09.json -D retry.headers -o retry.json -w '%{http_code}\n'           # 201 again
grep -i '^idempotent-replayed' retry.headers                                                  # idempotent-replayed: true
cmp first.json retry.json && echo 'same answer, delivered once'

# 7. Later, check where the return stands.
api "$API/v1/submissions/$(jq -r .submissionId first.json)" | jq -c '.submission | {status, source, validation}'
```

A new delivery for the period with a new key answers 200 and updates the same draft.

## Troubleshooting

- Find a failed request by its `traceId` or `X-Trace-Id`: [Find the request](TROUBLESHOOTING.md#1-find-the-request).
- Every status and `code` with its cause and fix, and the log events: [Web API](TROUBLESHOOTING.md#9-web-api).
- A 401 for a token that looks right, TLS trust, a 403: [Sign-in, tokens and WSO2](TROUBLESHOOTING.md#5-sign-in-tokens-and-wso2).
- Findings, templates and closed obligations: [Returns, templates and uploads](TROUBLESHOOTING.md#6-returns-templates-and-uploads).
- Is the API up, and can it reach the database and WSO2: [Check health](TROUBLESHOOTING.md#2-check-health).
