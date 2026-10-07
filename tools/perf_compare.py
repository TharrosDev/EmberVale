#!/usr/bin/env python3
"""One comparer for every performance JSON this repo produces.

    python tools/perf_compare.py CURRENT.json [MORE.json ...]     compare with this machine's baseline
    python tools/perf_compare.py CURRENT.json --update            record it as the baseline
    python tools/perf_compare.py NEW.json --baseline OLD.json     compare two runs directly

It reads any of them: the `--perf-report` session report and every other EMBERVALE_RESULT file
(their `facts`), the SDK `perf` metrics.json, `vfxperf_<tier>.json`, the world perf probe's report and
the streaming stress report. Nested objects are flattened to dotted keys and only numbers are compared.

BASELINES are keyed by machine, because a frame time from another machine is not a baseline:
`tests/performance_baselines/<suite>/<key>.<machine>.json`. `suite` comes from the file (`suite`, or
the gate name), `key` from --key or the file's own tier/mode/region/view fields (else its name), and
`machine` is a hash of this host, OS, CPU architecture and the renderer/adapter the file names.
No baseline for this machine is "NO BASELINE" and exit 0 (--require-baseline makes it exit 2).

A value REGRESSES when it is worse than the baseline by more than --tolerance (default 10%) AND by
more than an absolute floor for its kind (0.5 ms, 16 draw calls, 8 MB ...), so noise on a small
number does not fail a run. Which way is worse is decided by the key's name (RULES below); keys that
are settings or counts of work done (`frames`, `seconds`, `seed`) are informational.

OUTPUT is one line per regression or improvement and one summary line; --json prints one JSON line
instead. EXIT CODES: 0 no regression (or nothing to compare with) · 5 at least one regression ·
2 unusable input, a refused --update, or a missing baseline under --require-baseline.
"""
from __future__ import annotations

import argparse
import datetime as dt
import fnmatch
import hashlib
import json
import math
import platform
import re
import statistics
import subprocess
import sys
from pathlib import Path

from quality_common import ROOT, write_json

BASELINES = ROOT / "tests/performance_baselines"
EXIT_REGRESSION = 5
SCHEMA = 1

# (glob over one lower-cased segment of the dotted key, which way is better, absolute floor). First
# match wins (see rule_for for which segment is asked); a numeric key nothing matches is
# informational. "info" keys are never compared.
RULES = [
    ("*schema*", "info", 0), ("*seed*", "info", 0), ("*exit_code*", "info", 0), ("*elapsed_ms*", "info", 0),
    ("*over_budget*", "lower", 1),
    ("*seconds*", "info", 0), ("*frames*", "info", 0), ("*_cells", "info", 0), ("*cell_count*", "info", 0),
    ("*casters*", "info", 0), ("*enemies*", "info", 0), ("*cycles*", "info", 0),
    ("*fps*", "higher", 2), ("*casts*", "higher", 2),
    ("*violations*", "lower", 0), ("*errors*", "lower", 0), ("*warnings*", "lower", 0),
    ("*hitch*", "lower", 2), ("*orphan*", "lower", 2), ("*gc*", "lower", 2), ("*alloc*", "lower", 64),
    ("*bytes*", "lower", 8 * 1024 * 1024), ("*_mb*", "lower", 8), ("*mem*", "lower", 8),
    ("*draw*", "lower", 16), ("*prim*", "lower", 5000), ("*particle*", "lower", 50),
    ("*emitter*", "lower", 4), ("*light*", "lower", 2), ("*node*", "lower", 50),
    ("*ms*", "lower", 0.5),
]


# A nested key that ends in one of these is a count of work done, whatever its parent is called.
INFO_LEAVES = {"frames", "seconds", "count", "cells", "cell_count"}


def flatten(value, prefix=""):
    """Dotted numeric leaves of a JSON value. Lists, strings, booleans and non-finite numbers are skipped."""
    flat = {}
    if isinstance(value, dict):
        for key, child in value.items():
            flat.update(flatten(child, f"{prefix}.{key}" if prefix else str(key)))
    elif isinstance(value, (int, float)) and not isinstance(value, bool) and math.isfinite(value):
        flat[prefix] = float(value)
    return flat


def body(document):
    """The object that holds the measurements: `facts` of an EMBERVALE_RESULT, else the document."""
    if isinstance(document.get("facts"), dict) and "gate" in document:
        return document["facts"]
    return document


def _match(segment, rules):
    for pattern, direction, floor in rules:
        if fnmatch.fnmatchcase(segment, pattern):
            return direction, floor
    return None


