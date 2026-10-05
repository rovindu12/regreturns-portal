# 17. WSO2 configuration as code with an idempotent setup tool

- Status: accepted
- Date: 2026-10-04

## Context

RegReturns needs a set of WSO2 objects (plan §4.2): an `institution_id` claim and scope, an API resource with scopes,
the portal's OIDC app, six application roles, client-credentials apps per bank, a provisioning client and demo users.
Clicking them together in the Console is not repeatable, and exporting WSO2's database is not reviewable.

## Decision

- `tools/RegReturns.IamBootstrap` (`apply`, `demo-users`) creates or updates every object through WSO2's management and
  SCIM 2 APIs. Every object is looked up by a stable name first; it is created when missing, patched when different and
  otherwise left alone, so a second run reports no changes. It never deletes and recreates an app or the API resource,
  because WSO2 cascades those deletes to roles and authorizations.
- Rules learned against the live server are encoded in the tool: application PUTs replace everything (the full desired
  object is always sent), PATCHing `associatedRoles` deletes roles (never sent), API scopes are only ever added (a PUT
  would un-authorize them everywhere), and roles are looked up by name and audience.
- Client ids are deterministic (`regreturns-portal`, `regreturns-bank-<code>`, `regreturns-demo-api`); client secrets
  are random, kept by WSO2, and written to a git-ignored env file with mode 600, never to logs.
- The demo users come from the demo `AppUser` rows, so the app database is the single list of demo accounts; the tool
  links each `AppUser` to its WSO2 user id. Bank clients are recorded in `iam.ApiClients` for institution scoping.
- Roles have the portal as their audience, so they appear only in the portal's tokens. The administrator role is
  **`portal_admin`**, not `system_admin`: WSO2 7.3 rejects role names that start with `system_`.
- Bank clients get the API identifier in their tokens' `aud` (WSO2's `idToken.audience` setting); the portal does not,
  so its tokens cannot be used against the API.
- The public Swagger demo client is read-only (`returns:read`, `reference:read`) because its secret is published.

## Consequences

A fresh environment is one command away (`apply`), and the nightly demo reset can call `demo-users`. WSO2 changes made
by hand in the Console are overwritten on the next run, which is intended. The tool depends on management API details
that WSO2 may change between versions; the integration test that runs it twice against the container catches that.
