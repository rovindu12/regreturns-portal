# 29. Legacy data migration: mapping file, one reconciled transaction, approved returns through a system account

- Status: accepted
- Date: 2026-10-06

## Context

Before the portal, the Bank of Valoria collected returns in an older system, VRRS (Valoria Returns Reporting System).
Its filings from January 2024 to September 2025 must move into RegReturns so that supervisors see the history, the
dashboards show trends, and variance rules can compare a bank's new return with the same period last year. VRRS can
only export CSV files, one per return type, and the exports are messy:

- dates in several formats, some with times, some as Excel serial numbers;
- amounts with thousand separators, currency codes, percent signs and accounting brackets;
- `N/A` for missing values;
- bank names spelled several ways;
- returns exported twice, or corrected by a later row;
- rows that VRRS accepted but the current rules would refuse.

Plan §6 asks for a migrator with a dry run, a JSON mapping, an error report and a reconciliation of row counts and
field totals, source against target, by bank and period, that exits non-zero on a mismatch.

## Decision

- **A `legacy` verb on the migrator.** It runs as
  `regreturns-migrator legacy --source <folder> [--mapping <file>] [--dry-run] [--report <folder>]`.
  - The mapping defaults to `mapping.json` in the source folder.
  - It prints console tables and, with `--report`, writes five CSV files: summary, row errors, reconciliation
    detail, totals by bank and totals by period.
  - Exit codes: 0 reconciled, 1 could not start or failed, 2 did not reconcile. Rejected rows do not change the
    exit code; they are listed in the report.
- **The mapping is data, not code** (`LegacyMapping`). A JSON file names:
  - the cleansing rules: date formats in order, whether Excel serial dates are allowed, null tokens and currency
    codes;
  - each bank's legacy spellings, keyed by bank code;
  - for each file: its return type, which columns hold the bank, the period end, the filing date and the approval
    date, the column of each template field, and the columns deliberately left behind (free-text remarks).

  Unknown JSON members, a name mapped to two banks, a column named twice or a date format that cannot read back
  its own output all stop the run. The source folder must hold exactly the files the mapping lists. A file's
  header must match its mapping (any order, case ignored).
- **Cleansing is pure and refuses to guess** (`LegacyCleansing`):
  - Bank names are matched after upper-casing and collapsing punctuation and spaces.
  - Numbers may carry a currency code before or after, a percent sign, a sign or brackets, and comma or space
    thousand groups of three. A comma as the decimal point (`9.661,53`) is refused, never reinterpreted.
  - Dates are tried in the mapping's order, so whether `05/06/2024` is day first is decided once in the mapping,
    not per row. Month names may be in any case.
  - The exports carry no time zone; dates are read as UTC, the portal's storage convention.
  - The reporting date must be the last day of a month or quarter.
- **The last row wins.** Rows are grouped by return type, bank and period. Only the last row of each group is
  loaded; earlier ones are recorded as `Legacy.Superseded`, naming the line that replaced them.
- **Every row goes through the domain** (`Submission.Migrate`).
  - A migrated return is approved, with `Source = Migration` and revision 1, and has one `Migrate` workflow event
    whose comment names the legacy system, the file, the line and the run.
  - VRRS recorded who approved a return only as free text, so the system account `system.migration` stands in as
    preparer, submitter and approver. It is regulator-side, has no e-mail and no WSO2 link, and cannot sign in:
    `LinkSignedInUser` refuses system accounts, and `AppUser.Create` refuses reserved user names.
  - Values are validated with the template in force for the period and the same `ValidationEngine` as the portal.
    Prior values come from approved returns, including those migrated earlier in the same run, in period order.
  - An error finding rejects the row (`Legacy.Validation`, one entry per finding). Warnings stay on the return,
    justified with a fixed note, because VRRS accepted them without one.
  - `IsLate` comes from the obligation's due date and the legacy filing date. Filing must be on or after the period
    end, approval on or after filing, and neither in the future (`Legacy.InconsistentDates`).
  - A missing obligation is created. A period the portal already holds a return for is never overwritten
    (`Legacy.AlreadyFiled`). A period migrated before is skipped as *already migrated* and reconciled again, so a
    rerun is safe.
- **One transaction, reconciled before commit** (`LegacyMigrator`).
  - The load runs in one transaction inside the execution strategy.
  - After saving, it reads the stored values back and compares them, return by return and field by field, with
    the cleansed source (`Reconciliation`). Totals are grouped by bank, period and field.
  - The run commits only if it is not a dry run, every line matches, and every row read is accounted for as
    blank, superseded, rejected, migrated or already migrated.
  - Otherwise everything rolls back, and the run is saved on its own with its counts and row errors.
- **Runs are recorded** in the `migration` schema: `Runs`, `RunFiles` (name, return type, SHA-256, rows) and
  `RowErrors` (file, line, kind, code, column, value, message), along with the mapping's SHA-256.
- **Audited like the portal.** The migrator's legacy verb adds the audit trail, with the system actor
  `regreturns-migrator`. Every migrated return, created obligation and run joins the hash chain in the save's
  transaction, so it needs `Audit:HmacKey`. A dry run leaves only the run's entry.
- **Logs never carry figures.** Events 2101 to 2106 cover start, each file read with its hash, finish, mismatch,
  cannot start and failure. Return figures appear only in the console and the report files, which are the
  operator's working papers. The report writer applies the CSV formula guard.
- **Samples are generated.** `regreturns-migrator legacy-samples --out samples/legacy` writes three deterministic
  VRRS exports with planted defects, plus their mapping. The committed copies are kept byte for byte
  (`.gitattributes`: `-text`), because their line endings and byte-order marks are among the defects.
  `samples/legacy/README.md` lists every defect and the expected result.
- **Templates in force since January 2024.** The seeded templates take effect from `DemoScenario.FormsInForceSince`
  (1 January 2024), so the legacy periods have a template. A row for an earlier period is rejected with
  `Legacy.NoTemplate`.

## Consequences

- Migrated history shows on return pages, reports and variance checks like any approved return. Its workflow
  history shows one step, "Migrated from the legacy system, approved".
- A run is all or nothing. That keeps the reconciliation honest and makes reruns trivial. It also means one
  transaction holds every row: right for the few thousand rows of a regulator's history, but millions of rows would
  need batches with a run-level reconciliation (backlog).
- Rejected rows do not stop the run. The operator fixes the export, or adds a spelling or format to the mapping,
  and runs again; returns already migrated are skipped and reconciled.
- A changed figure in a re-exported file, or a stored value changed since the last run, is a mismatch: exit code 2
  and nothing committed. Correcting history that has already been migrated is a portal correction, not a rerun.
- Legacy remarks and the names of legacy approvers are not migrated. The mapping lists the columns it ignores, so
  dropping one is a visible decision.
