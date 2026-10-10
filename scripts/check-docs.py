#!/usr/bin/env python3
"""Checks the Markdown documentation: every relative link and image points at a file that exists, every #anchor at a
heading that exists (GitHub's anchor rules), and every ADR is listed in docs/adr/README.md (ADR 0035).

    python3 scripts/check-docs.py            # from the repository root; exit 0 when everything resolves

External links (http, https, mailto) are not fetched: CI must not depend on other people's websites. Links inside code
blocks and inline code are ignored. Run by the CI job "Documentation".
"""

from __future__ import annotations

import re
import subprocess
import sys
from pathlib import Path
from urllib.parse import unquote

ROOT = Path(__file__).resolve().parent.parent
ADR_DIR = ROOT / "docs" / "adr"

FENCE = re.compile(r"^\s*(```|~~~)")
INLINE_CODE = re.compile(r"`+[^`]*`+")
# [text](target "title") and ![alt](target); the target ends at whitespace or the closing bracket.
LINK = re.compile(r"!?\[(?:[^\]\[]|\[[^\]]*\])*\]\(\s*<?([^)\s>]+)>?(?:\s+\"[^\"]*\")?\s*\)")
REFERENCE = re.compile(r"^\s*\[[^\]]+\]:\s*<?(\S+?)>?(?:\s+\"[^\"]*\")?\s*$")
HTML_SOURCE = re.compile(r"<(?:img|a|source)\b[^>]*?\s(?:src|href|srcset)=\"([^\"]+)\"", re.IGNORECASE)
HEADING = re.compile(r"^(#{1,6})\s+(.*?)\s*#*\s*$")
HTML_ANCHOR = re.compile(r"<a\s+(?:id|name)=\"([^\"]+)\"", re.IGNORECASE)
EXTERNAL = re.compile(r"^(?:[a-z][a-z0-9+.-]*:|//)", re.IGNORECASE)


def markdown_files() -> list[Path]:
    """The Markdown files git tracks (so build output and dependencies are never checked)."""
    listed = subprocess.run(
        ["git", "ls-files", "-z", "--cached", "--others", "--exclude-standard", "--", "*.md"],
        cwd=ROOT, check=True, capture_output=True,
    ).stdout.decode().split("\0")
    return sorted(ROOT / name for name in listed if name and (ROOT / name).is_file())


def prose_lines(path: Path) -> list[tuple[int, str]]:
    """The file's lines outside fenced code blocks, with inline code removed, numbered from 1."""
    lines, fenced = [], False
    for number, line in enumerate(path.read_text(encoding="utf-8").splitlines(), start=1):
        if FENCE.match(line):
            fenced = not fenced
            continue
        if not fenced:
            lines.append((number, INLINE_CODE.sub("", line)))
    return lines


def slug(heading: str) -> str:
    """GitHub's anchor for a heading: link text only, lower case, punctuation dropped, spaces to hyphens."""
    text = re.sub(r"!?\[([^\]]*)\]\([^)]*\)", r"\1", heading)
    text = re.sub(r"<[^>]+>", "", text).replace("`", "").lower()
    text = "".join(c for c in text if c.isalnum() or c in "-_ ")
    return text.replace(" ", "-")


def anchors(path: Path, cache: dict[Path, set[str]]) -> set[str]:
    """Every anchor a Markdown file offers: its headings (with GitHub's -1, -2 for repeats) and explicit <a id>."""
    if path not in cache:
        found: set[str] = set()
        seen: dict[str, int] = {}
        for _, line in prose_lines(path):
            if match := HEADING.match(line):
                base = slug(match.group(2))
                count = seen.get(base, 0)
                seen[base] = count + 1
                found.add(base if count == 0 else f"{base}-{count}")
            found.update(HTML_ANCHOR.findall(line))
        cache[path] = found
    return cache[path]


def targets(line: str) -> list[str]:
    found = LINK.findall(line) + HTML_SOURCE.findall(line)
    if match := REFERENCE.match(line):
        found.append(match.group(1))
    return found


def check_links(files: list[Path]) -> list[str]:
    problems: list[str] = []
    cache: dict[Path, set[str]] = {}
    for path in files:
        for number, line in prose_lines(path):
            for target in targets(line):
                if EXTERNAL.match(target):
                    continue
                where = f"{path.relative_to(ROOT)}:{number}"
                file_part, _, anchor = target.partition("#")
                file_part = unquote(file_part.split("?", 1)[0])
                resolved = (path.parent / file_part).resolve() if file_part else path
                if not resolved.is_relative_to(ROOT):
                    problems.append(f"{where}: {target} points outside the repository")
                elif not resolved.exists():
                    problems.append(f"{where}: {target} does not exist")
                elif anchor and resolved.suffix.lower() == ".md" and unquote(anchor).lower() not in anchors(resolved, cache):
                    problems.append(f"{where}: {target} has no heading for #{anchor}")
    return problems


def check_adr_index() -> list[str]:
    index = (ADR_DIR / "README.md").read_text(encoding="utf-8")
    return [f"docs/adr/README.md does not list {adr.name}"
            for adr in sorted(ADR_DIR.glob("[0-9][0-9][0-9][0-9]-*.md")) if f"({adr.name})" not in index]


def main() -> int:
    files = markdown_files()
    problems = check_links(files) + check_adr_index()
    for problem in problems:
        print(f"  FAIL  {problem}")
    if problems:
        print(f"{len(problems)} problem(s) in the documentation.")
        return 1
    print(f"Documentation OK: {len(files)} Markdown files, every relative link, image and anchor resolves.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
