# Sample legacy exports (VRRS)

Fictional exports of **VRRS (Valoria Returns Reporting System)**, the Bank of Valoria's returns system before the
portal, with the mapping that migrates them (ADR 0029, [data migration guide](../../docs/DATA-MIGRATION.md)). They cover
January 2024 to September 2025 for the five demo banks, continuing the seeded figures.

The files are generated and must not be edited by hand. Regenerate them with:

```bash
dotnet run --project tools/RegReturns.Migrator -- legacy-samples --out samples/legacy
```

A unit test checks that the committed files equal the generator's output byte for byte, and `.gitattributes` keeps
their line endings and byte-order marks as exported.

| File | Return type | Format |
|---|---|---|
| `VRRS_MLR_EXPORT.csv` | Monthly Liquidity Return (MLR) | UTF-8 without BOM, LF line endings |
| `vrrs_mda_history.csv` | Monthly Deposits and Advances Return (MDA) | UTF-8 with BOM, CRLF line endings |
| `VRRS_QCAR_2024_2025.csv` | Quarterly Capital Adequacy Return (QCAR) | UTF-8 with BOM, CRLF line endings |
| `mapping.json` | Bank spellings, columns and cleansing rules | |

## Mess that is cleaned

These quirks appear throughout and are handled by the mapping and the cleansing rules, without a row error:

- Bank names in several spellings: `Harbourline Bank Plc.`, `HARBOURLINE BANK PLC`, `MERIDIAN DEV. BANK`,
  `Meridian Development Bank (MDB)`.
- Reporting dates as `31/01/2024`, `2024-01-31`, `31-Jan-2024`, `31.01.2024`, `Mar 31, 2024`, `20250930` and Excel
  serial numbers (`45322`, the MDA file in 2024). Approval dates in upper case, such as `23-FEB-2024`.
- Filing times as `08/02/2024 15:15` and `2024-02-08 15:15:00`.
- Amounts as `134033.49`, `"134,033.49"`, `"VLD 134,033.49"`, `"171,462.82 VLD"` and ` 104850.64 ` (padded).
- Ratios with and without a percent sign (`228.41`, `30.23%`).
- Remarks such as `=see covering letter`, which a spreadsheet would run as a formula. The mapping ignores the remarks
  column, and the report writer guards every cell it writes.

## Planted defects and what the migrator does

Line numbers count the header as line 1.

| File | Line | Defect | Result |
|---|---|---|---|
| MLR | 2 | Harbourline, December 2023, before the current forms | Rejected, `Legacy.NoTemplate` |
| MLR | 8, 14, 20 | Valoria Agricultural Bank (mapped as `VAB`), closed before the portal | Rejected, `Legacy.InstitutionNotInPortal` |
| MLR | 31 | Harbourline, June 2024, original filing | Superseded by the correction on line 41 |
| MLR | 51 | Meridian, September 2024: Total HQLA keyed 90 too high | Rejected, `Legacy.Validation` (HQLA sum and LCR rules) |
| MLR | 58 | Crestmont, reporting date `15/11/2024` | Rejected, `Legacy.NotPeriodEnd` |
| MLR | 70 | Lotus Union, January 2025: approved two days before it was received | Rejected, `Legacy.InconsistentDates` |
| MLR | 81 | Northgate, March 2025: Level 2A assets as `9.661,53` | Rejected, `Legacy.BadNumber` |
| MLR | 67 | Blank row after December 2024 | Counted as blank |
| MLR | | Lotus Union, August 2024: unusual HQLA mix | Migrated with its warnings (and September's variance warnings), justified with the migration note |
| MLR | | Northgate, October 2024: filed three days after the due date | Migrated, marked late |
| MDA | 18 | Harbourline, May 2025: line cut short after 10 cells | Rejected, `Legacy.ColumnCount` |
| MDA | 47 | `Lotus Unoin Bank`, April 2024 (keyed again correctly on the next line) | Rejected, `Legacy.UnknownInstitution` |
| MDA | 58 | Lotus Union, February 2025, exported twice | Superseded by line 59 |
| MDA | 77 | Northgate, November 2024: NPL amount and ratio `N/A` | Rejected, `Legacy.Validation` (both required) |
| MDA | 89 | Meridian, reporting date `31/02/2024` | Rejected, `Legacy.BadDate` |
| MDA | 109 | Trailing empty line | Counted as blank |
| MDA | | Meridian, July 2024: filed two days late | Migrated, marked late |
| QCAR | 13 | Crestmont, 2024-Q3, first filing | Superseded by the restatement on line 37 |
| QCAR | 20 | Northgate, 2024-Q4: capital adequacy ratio in brackets, read as negative | Rejected, `Legacy.Validation` (range and calculation) |
| QCAR | | Northgate, 2024-Q2: filed four days late | Migrated, marked late |

## Expected result

On the seeded demo database, a dry run and a live run both reconcile (exit code 0):

| File | Rows | Blank | Superseded | Rejected | Migrated |
|---|---:|---:|---:|---:|---:|
| `VRRS_MLR_EXPORT.csv` | 111 | 1 | 1 | 8 | 101 |
| `vrrs_mda_history.csv` | 108 | 1 | 1 | 4 | 102 |
| `VRRS_QCAR_2024_2025.csv` | 36 | 0 | 1 | 1 | 34 |
| All files | 255 | 2 | 3 | 13 | 237 |

Run again, the same files report 237 returns as already migrated and still reconcile.
