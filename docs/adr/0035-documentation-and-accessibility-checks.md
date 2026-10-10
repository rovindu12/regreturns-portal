# 35. Documentation as code, C4 diagrams in Mermaid, and accessibility and screenshots from the browser tour

- Status: accepted
- Date: 2026-10-09

## Context

Phase 12 finishes the documentation (plan §10): a README with screenshots, logins and the role mapping, architecture
documents with C4 diagrams, identity, API and user guides, and a backlog. The plan's standards table also asks for
WCAG 2.2 AA, "checked with axe in the Playwright scenario run", and the phase's tests are a documentation link check
and a full test run.

Documentation that is not checked drifts: a renamed file or heading breaks links nobody follows, and screenshots age
with every change to a page. Accessibility checked once by hand regresses the same way. The guided tour
(`scripts/smoke/demo-scenario.cjs`, ADR 0031) already signs in as every role and visits most pages, but it ran only by
hand against a demo, never in CI.

## Decision

- **Documentation lives in the repository as Markdown**, next to the code it describes and changed in the same pull
  request. Diagrams are Mermaid in the documents, so GitHub renders them and a diff shows a change. The C4 levels
  (context, containers, components) are drawn as Mermaid flowcharts rather than with Mermaid's C4 syntax, which is
  experimental and lays out poorly on GitHub. Only the landing page's diagram is rendered to SVG (ADR 0031), because the
  portal does not run Mermaid.
- **A link check runs in CI** (`scripts/check-docs.py`, job *Documentation*): every relative link, image and `#anchor`
  in every tracked Markdown file must resolve, with GitHub's anchor rules, and every ADR must be listed in
  `docs/adr/README.md`. External links are not fetched, so the build never depends on someone else's website.
- **Accessibility is checked by axe in the guided tour**, against the WCAG 2.0, 2.1 and 2.2 level A and AA rules
  (`scripts/smoke/accessibility.cjs`, axe-core pinned like Playwright). axe runs through Playwright's `evaluate`, which
  the Content Security Policy does not block, so the check needs no script tag and no policy exception. Any violation
  on a portal page fails the tour, whatever axe's impact rating: each is a conformance failure. WSO2's sign-in pages are
  checked and reported but do not fail the tour: they are WSO2's product, like its images' vulnerabilities (ADR 0034).
  The public pages are also checked at phone width.
- **The tour runs in the Release workflow** against the production stack on the runner, after the identity smoke test:
  every pull request takes a return from draft to approval through every role in a real browser, with the axe and CSP
  checks. To exercise warnings and justifications, the maker raises non-performing loans by half (keeping the ratio
  consistent), so the checker must justify the warnings and the insight has a movement to explain. The admin step
  looks round the administration pages. The tour also fails fast with a clear message when the demo reset is cooling
  down.
- **Screenshots come from the same tour.** `scripts/demo-scenario.sh --screenshots` saves them to `docs/images` at a
  fixed 1280 × 800 viewport, with the published password, authenticator keys, QR codes and API secret masked. CI
  uploads a fresh set from every run as an artifact, so a reviewer sees what a change looks like; the committed set is
  refreshed by running the script and committing the result.

Fixes the first axe runs called for: links inside alerts use Bootstrap's `alert-link` (contrast); every table has a
caption; a table wrapper that scrolls sideways becomes a focusable, named region (`site.js`) and wrappers no longer
scroll a pixel vertically (`site.css`); and the portal shows a "Page not found" page in its layout for an empty error
answer to a `GET` (status code pages, re-executed for `GET` and `HEAD` only, so a refused `POST` is never re-run through
the anti-forgery check).

## Consequences

- A broken link, a page that loses its labels or contrast, or an inline script fails a pull request, not a visitor.
- axe finds about a third to a half of WCAG issues; keyboard order, focus visibility and reading order still need a
  manual pass before a release that changes the layout. The tour checks the pages it visits: a new page needs a step.
- The Release job runs a few minutes longer.
- Screenshots in the repository are a snapshot: they change only when someone runs the script, and the dates and
  figures in them are the demo's on that day.
