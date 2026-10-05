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
  a VBA project is refused; a `.csv` must be text without a ZIP or other binary signature.
- Limits: 5 MB per file (checked in the browser, by the controller and by the use case, with the request body capped a
  little above), and parsing limits on rows and cell lengths in the reader.
- Cell values are read as text and parsed by the same `FieldValueParser` as the web form. Formulas are not evaluated;
  the cached value is used. Values written to downloads that start with `=`, `+`, `-` or `@` are written as text so
  they cannot become formulas.
- Accepted files are kept in `returns.StoredFiles` (`varbinary(max)`) with a sanitised name, the detected content type,
  size and SHA-256, linked to the submission and revision. There is no endpoint that serves them back: they are evidence
  for supervisors and auditors, not downloads.
- Fields the file leaves out keep their values, and unknown field codes refuse the whole file (listing the codes).

## Consequences

- No file system or object store to secure, back up or scan; database backups carry the evidence. At 5 MB per file
  and a handful of files per return a month, the growth is small. A move to blob storage would change only the
  infrastructure.
- Refusals are logged and counted by error code (`regreturns.uploads`), never with the file name or figures.
- Antivirus scanning is out of scope for the demo; the content checks and the fact that files are never opened or
  served are the mitigation.
