"""Versioned result contract and conservative diagnostic extraction for legacy tools."""
from __future__ import annotations

import json
import re

SCHEMA = 1
RESULT = "EMBERVALE_RESULT"
EXIT_CODES = {0: "passed", 1: "validation_failed", 2: "configuration_error",
              3: "timeout", 4: "crash", 5: "assertion_failed", 130: "interrupted"}


def diagnostic(severity, code, message, path=None, node=None):
    return dict(severity=severity, code=code, path=path, node=node, message=message)


def parse_result(output: str):
    """The last `EMBERVALE_RESULT {json}` line a headless gate printed, as a dict, or None."""
    for line in reversed(output.splitlines()):
        line = line.strip()
        if line.startswith(RESULT):
            try:
                data = json.loads(line[len(RESULT):])
            except ValueError:
                return None
            return data if isinstance(data, dict) else None
    return None


def result_brief(report: dict) -> str:
    """One line for a gate report: verdict and its facts, lists shown as counts."""
    facts = []
    for key, value in (report.get("facts") or {}).items():
        facts.append(f"{key}={len(value)} items" if isinstance(value, (list, dict)) else f"{key}={value}")
    line = f"RESULT {report.get('gate')} {'ok' if report.get('ok') else 'FAILED'}"
    return line + (" " + " ".join(facts) if facts else "")


def first_words(output: str, limit: int = 3) -> str:
    """What a failed tool said for itself: its first few non-blank lines, for a tool whose failure
    matches no error pattern (most Python gates print `<thing> is stale: ...` and exit 1)."""
    lines = [line.strip() for line in output.splitlines() if line.strip() and not line.startswith(RESULT)]
    return " | ".join(lines[:limit]) + (f" | +{len(lines) - limit} lines" if len(lines) > limit else "")


def diagnostics_from_log(output: str, label: str) -> list[dict]:
    result = []
    seen = {}
    for line in output.splitlines():
        # Do not match prose such as "0 errors" or a test named ErrorHandling.
        error = re.search(r"^(?:SCRIPT ERROR:|ERROR:|\[ERROR\]|Unhandled exception|Unhandled Exception:|.*System\.[A-Za-z]+Exception:|.*\berror [A-Z]+\d+:)", line.strip())
        warning = re.search(r"^(?:WARNING:|\[WARN\]|.*\bwarning [A-Z]+\d+:)", line.strip())
        if not error and not warning:
            continue
        # The editor-bound Godot-MCP addon logs its relay being down as ERROR on every editor
        # launch; that is tooling state, not a project diagnostic, so it is neither an error nor a
        # warning (a warning would still count against --max-warnings and fail --strict).
        if "[McpPlugin]" in line:
            continue
        if line in seen:
            seen[line]["count"] = seen[line].get("count", 1) + 1
            continue
        path = re.search(r"res://[^\s\"']+|[\w./\\:-]+\.(?:cs|gd|tscn|tres)(?:\(\d+,\d+\))?", line)
        item = diagnostic("error" if error else "warning", "process." + label,
                          line.strip(), path.group(0) if path else None)
        seen[line] = item
        result.append(item)
    # A quiet gate names its failures only in its result line; one already printed as an [ERROR]
    # line is not repeated. Warnings stay with the caller that owns the gate's report.
    report = parse_result(output)
    for failure in (report or {}).get("failures") or []:
        text = str(failure)
        if not any(d["severity"] == "error" and text in d["message"] for d in result):
            result.append(diagnostic("error", "process." + label, text))
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