def rule_for(key, rules=RULES):
    """(direction, floor) for a dotted key. The last segment decides when it names a setting
    (`steadyMs.frames` is informational); otherwise the first segment from the root that any rule
    matches does, so an id used as a key (`castsBySpell.spell.ball_lightning`,
    `cell_ms.lighthouse_w`) cannot be mistaken for a metric name."""
    segments = key.lower().split(".")
    if len(segments) > 1 and segments[-1] in INFO_LEAVES:
        return "info", 0
    for segment in segments:
        found = _match(segment, rules)
        if found:
            return found
    return "info", 0


def _slug(text):
    return re.sub(r"[^a-z0-9_.-]+", "-", str(text).lower()).strip("-.") or "perf"


def identity(document, path=None, key=None):
    """(suite, key) for a perf document."""
    facts = body(document)
    suite = facts.get("suite") or document.get("suite") or document.get("gate") or "perf"
    if key is None:
        parts = [facts[name] for name in ("tier", "mode", "region", "plan", "view")
                 if isinstance(facts.get(name), str) and facts[name]]
        if facts.get("reducedMotion") is True:
            parts.append("reduced")
        key = "-".join(parts) if parts else (Path(path).stem if path else "default")
    return _slug(str(suite).lstrip("-")), _slug(key)


def machine_id(document):
    """Eight hex characters naming this machine and the renderer the document was measured on."""
    facts = body(document)
    renderer = [str(facts.get(name) or document.get(name) or "") for name in ("renderer", "adapter")]
    resolution = facts.get("resolution") or document.get("resolution") or ""
    text = "|".join([platform.node(), platform.system(), platform.machine(), *renderer, json.dumps(resolution)])
    return hashlib.sha1(text.encode("utf-8")).hexdigest()[:8]


def baseline_path(suite, key, machine):
    return BASELINES / suite / f"{key}.{machine}.json"


def median_values(flats):
    """Per-key median over several flattened runs of one measurement (keys present in all of them)."""
    if not flats:
        return {}
    shared = set(flats[0]).intersection(*map(set, flats[1:]))
    return {key: statistics.median(flat[key] for flat in flats) for key in sorted(shared)}


def compare(current, baseline, tolerance=0.10, rules=RULES):
    """Compares two flattened runs. Returns regress/improve lists and counts of the rest."""
    result = dict(regress=[], improve=[], same=0, info=0, new=0, missing=len(set(baseline) - set(current)))
    for key in sorted(current):
        direction, floor = rule_for(key, rules)
        if direction == "info":
            result["info"] += 1
            continue
        if key not in baseline:
            result["new"] += 1
            continue
        now, was = current[key], baseline[key]
        delta = now - was if direction == "lower" else was - now  # positive is worse
        limit = abs(was) * tolerance
        percent = 0.0 if was == 0 else (now - was) / abs(was) * 100.0
        entry = dict(key=key, baseline=was, current=now, percent=round(percent, 1))
        if delta > limit and delta > floor:
            result["regress"].append(entry)
        elif -delta > limit and -delta > floor:
            result["improve"].append(entry)
        else:
            result["same"] += 1
    return result


def _git_head():
    try:
        done = subprocess.run(["git", "rev-parse", "--short", "HEAD"], cwd=ROOT, capture_output=True,
                              text=True, timeout=20)
        return done.stdout.strip() if done.returncode == 0 else ""
    except (OSError, subprocess.SubprocessError):
        return ""


def _load(path):
    document = json.loads(Path(path).read_text(encoding="utf-8"))
    if not isinstance(document, dict):
        raise ValueError(f"{path} is not a JSON object")
    return document


def refusal(document):
    """Why a document must not become a baseline, or None."""
    facts = body(document)
    if facts.get("stale_binary") is True or document.get("stale_binary") is True:
        return "it was measured on a stale binary (rebuild, run again)"
    if document.get("ok") is False:
        return "the run it came from failed"
    return None


