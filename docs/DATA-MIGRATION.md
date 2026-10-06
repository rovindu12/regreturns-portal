# Data migration from the legacy returns system

How to move filed returns from a legacy system's CSV exports into RegReturns with `regreturns-migrator legacy`. The
design and its reasons are in [ADR 0029](adr/0029-legacy-data-migration.md); the sample exports and their planted
defects are described in [samples/legacy/README.md](../samples/legacy/README.md).

## What it does

1. Reads the mapping and every CSV file it lists, and records each file's SHA-256.
2. Cleanses each row: bank name, reporting date, filing and approval dates, and every value column.
3. Keeps the last row for each return type, bank and period; earlier rows are reported as superseded.
4. In one database transaction, validates each row with the template in force for its period, then files it as an
   approved return (`Source = Migration`) through the `system.migration` account.
5. Reads the stored values back and reconciles them with the source, return by return and field by field.
6. Commits only if the run is not a dry run and everything reconciles; otherwise it rolls back. Either way the run
   and its row errors are kept in the `migration` schema.

Migrated returns, their obligations and the run are recorded in the audit trail, with the actor
`regreturns-migrator` (system).

## Before you start

- The database is migrated (`migrate-db`) and holds the banks, return types and published templates the exports
  refer to. Templates must be in force for the oldest period you migrate.
- The migrator has `ConnectionStrings:RegReturns` and `Audit:HmacKey`, the same key as the portal and the API, so the
  migrated returns join the same hash chain. Locally, `scripts/dev-secrets.sh` sets both.
- The exports and the mapping are in one folder. The folder holds exactly the `.csv` files the mapping lists.

## Run it

```bash
# 1. Dry run: everything is loaded, validated and reconciled, then rolled back.
dotnet run --project tools/RegReturns.Migrator -- legacy --source samples/legacy --dry-run --report out/legacy-dry

# 2. Read the console tables and out/legacy-dry/row-errors.csv. Fix the exports or the mapping and repeat.

# 3. Live run: commits only if it reconciles.
dotnet run --project tools/RegReturns.Migrator -- legacy --source samples/legacy --report out/legacy-live
```

| Option | Meaning |
|---|---|
| `--source <folder>` | Folder with the CSV exports (required) |
| `--mapping <file>` | Mapping file; default `mapping.json` in the source folder |
| `--dry-run` | Roll back at the end, whatever the outcome |
| `--report <folder>` | Write the CSV reports there (created if missing) |

| Exit code | Meaning |
|---|---|
| 0 | Reconciled; committed unless it was a dry run. Rejected rows may still be listed |
| 1 | Could not start (missing folder, invalid mapping, files and mapping disagree, a header that does not match) or failed |
| 2 | Did not reconcile; nothing was committed |

Running the same exports again is safe: returns already migrated are skipped, reported as *already migrated* and
reconciled again.

## The mapping file

```json
{
  "system": "VRRS (Valoria Returns Reporting System)",
  "dateFormats": ["yyyy-MM-dd", "dd/MM/yyyy HH:mm", "dd/MM/yyyy", "dd-MMM-yyyy", "yyyyMMdd"],
  "excelSerialDates": true,
  "nullTokens": ["N/A", "NA", "NULL", "#N/A"],
  "currencyCodes": ["VLD"],
  "institutions": {
    "HLB": ["Harbourline Bank PLC"],
    "MDB": ["Meridian Development Bank", "Meridian Dev Bank"]
  },
  "files": [
    {
      "file": "VRRS_MLR_EXPORT.csv",
      "returnType": "MLR",
      "institutionColumn": "Institution",
      "periodEndColumn": "Return Period",
      "submittedColumn": "Received On",
      "approvedColumn": "Approved On",
      "fields": { "Lvl 1 Assets": "L1_HQLA", "Total HQLA": "TOTAL_HQLA" },
      "ignoredColumns": ["Remarks"]
    }
  ]
}
```

| Member | Meaning |
|---|---|
| `system` | Name of the legacy system, used in each migrated return's history comment |
| `dateFormats` | .NET custom date formats, tried in order with the invariant culture. Put the day-first or month-first format you mean first; the migrator never guesses per row |
| `excelSerialDates` | Read a five-digit number in a date column as an Excel serial date |
| `nullTokens` | Values that mean "no value", matched ignoring case. A blank cell is always no value |
| `currencyCodes` | Codes allowed before or after an amount |
| `institutions` | Legacy names of each bank, keyed by its RegReturns code. The code itself always matches. Names match after upper-casing and turning punctuation and repeated spaces into one space |
| `files[].file` | File name in the source folder |
| `files[].returnType` | RegReturns return type code |
| `files[].*Column` | Columns holding the bank name, the reporting date (last day of the period), the filing date and the approval date |
| `files[].fields` | Template field code of each value column. Fields a file leaves out are blank on the migrated return |
| `files[].ignoredColumns` | Columns deliberately not migrated, such as remarks |

