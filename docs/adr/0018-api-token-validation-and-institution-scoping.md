# 18. API token validation and institution scoping

- Status: accepted
- Date: 2026-10-04

## Context

Banks call the RegReturns API with client-credentials tokens from WSO2. The API must accept only tokens meant for it,
know which bank the caller is, and never let one bank read or learn about another bank's data.

## Decision

- **Strict JWT validation.** Issuer `Wso2:Authority + oauth2/token`, audience `https://api.regreturns`, RS256 only,
  signature and expiry required, 30 seconds of clock skew. Only access tokens are accepted: `typ` must be `at+jwt` or
  `application/at+jwt` (both are allowed by RFC 9068 §4), so ID tokens are refused. Signing keys come from WSO2's JWKS
  over the back channel that trusts only the configured CA (ADR 0015). Inbound claim mapping is off.
- **The bank comes from our database, not from the token.** For `aut=APPLICATION` tokens the client id (`azp`, else
  `client_id`) is looked up in `iam.ApiClients`, compared case-sensitively, and the institution is added to the
  principal. Any `institution_id` claim that arrives in a token is removed first, so a user token or a misconfigured
  WSO2 app cannot claim a bank. Tokens whose `azp` and `client_id` disagree, or that carry neither, are rejected.
  Lookups (including misses) are cached for 60 seconds, so deactivating a client takes effect within a minute.
- **Other banks look like nothing.** A request for another bank's resource returns the same `404` problem as an
  unknown one, so the API never reveals which banks or records exist.
- **Every refusal is audited.** Failed authentication (with the exception type only) and forbidden requests (with the
  endpoint's policy names) go to the audit trail through `AccessDeniedAuditor`, without the query string. A signed-in
  caller is recorded once a minute per endpoint (by route pattern, so probing many ids writes one entry) and an
  anonymous caller once a minute per IP address. The write has its own 15-second timeout instead of the request's
  token, so a client cannot cancel its own entry by dropping the connection.
  401 and 403 responses are RFC 9457 problem documents with the trace id. `WWW-Authenticate` carries no error
  description (`IncludeErrorDetails=false`), so a caller cannot learn why a token failed; the log and the audit entry can.

## Consequences

- A bank client works only after IamBootstrap has recorded it in `iam.ApiClients`; an unknown client gets 403.
- When the API cannot reach or trust WSO2, callers see a plain 401; the log says `The signature key was not found`
  and the TLS cause (see the troubleshooting guide).
- Forbidden-request auditing only runs under the real bearer scheme; tests that check it use real signed tokens against
  a static OpenID configuration rather than the header-driven test scheme.
