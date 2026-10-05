# 21. A small in-house expression language for cross-field rules

- Status: accepted (replaces the NCalc choice in the implementation plan)
- Date: 2026-10-05

## Context

Cross-field rules compare two arithmetic expressions over field codes, for example
`[TOTAL_HQLA]` against `[L1_HQLA] + [L2A_HQLA] + [L2B_HQLA]`. The plan proposed NCalc with a function whitelist.
Expressions are written by administrators and evaluated for every return, so they must be safe, exact with money and
easy to explain when they fail. NCalc evaluates in `double` by default, supports far more than the rulebook needs
(string functions, `if`, custom functions through events), and turning that surface off is configuration that has to
stay right forever.

## Decision

`RuleExpression` in the domain parses and evaluates a deliberately small grammar:

- decimal literals with a full stop, field references `[CODE]`, `+ - * /`, unary sign and parentheses;
- three functions, case-insensitive: `Min(a, b, ...)` and `Max(a, b, ...)` (two or more arguments) and `Abs(x)`;
- at most 500 characters and 32 levels of nesting, checked when the rule is saved.

Arithmetic is `decimal` throughout. Evaluation returns no value, and the rule is skipped, when a referenced field is
blank or invalid, on division by zero and on overflow. The comparison (equal, at most, at least) and the absolute
tolerance are typed rule columns (ADR 0008), not part of the expression text.

## Consequences

- No third-party evaluator, no reflection and no way to reach anything but the return's own figures.
- Failure messages can show both sides with the same rounding the bank sees ("Entered 1,400; calculated 1,300.").
- New functions need a code change and tests; the regulator's rulebook changes slowly, so that is acceptable.
- Saved expressions are re-parsed on publish, so a template cannot be published with a rule that cannot run.