Every column in a file's header must be mapped or ignored, and every mapped column must be in the header. Unknown
members, a name mapped to two banks or a column named twice stop the run with `Migration.MappingInvalid`.

## How values are cleansed

| Value | Accepted | Refused |
|---|---|---|
| Amounts | `1234.56`, `1,234.56`, `1 234.56`, `VLD 1,234.56`, `1,234.56 VLD`, `-1234.56`, `(1,234.56)` | `1.234,56` (decimal comma), `1,23,456` (groups not of three), text |
| Ratios | `12.5`, `12.5%`, `(14.25)` (negative) | anything the amount rules refuse |
| Dates | Any listed format, with or without time; month names in any case; Excel serials if enabled | `31/02/2024`, anything no format reads |
| Missing | Blank cells and null tokens | |

Dates without a time zone are read as UTC. A cleansed number then goes through the template's own parser and rules,
exactly like a value typed in the portal.

## Row errors

Each rejected or superseded row is one or more lines in `row-errors.csv` and in `migration.RowErrors`.

| Code | Meaning | What to do |
|---|---|---|
| `Legacy.ColumnCount` | The row has more or fewer cells than the header | Re-export the row; a cut line is usually a broken export |
| `Legacy.UnknownInstitution` | The bank name is not in the mapping | Add the spelling under the right bank code, if it is one |
| `Legacy.InstitutionNotInPortal` | The mapping names a bank code the portal does not have | Expected for banks closed before the portal; create the bank only if its history must be kept |
| `Legacy.BadDate` | A date column is blank or matches no format | Add the format to `dateFormats`, or correct the export |
| `Legacy.NotPeriodEnd` | The reporting date is not the last day of a month or quarter | Correct the export |
| `Legacy.InconsistentDates` | Filed before the period ended, approved before it was filed, or dated in the future | Correct the export |
| `Legacy.BadNumber` | A value cannot be read without guessing | Correct the export; decimal commas are never reinterpreted |
| `Legacy.NoTemplate` | No published template is in force for the period | Publish a template for that period, or leave the row behind |
| `Legacy.AlreadyFiled` | The portal already holds a return filed there for that period | The portal's return stands; leave the row behind |
| `Legacy.Validation` | A validation rule with error severity fails (one line per finding) | Correct the export, or accept that the row stays behind |
| `Legacy.Refused` | The domain refused the return for another reason | See the message |
| `Legacy.Superseded` | A later row in the file has the same bank and period (not an error) | None, unless the earlier row was the right one |

Warnings do not reject a row: the migrated return keeps them, justified with a note that the legacy system accepted
them.

## Reports

`--report` writes five UTF-8 CSV files (with a byte-order mark, so spreadsheets read them correctly). A cell that
starts with `=`, `+`, `-`, `@`, a tab or a carriage return, and is not a plain number, is prefixed with `'` so that a
spreadsheet never runs it as a formula.

| File | Contents |
|---|---|
| `summary.csv` | Per file and in total: rows read, blank, superseded, rejected, migrated, already migrated, and the outcome |
| `row-errors.csv` | File, line, kind, code, column, value and message of every row error |
| `reconciliation-detail.csv` | Every field of every return: source, target, difference and status |
| `reconciliation-by-bank.csv` | Per return type, bank and field: return counts and totals, source against target |
| `reconciliation-by-period.csv` | The same per return type, period and field |

The reports and the console hold return figures. Treat them like the exports: keep them out of the repository (`out/`
is git-ignored) and delete them when the migration is signed off. Logs never contain figures.

## Checking the result

- The console's last line states the outcome and what was committed.
- In the portal, a migrated return's history shows one step, "Migrated from the legacy system, approved", with a
  comment naming the file, line and run.
- `migration.Runs` holds one row per run, including dry runs and runs that did not reconcile.
- The auditor's chain verification covers the migrated returns like any other entry.

```sql
SELECT StartedAt, IsDryRun, Outcome, RowsRead, MigratedReturns, AlreadyMigratedReturns, RejectedRows, Mismatches
FROM migration.Runs ORDER BY StartedAt DESC;
```

For log events 2101 to 2106 and common failures, see [Troubleshooting §11](TROUBLESHOOTING.md#11-legacy-data-migration).
