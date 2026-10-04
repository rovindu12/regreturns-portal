# 15. Explicit trust for a private development CA, never disabled validation

- Status: accepted
- Date: 2026-10-04

## Context

WSO2 serves HTTPS with its own certificate. Development and server-to-server calls (discovery, JWKS, token exchange,
management APIs) must trust it. The usual shortcuts, `DangerousAcceptAnyServerCertificateValidator`, `curl -k` or
adding a dev certificate to the machine's trust store, either switch validation off or widen trust for every app.

## Decision

- `scripts/dev-certs.sh` creates a private CA (`.certs/regreturns-dev-ca.crt`, git-ignored) and issues WSO2's TLS
  certificate (SANs `localhost`, `iam.localhost`, `wso2`, `127.0.0.1`, and `iam.$DOMAIN` when set), a separate token
  signing key, and a truststore that WSO2 uses for outbound calls.
- .NET code that calls WSO2 uses one named `HttpClient` (`Wso2Backchannel`). When `Wso2:TrustedCaPath` is set, its
  handler validates the server certificate with `X509ChainTrustMode.CustomRootTrust` against that CA only. The platform's
  host name check still applies, expiry and signatures are checked, and revocation is not checked because the private CA
  publishes no CRL. When the setting is empty, the operating system trust store applies (production, behind Let's Encrypt).
- A `DelegatingHandler` rewrites WSO2's public origin to its internal address for server-to-server calls, so the issuer
  stays public and is still validated against the public value.
- Scripts use `curl --cacert .certs/regreturns-dev-ca.crt`; nothing in the repository uses `-k`.

## Consequences

Validation is never switched off, and trusting the dev CA affects only calls to WSO2. Developers run
`scripts/dev-certs.sh` once per clone; browsers will warn for `https://localhost:9443` unless the developer chooses to
trust the CA in their own browser, which is their decision, not the repository's.
