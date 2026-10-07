"""Failed-test extraction: which tests failed and their first assertion line, from the runners' own output."""
from __future__ import annotations

import re
import xml.etree.ElementTree as ET
from pathlib import Path

HEADER = re.compile(r"^(FAIL|ERROR): (\S+) \((.+?)\)")


def python_report(text: str) -> dict:
    """unittest's text output as {"ran": n, "failures": [(test id, first message line)]}."""
    failures, pending = [], None
    for line in text.splitlines():
        header = HEADER.match(line)
        if header:
            if pending:
                failures.append((pending, ""))
            method, owner = header.group(2), header.group(3)
            pending = owner if owner.endswith("." + method) else f"{owner}.{method}"
            continue
        if pending and line and not line[0].isspace() and not line.startswith(("-", "=", "Traceback")):
            failures.append((pending, line.strip()))
            pending = None
    if pending:
        failures.append((pending, ""))
    ran = re.search(r"^Ran (\d+) tests? in", text, re.M)
    return dict(ran=int(ran.group(1)) if ran else None, failures=failures)


def trx_report(path: Path) -> dict | None:
    """A `dotnet test --logger trx` file as {"passed", "failed", "skipped", "failures": [(test,
    first message line)]}, or None when it is missing or unreadable."""
    try:
        root = ET.parse(path).getroot()
    except (OSError, ET.ParseError):
        return None
    counters = root.find(".//{*}ResultSummary/{*}Counters")
    failures = []
    for result in root.iterfind(".//{*}UnitTestResult"):
        if result.get("outcome") != "Failed":
            continue
        message = result.findtext(".//{*}ErrorInfo/{*}Message") or ""
        first = next((line.strip() for line in message.splitlines() if line.strip()), "")
        failures.append((result.get("testName", "?"), first))

    def count(name):
        return int(counters.get(name, 0)) if counters is not None else None
    total, passed, failed = count("total"), count("passed"), count("failed")
    skipped = total - passed - failed if None not in (total, passed, failed) else None
    return dict(passed=passed, failed=failed if failed is not None else len(failures), skipped=skipped, failures=failures)
