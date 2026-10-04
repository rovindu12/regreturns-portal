#!/usr/bin/env python3
"""Fails the build when line coverage of a gated assembly drops below its threshold.

Usage: check-coverage.py <cobertura.xml> [Assembly=percent ...]
Example: check-coverage.py unit.cobertura.xml RegReturns.Domain=80
"""
import sys
import xml.etree.ElementTree as ET


def main() -> int:
    if len(sys.argv) < 3:
        print(__doc__)
        return 2

    report = ET.parse(sys.argv[1]).getroot()
    rates = {p.get("name"): float(p.get("line-rate")) * 100 for p in report.iter("package")}
    failed = False
    for gate in sys.argv[2:]:
        assembly, threshold = gate.split("=")
        actual = rates.get(assembly)
        if actual is None:
            print(f"::error::{assembly} not found in coverage report")
            failed = True
            continue
        status = "ok" if actual >= float(threshold) else "BELOW THRESHOLD"
        print(f"{assembly}: {actual:.1f}% line coverage (minimum {threshold}%) {status}")
        failed |= actual < float(threshold)

    for name, rate in sorted(rates.items()):
        print(f"  {name}: {rate:.1f}%")
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
