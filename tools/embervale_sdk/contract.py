"""Versioned result contract and conservative diagnostic extraction for legacy tools."""
from __future__ import annotations

import re

SCHEMA = 1
EXIT_CODES = {0: "passed", 1: "validation_failed", 2: "configuration_error",
              3: "timeout", 4: "crash", 5: "assertion_failed", 130: "interrupted"}


def diagnostic(severity, code, message, path=None, node=None):
    return dict(severity=severity, code=code, path=path, node=node, message=message)


def diagnostics_from_log(output: str, label: str) -> list[dict]:
    result = []
    seen = {}
    for line in output.splitlines():
        # Do not match prose such as "0 errors" or a test named ErrorHandling.
        error = re.search(r"^(?:SCRIPT ERROR:|ERROR:|\[ERROR\]|Unhandled exception|Unhandled Exception:|.*System\.[A-Za-z]+Exception:|.*\berror [A-Z]+\d+:)", line.strip())
        warning = re.search(r"^(?:WARNING:|\[WARN\]|.*\bwarning [A-Z]+\d+:)", line.strip())
        if not error and not warning:
            continue
        if line in seen:
            seen[line]["count"] = seen[line].get("count", 1) + 1
            continue
        path = re.search(r"res://[^\s\"']+|[\w./\\:-]+\.(?:cs|gd|tscn|tres)(?:\(\d+,\d+\))?", line)
        item = diagnostic("error" if error else "warning", "process." + label,
                          line.strip(), path.group(0) if path else None)
        seen[line] = item
        result.append(item)
    return result


def exit_code(result: dict, strict: bool = False) -> int:
    codes = [step.get("exit_code", 0) for step in result["steps"]]
    for code in (130, 3, 2, 4, 5):
        if code in codes:
            return code
    if any(not a["success"] for a in result["assertions"]):
        return 5
    if any(codes) or any(d["severity"] == "error" or
                         (strict and d["severity"] == "warning") for d in result["diagnostics"]):
        return 1
    return 0