def evaluate(paths, baseline=None, update=False, tolerance=0.10, key=None, median=False, force=False,
             rules=RULES):
    """Compares (or records) each file. With median=True the files are repeats of ONE measurement.
    Returns one verdict dict per measurement; verdict["status"] is regress, ok, no-baseline,
    incomparable, updated or refused."""
    documents = [(Path(p), _load(p)) for p in paths]
    groups = [documents] if median else [[item] for item in documents]
    verdicts = []
    for group in groups:
        path, document = group[0]
        suite, name = identity(document, path, key)
        machine = machine_id(document)
        current = median_values([flatten(body(doc)) for _, doc in group])
        target = Path(baseline) if baseline else baseline_path(suite, name, machine)
        verdict = dict(suite=suite, key=name, machine=machine, baseline=str(target), files=[str(p) for p, _ in group])
        if update:
            reason = next((r for r in (refusal(doc) for _, doc in group) if r), None)
            if reason and not force:
                verdict.update(status="refused", reason=reason)
            else:
                write_json(target, dict(schema=SCHEMA, suite=suite, key=name, machine_id=machine,
                                        machine=dict(system=platform.system(), machine=platform.machine()),
                                        captured=dt.datetime.now(dt.timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ"),
                                        git=_git_head(), source=path.name, values=current))
                verdict.update(status="updated", values=len(current))
            verdicts.append(verdict)
            continue
        if not target.is_file():
            verdict.update(status="no-baseline")
            verdicts.append(verdict)
            continue
        stored = _load(target)
        recorded = stored.get("machine_id")
        if recorded and recorded != machine and not force:
            verdict.update(status="incomparable",
                           reason=f"the baseline is from machine {recorded}, this run is {machine}")
            verdicts.append(verdict)
            continue
        old = stored["values"] if isinstance(stored.get("values"), dict) else flatten(body(stored))
        outcome = compare(current, old, tolerance, rules)
        verdict.update(outcome, status="regress" if outcome["regress"] else "ok", tolerance=tolerance,
                       captured=stored.get("captured", ""), git=stored.get("git", ""))
        verdicts.append(verdict)
    return verdicts


def _number(value):
    return f"{value:.2f}" if abs(value) < 100000 else f"{value:.0f}"


def render(verdict):
    """The text lines for one verdict: changed values only, then one summary line."""
    head = f"perf {verdict['suite']}/{verdict['key']}@{verdict['machine']}"
    status = verdict["status"]
    if status == "no-baseline":
        return [f"{head}: NO BASELINE ({verdict['baseline']}); record one with --update"]
    if status == "incomparable":
        return [f"{head}: INCOMPARABLE {verdict['reason']}"]
    if status == "refused":
        return [f"{head}: REFUSED --update: {verdict['reason']} (--force overrides)"]
    if status == "updated":
        return [f"{head}: UPDATED {verdict['baseline']} ({verdict['values']} values)"]
    lines = []
    limit = f"limit {verdict['tolerance'] * 100:.0f}%"
    for label, entries in (("REGRESS", verdict["regress"]), ("IMPROVE", verdict["improve"])):
        for entry in entries:
            lines.append(f"{label} {entry['key']} {_number(entry['baseline'])} -> {_number(entry['current'])} "
                         f"{entry['percent']:+.1f}% ({limit})")
    origin = " ".join(filter(None, [verdict.get("captured", "")[:10], verdict.get("git", "")]))
    lines.append(f"{head}: {len(verdict['regress'])} regress, {len(verdict['improve'])} improve, "
                 f"{verdict['same']} same, {verdict['info']} info, {verdict['new']} new, "
                 f"{verdict['missing']} missing" + (f" (baseline {origin})" if origin else ""))
    return lines


def exit_code(verdicts, require_baseline=False):
    statuses = {v["status"] for v in verdicts}
    if "refused" in statuses or (require_baseline and statuses & {"no-baseline", "incomparable"}):
        return 2
    return EXIT_REGRESSION if "regress" in statuses else 0


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("current", nargs="+", type=Path, help="perf JSON file(s)")
    parser.add_argument("--baseline", type=Path, help="compare with this file instead of the machine-keyed baseline")
    parser.add_argument("--update", action="store_true", help="record the file(s) as the baseline")
    parser.add_argument("--tolerance", type=float, default=0.10, help="relative change that counts (default 0.10)")
    parser.add_argument("--key", help="baseline key (default: from the file's tier/mode/region/view, else its name)")
    parser.add_argument("--median", action="store_true", help="the files are repeats of one measurement: compare their per-key median")
    parser.add_argument("--require-baseline", action="store_true", help="exit 2 when there is nothing to compare with")
    parser.add_argument("--force", action="store_true", help="update from a failed/stale run, or compare across machines")
    parser.add_argument("--json", action="store_true", help="one JSON line instead of text")
    args = parser.parse_args(argv)
    if not math.isfinite(args.tolerance) or args.tolerance < 0:
        print("perf: --tolerance must be a non-negative number")
        return 2
    try:
        verdicts = evaluate(args.current, args.baseline, args.update, args.tolerance, args.key, args.median, args.force)
    except (OSError, ValueError, KeyError) as error:
        print(f"perf: unusable input: {error}")
        return 2
    code = exit_code(verdicts, args.require_baseline)
    if args.json:
        print(json.dumps(dict(exit_code=code, verdicts=verdicts), ensure_ascii=False))
    else:
        for verdict in verdicts:
            print("\n".join(render(verdict)))
    return code


if __name__ == "__main__":
    sys.exit(main())
