# 1. Record architecture decisions

- Status: accepted
- Date: 2026-10-04

## Context

Reviewers of a regulated system need to see why it is built the way it is, not only how.

## Decision

Significant decisions are recorded as short Architecture Decision Records in this folder, using a lightweight
[MADR](https://adr.github.io/madr/) layout: context, decision, consequences. Records are numbered and never
rewritten; a later record supersedes an earlier one.

## Consequences

Every non-obvious choice in the code base can be traced to a record. New records are added in the same
change as the code they describe.
