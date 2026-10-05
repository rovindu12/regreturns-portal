# 28. Reporting views read with Dapper, and audited Excel and PDF exports

- Status: accepted
- Date: 2026-10-05

## Context

Phase 6 adds the supervisors' dashboards (plan §6): a compliance heat map (bank × period: on time, late, missing), the
overdue list, the trend of validation findings by rule, key-ratio sparklines, and Excel and PDF exports. These are
aggregate reads across obligations, returns, findings and values, the kind of query EF Core's LINQ makes either slow
(many round trips) or hard to read. The plan names Dapper over SQL views for them, ClosedXML for Excel and QuestPDF for
PDF. Every role opens the reports area; bank staff see only their own bank. Regulator staff must not see a bank's
draft (ADR 0025), and the audit trail must show who took data out of the system.

## Decision

- **Views in a `reporting` schema**, created by the `AddReportingViews` migration with plain SQL and not part of the EF
  model:
  - `reporting.ObligationCompliance`: one row per obligation, with its institution, return type, `PeriodKey` and live
    (not rejected) return, if any;
  - `reporting.SubmittedFindings`: findings of revisions that reached the regulator, that is every revision before
    the current one plus the current one once its return is submitted (drafts never count);
  - `reporting.ApprovedValues`: numeric values of approved returns.

  `PeriodKey` (year × 100 + month or quarter) sorts and filters periods of one frequency.
- **Dapper read model.** `IReportingReadModel` (Application) is implemented by `ReportingReadModel` (Infrastructure).
  - It runs parameterised SQL whose WHERE clause holds only the conditions a query sets, on its own `SqlConnection`
    with SqlClient's exponential retry on open (`Database:MaxRetryCount`) and the EF command timeout.
  - Rows map through record constructors in their SQL types and are converted in code (dates, enum names).
  - The SqlClient OpenTelemetry instrumentation traces these queries like EF's.
- **Rules in one place.** `Compliance.StateOf` decides each cell, matching `ReturnObligation.IsOverdue`:
  - open and past due is *overdue*;
  - open otherwise is *not due yet*;
  - any other status is *late* or *on time*, from its first submission.

  The window is the last `Reports:MonthsShown` (12) or `Reports:QuartersShown` (4) completed periods. Shaping is
  pure (`ReportShaping`) and unit tested. `ReportBuilder` resolves the caller and scopes every query:
  - bank staff to their institution;
  - regulator staff to every active institution, where a return's workflow status shows only once it has been
    submitted.
- **Key ratios are configuration.** `Reports:KeyRatios` lists return type and field pairs (LCR, liquid assets ratio,
  NPL ratio, CAR, CET1 ratio). Their labels, units and precision come from the latest published template, so a
  renamed field needs no code change.
- **Exports.** `GET /reports/compliance/export?returnType=&format=xlsx|pdf` exports the compliance report:
  - Excel, with three sheets (grid with counts, overdue list, every obligation), strings always stored as text;
  - PDF, A4 landscape, with the grid, legend and overdue list.

  Every export appends an `AuditAction.ReportExported` entry (entity `Report`/`compliance`). The entry's details name
  the return type, window, scope and format, never figures. Exports also log event 5401 and count
  `regreturns.reports.exports`.
- **Charts.** Chart.js 4.5.1 is vendored under `wwwroot/lib/chart.js`. `wwwroot/js/reports.js` draws from JSON in
  `data-chart` attributes; there are no inline scripts. Every chart's figures are also on the page as text: a
  findings table and sparkline labels for screen readers. Grid cells spell out their state, so colour is never the
  only cue.
- **QuestPDF's Community licence.** It is free for individuals, open-source projects and companies under USD 1M
  annual revenue, which covers this portfolio project. The renderer sets it in a static constructor. A commercial
  deployment would need to buy a licence or switch to another renderer behind `IComplianceReportRenderer`.

## Consequences

- A migration that renames a column a view uses breaks the view. The read model's integration tests query every
  column on the seeded data and catch that. The views are not `SCHEMABINDING`, so EF migrations of the base tables
  stay unchanged.
- Exports are synchronous and in memory. That suits the demo's five banks and twelve periods. A much larger grid
  would move to a background job with a download link (backlog).
- The reports area adds an audit entry per download, so the audit trail shows who exported what and when.
- Exports are plain GET downloads, like any page read. A link on another site can start one for a signed-in user
  (the session cookie is `SameSite=Lax`), but it cannot read the file. The user sees the download, and the audit
  trail records it.
