"""Log triage: any Godot or SDK log reduced to its unique errors and warnings.

Lines that differ only in an id, a number, a coordinate or a path's line number are one group, with
a count, the first line it appeared on, the first raw example and the first stack origin under it.
Known noise (tools/headless/known_noise.json) is counted, not listed.
"""
from __future__ import annotations

import json
import re
from pathlib import Path

from quality_common import ROOT

ERROR = re.compile(r"^(?:SCRIPT ERROR:|USER ERROR:|ERROR:|\[ERROR\]|Unhandled exception|Unhandled Exception:|.*System\.[A-Za-z.]+Exception:|.*\berror [A-Z]+\d+:)")
WARNING = re.compile(r"^(?:USER WARNING:|WARNING:|\[WARN\]|.*\bwarning [A-Z]+\d+:)")
# Godot's `   at: function (file:line)`, a GDScript `   at: res://x.gd:12` and a .NET stack frame.
ORIGIN = re.compile(r"^\s+at:?\s+(.*)$")
FRAME_FILE = re.compile(r"([\w.]+)\(.*?\) in .*?([\w.-]+\.(?:cs|gd)):line (\d+)")
MARKERS = ("EMBERVALE_RESULT", "STALE_BINARY", "zz-summary", "the cast button did not start",
           "ran without its condition being met")
NOISE_FILE = "tools/headless/known_noise.json"


def known_noise(root: Path = ROOT) -> list[str]:
    try:
        return list(json.loads((Path(root) / NOISE_FILE).read_text(encoding="utf-8"))["patterns"])
    except (OSError, ValueError, KeyError, TypeError):
        return []


def normalise(line: str) -> str:
    """The grouping key: the line with everything that varies between occurrences replaced."""
    line = re.sub(r"^\s*\[?\d{1,2}:\d{2}:\d{2}(?:[.,]\d+)?\]?\s*", "", line.strip())      # leading clock
    line = re.sub(r"\(\s*-?\d+(?:\.\d+)?(?:\s*,\s*-?\d+(?:\.\d+)?)+\s*\)", "(#)", line)   # vectors
    line = re.sub(r"<([A-Za-z_]\w*)#-?\d+>", r"<\1#>", line)                               # <Node3D#123>
    # Hashes and ids: 0x…, or eight or more hex characters with a digit among them (so not a word).
    line = re.sub(r"\b0x[0-9a-fA-F]+\b|(?<![A-Za-z0-9])(?=[a-f]*\d)[0-9a-f]{8,}(?![A-Za-z0-9])", "#", line)
    line = re.sub(r"(?<![A-Za-z])-?\d+(?:\.\d+)?(?![A-Za-z])", "#", line)                 # numbers, not Node3D
    return line


def origin_of(line: str) -> str | None:
    match = ORIGIN.match(line)
    if not match:
        return None
    frame = FRAME_FILE.search(match.group(1))
    if frame:
        return f"{'.'.join(frame.group(1).split('.')[-2:])} {frame.group(2)}:{frame.group(3)}"
    return match.group(1).strip()[:160]


def triage(text: str, noise=()) -> dict:
    """{"groups": [{severity, count, line, message, origin}], "noise": n, "markers": [...],
    "lines": n, "result": parsed EMBERVALE_RESULT or None}. Groups are errors first, then by count."""
    noise = [re.compile(p) for p in noise]
    groups, order = {}, []
    suppressed, markers, result, last = 0, [], None, None
    lines = text.splitlines()
    for number, raw in enumerate(lines, start=1):
        stripped = raw.strip()
        if not stripped:
            continue
        if last is not None and last["origin"] is None and (origin := origin_of(raw)):
            last["origin"] = origin
            continue
        error, warning = ERROR.search(stripped), WARNING.search(stripped)
        if not error and not warning:
            if stripped.startswith("EMBERVALE_RESULT"):
                try:
                    result = json.loads(stripped[len("EMBERVALE_RESULT"):])
                except ValueError:
                    pass
            if any(m in stripped for m in MARKERS) and len(markers) < 20:
                markers.append(dict(line=number, text=stripped[:300]))
            if not raw[:1].isspace():
                last = None
            continue
        if any(p.search(stripped) for p in noise):
            suppressed += 1
            last = None
            continue
        key = ("error" if error else "warning", normalise(stripped))
        if key in groups:
            groups[key]["count"] += 1
            last = None   # the origin of the first occurrence is the one reported
            continue
        last = groups[key] = dict(severity=key[0], count=1, line=number, message=stripped, origin=None)
        order.append(key)
    ranked = sorted((groups[k] for k in order), key=lambda g: (g["severity"] != "error", -g["count"], g["line"]))
    return dict(groups=ranked, noise=suppressed, markers=markers, lines=len(lines), result=result)


def render(report: dict, top: int = 30, errors_only: bool = False, source: str = "") -> list[str]:
    """One line per group, then the markers, then one totals line."""
    groups = [g for g in report["groups"] if not errors_only or g["severity"] == "error"]
    out = []
    for group in groups[:top]:
        message = group["message"] if len(group["message"]) <= 220 else group["message"][:219] + "…"
        line = f"{'E' if group['severity'] == 'error' else 'W'} x{group['count']} L{group['line']} {message}"
        out.append(line + (f" | {group['origin']}" if group["origin"] else ""))
    if len(groups) > top:
        out.append(f"+{len(groups) - top} more groups (--top N)")
    for marker in report["markers"]:
        out.append(f"M L{marker['line']} {marker['text'][:220]}")
    errors = [g for g in report["groups"] if g["severity"] == "error"]
    warnings = [g for g in report["groups"] if g["severity"] == "warning"]
    out.append(f"LOGS errors={len(errors)}({sum(g['count'] for g in errors)}) "
               f"warnings={len(warnings)}({sum(g['count'] for g in warnings)}) "
               f"noise={report['noise']} lines={report['lines']}" + (f" source={source}" if source else ""))
    return out
