"""The compact view of a run: failures and where their logs are, then one verdict line.

A passing run is one line. `--json` prints compact(); `--json-full` keeps the whole summary, which
is always on disk as summary.json next to summary.txt (this text).
"""
from __future__ import annotations

import json
import re
from pathlib import Path

from quality_common import ROOT
from .costs import clock

RUN_ID = re.compile(r"^\d{8}T\d{6}-[0-9a-f]{4,10}$")
MAX_FAILED, MAX_ERRORS, MAX_BRIEF, MESSAGE = 20, 10, 40, 200


def clip(text, limit=MESSAGE) -> str:
    text = " ".join(str(text).split())
    return text if len(text) <= limit else text[:limit - 1] + "…"


def short_path(path) -> str:
    """Relative to the project when it is inside it: shorter, and it still opens."""
    try:
        return Path(path).resolve().relative_to(ROOT.resolve()).as_posix()
    except (ValueError, OSError):
        return str(path)


def compact(result: dict) -> dict:
    """A bounded summary of a full result: counts, failed steps with their first error and log, and
    errors that belong to no step."""
    directory = result.get("artifact_directory") or ""
    steps = result.get("steps", [])
    diagnostics = result.get("diagnostics", [])
    errors = [d for d in diagnostics if d.get("severity") == "error"]
    failed, claimed = [], set()
    for step in steps:
        if step.get("success", not step.get("exit_code")) and not step.get("exit_code"):
            continue
        name = step.get("name", "?")
        own = [d for d in errors if d.get("code") == "process." + name
               or (d.get("code") in ("process.failed", "process.timeout", "process.crash")
                   and d.get("message", "").startswith((name + ":", name + " ")))]
        claimed.update(id(d) for d in own)
        row = dict(step=name, exit_code=step.get("exit_code", 1), message=clip(own[0]["message"]) if own else "")
        if len(own) > 1:
            row["errors"] = sum(d.get("count", 1) for d in own)
        if step.get("stdout"):
            row["log"] = short_path(Path(directory) / step["stdout"])
        failed.append(row)
    loose = [dict(code=d.get("code"), message=clip(d.get("message", "")),
                  **({"path": d["path"]} if d.get("path") else {}))
             for d in errors if id(d) not in claimed]
    if loose:   # a pseudo-step with no message and no log (configuration, prerequisites) only repeats the ERROR line
        failed = [row for row in failed if row["message"] or row.get("log")]
    out = dict(schema=result.get("schema"), run_id=result.get("run_id"), command=result.get("command"),
               success=result.get("success"), exit_code=result.get("exit_code"),
               duration=round(result.get("duration") or 0, 1), steps=len(steps),
               cached=sum(1 for s in steps if s.get("cached")),
               failed=failed[:MAX_FAILED], errors=loose[:MAX_ERRORS],
               warnings=sum(d.get("count", 1) for d in diagnostics if d.get("severity") == "warning"),
               artifact_directory=short_path(directory) if directory else None)
    broken = [a for a in result.get("assertions", []) if not a.get("success")]
    if broken:
        out["assertions_failed"] = [dict(name=clip(a.get("name", "?"), 80), expected=clip(a.get("expected"), 80),
                                         actual=clip(a.get("actual"), 80)) for a in broken[:MAX_ERRORS]]
    if len(failed) > MAX_FAILED:
        out["failed_more"] = len(failed) - MAX_FAILED
    if len(loose) > MAX_ERRORS:
        out["errors_more"] = len(loose) - MAX_ERRORS
    if result.get("partial"):
        out["partial"] = True
    if result.get("label"):
        out["label"] = result["label"]
    brief = result.get("brief")
    if brief:
        out["brief"] = [clip(line, 300) for line in brief[:MAX_BRIEF]]
    return out


def text(result: dict) -> str:
    """The compact summary as lines (see compact())."""
    c = compact(result)
    lines = list(c.get("brief", ()))
    for row in c["failed"]:
        line = f"FAIL {row['step']} exit={row['exit_code']}"
        if row.get("message"):
            line += " " + row["message"]
        if row.get("errors"):
            line += f" (+{row['errors'] - 1} more)"
        if row.get("log"):
            line += f" log={row['log']}"
        lines.append(line)
    if c.get("failed_more"):
        lines.append(f"+{c['failed_more']} more failed steps")
    for item in c["errors"]:
        lines.append(f"ERROR {item['code']}: {item['message']}" + (f" [{item['path']}]" if item.get("path") else ""))
    if c.get("errors_more"):
        lines.append(f"+{c['errors_more']} more errors")
    for item in c.get("assertions_failed", ()):
        lines.append(f"ASSERT {item['name']}: expected {item['expected']}, actual {item['actual']}")
    verdict = "PARTIAL" if c.get("partial") else ("PASS" if c["success"] else "FAIL")
    line = f"{verdict} {c.get('label') or c['command']} {c['steps']} steps"
    if c["cached"]:
        line += f" ({c['cached']} cached)"
    line += f" {clock(c['duration'])}"
    if c["exit_code"]:
        line += f" exit={c['exit_code']}"
    if c["warnings"]:
        line += f" warnings={c['warnings']}"
    lines.append(line + f" evidence={c['artifact_directory']}")
    return "\n".join(lines)


def runs(parent: Path) -> list[Path]:
    """Run directories under a parent, newest first (the id starts with its UTC start time)."""
    parent = Path(parent)
    if not parent.is_dir():
        return []
    return sorted((p for p in parent.iterdir() if p.is_dir() and RUN_ID.match(p.name)),
                  key=lambda p: p.name, reverse=True)


def load(directory: Path) -> dict | None:
    """A run directory's summary, or a stand-in for a run that was killed before it wrote one."""
    directory = Path(directory)
    try:
        return json.loads((directory / "summary.json").read_text(encoding="utf-8"))
    except (OSError, ValueError):
        pass
    try:
        progress = json.loads((directory / "progress.json").read_text(encoding="utf-8"))
    except (OSError, ValueError):
        return None
    return dict(run_id=directory.name, command=progress.get("command"), success=None, exit_code=None, partial=True,
                duration=0, steps=[], diagnostics=[], artifact_directory=str(directory),
                brief=[f"killed during step {progress.get('step')}"])
