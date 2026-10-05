# 22. Return files: checked by content, stored in the database, never served

- Status: accepted
- Date: 2026-10-05

## Context

Banks fill returns in spreadsheets and upload them. An upload is untrusted input: the name and the browser's
content type can be anything, workbooks can carry macros, and spreadsheets are a classic route for formula injection
when files are opened again. The brief requires extension and signature checks, size limits, and that uploads are
never executed or served directly.

## Decision

- Two formats only: `.xlsx` and UTF-8 `.csv`, both laid out as the downloadable template (`FieldCode` and `Value`
  columns; other columns are ignored). The template download pre-fills the values already entered.
- The extension must match the content: an `.xlsx` must be a ZIP package that is a workbook, and a workbook containing
  a VBA project or Excel 4.0 macro sheets is refused; a `.csv` must be UTF-8 text without a ZIP, PDF or other binary
  signature.
- Limits: 5 MB per file (checked in the browser, by the controller and by the use case, with the request body capped a
  little above). Before ClosedXML opens a workbook the package is inspected for zip bombs: at most 200 entries, 50 MB
  expanded in total and a compression ratio of 100 per entry. The reader looks for the header in the first 20 rows and
  reads at most 2,000 rows, 4,000 characters per cell and 200 CSV columns (`ReturnFileLimits`).
- Cell values are read as text and parsed by the same `FieldValueParser` as the web form. Formulas are not evaluated;
  the cached value is used. A number formatted as a percentage is read as `12.5%`, not `0.125`, so a bank's own
  workbook cannot send a figure a hundred times too small.
- Downloads never contain formulas: workbook cells are written as values, and CSV cells that a spreadsheet could run
  (starting with `=`, `+`, `-`, `@`, a tab or a carriage return, and not a plain number) get a leading apostrophe
  (OWASP CSV injection). The reader removes exactly that apostrophe, so a downloaded file uploads unchanged.
- Accepted files are kept in `returns.StoredFiles` (`varbinary(max)`) with a sanitised name, the detected content type,
  size and SHA-256, linked to the submission and revision. There is no endpoint that serves them back: they are evidence
  for supervisors and auditors, not downloads.
- Fields the file leaves out keep their values, and unknown field codes refuse the whole file (listing the codes).

## Consequences

- No file system or object store to secure, back up or scan; database backups carry the evidence. At 5 MB per file
  and a handful of files per return a month, the growth is small. A move to blob storage would change only the
  infrastructure.
- Refusals are logged and counted by error code (`regreturns.uploads`), never with the file name or figures. A workbook
  ClosedXML cannot read logs only the exception type (event 5210).
- Antivirus scanning is out of scope for the demo; the content checks and the fact that files are never opened or
  served are the mitigation.
