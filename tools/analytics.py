#!/usr/bin/env python3
"""Read the game's analytics logs (the JSON-lines files AnalyticsSink writes, one per session).

    python tools/analytics.py summary                  every session in the default directory
    python tools/analytics.py summary --latest         only the newest session
    python tools/analytics.py summary --last 10        the ten newest
    python tools/analytics.py summary RUN_DIR FILE...  these files, or every session_*.jsonl under a directory
    python tools/analytics.py summary --json           one JSON line instead of text

The summary is: sessions and play time, the player's deaths by killer, kills by enemy, damage taken
and dealt by source/target, gold in and out by source, and how long each quest took in play seconds
(median over the sessions that finished it) with the ones started and never finished.

The default directory is the game's user directory (`analytics/` under it). An SDK run writes into
its own artifacts instead: pass that run directory. Sessions from before the sink wrote totals
(no `session_end` row) still count for deaths, kills and quests; they have no damage or gold, and
their quest times are wall-clock (`pt` did not exist), which the output marks with `~`.

EXIT CODES: 0 summarised · 2 nothing to read.
"""
from __future__ import annotations

import argparse
import json
import os
import statistics
import sys
from collections import Counter, defaultdict
from pathlib import Path

PLAYER = "player"
TOP = 6


def default_directory():
    """`analytics/` in the game's user directory (Godot's app_userdata/Embervale)."""
    override = os.environ.get("EMBERVALE_USER_DIR")
    if override:
        return Path(override) / "analytics"
    if sys.platform == "win32":
        base = Path(os.environ.get("APPDATA", Path.home() / "AppData/Roaming")) / "Godot"
    elif sys.platform == "darwin":
        base = Path.home() / "Library/Application Support/Godot"
    else:
        base = Path(os.environ.get("XDG_DATA_HOME", Path.home() / ".local/share")) / "godot"
    return base / "app_userdata/Embervale/analytics"


def find_sessions(paths):
    """Session files named by the arguments: a file is itself, a directory is every session under it."""
    files = []
    for path in paths:
        path = Path(path)
        if path.is_dir():
            files.extend(p for p in path.rglob("session_*.jsonl"))
        elif path.is_file():
            files.append(path)
    return sorted(set(files), key=lambda p: (p.stat().st_mtime, p.name))


def read_rows(path):
    """The rows of one session file. A truncated or damaged line is skipped, not fatal."""
    rows = []
    for line in Path(path).read_text(encoding="utf-8", errors="replace").splitlines():
        line = line.strip()
        if not line:
            continue
        try:
            row = json.loads(line)
        except json.JSONDecodeError:
            continue
        if isinstance(row, dict) and "type" in row:
            rows.append(row)
    return rows


def _is_player(name):
    return isinstance(name, str) and name.lower() == PLAYER


def summarize(sessions):
    """One summary over a list of sessions (each a list of rows)."""
    summary = dict(sessions=0, automation=0, rows=0, play_seconds=0.0, player_deaths=0, max_level=0,
                   deaths_by_killer=Counter(), deaths_by_region=Counter(), kills=Counter(),
                   damage_taken=defaultdict(lambda: [0.0, 0]), damage_dealt=defaultdict(lambda: [0.0, 0]),
                   gold_in=Counter(), gold_out=Counter(), quests={}, wall_clock_quests=False)
    quests = defaultdict(lambda: dict(started=0, completed=0, failed=0, seconds=[]))
    for rows in sessions:
        if not rows:
            continue
        summary["sessions"] += 1
        summary["rows"] += len(rows)
        summary["play_seconds"] += max((r.get("pt", 0.0) for r in rows if isinstance(r.get("pt"), (int, float))), default=0.0)
        started = {}
        end = None
        for row in rows:
            kind = row["type"]
            if kind == "session_start" and row.get("automation"):
                summary["automation"] += 1
            elif kind == "death":
                entity, killer = row.get("entity", ""), row.get("killer", "")
                if row.get("player") or _is_player(entity):
                    summary["player_deaths"] += 1
                    summary["deaths_by_killer"][killer or "unknown"] += 1
                    summary["deaths_by_region"][row.get("region") or "unknown"] += 1
                elif _is_player(killer):
                    summary["kills"][entity or "unknown"] += 1
            elif kind == "quest_start":
                quests[row.get("quest", "")]["started"] += 1
                started[row.get("quest", "")] = row
            elif kind in ("quest_complete", "quest_fail"):
                quest = quests[row.get("quest", "")]
                quest["completed" if kind == "quest_complete" else "failed"] += 1
                seconds = row.get("seconds")
                begun = started.pop(row.get("quest", ""), None)
                if seconds is None and begun is not None:
                    # A session from before `seconds`: play time when both rows have it, else wall clock.
                    if "pt" in row and "pt" in begun:
                        seconds = row["pt"] - begun["pt"]
                    elif "t" in row and "t" in begun:
                        seconds = row["t"] - begun["t"]
                        summary["wall_clock_quests"] = True
                if kind == "quest_complete" and isinstance(seconds, (int, float)):
                    quest["seconds"].append(float(seconds))
            elif kind == "level_up":
                summary["max_level"] = max(summary["max_level"], int(row.get("level", 0)))
            elif kind == "gold":
                delta = int(row.get("delta", 0))
                (summary["gold_in"] if delta > 0 else summary["gold_out"])[row.get("source", "other")] += abs(delta)
            elif kind == "session_end":
                end = row
        if end:
            for entry in end.get("damage", []):
                source, target = entry.get("source", ""), entry.get("target", "")
                table = summary["damage_dealt"] if _is_player(source) else summary["damage_taken"] if _is_player(target) else None
                if table is not None:
                    other = target if _is_player(source) else source
                    table[other][0] += float(entry.get("total", 0))
                    table[other][1] += int(entry.get("hits", 0))
    for name, quest in sorted(quests.items()):
        summary["quests"][name] = dict(started=quest["started"], completed=quest["completed"], failed=quest["failed"],
                                       median_seconds=round(statistics.median(quest["seconds"]), 1) if quest["seconds"] else None)
    for key in ("deaths_by_killer", "deaths_by_region", "kills", "gold_in", "gold_out"):
        summary[key] = dict(summary[key].most_common())
    for key in ("damage_taken", "damage_dealt"):
        ranked = sorted(summary[key].items(), key=lambda item: -item[1][0])
        summary[key] = {name: dict(total=round(total, 1), hits=hits) for name, (total, hits) in ranked}
    summary["play_seconds"] = round(summary["play_seconds"], 1)
    return summary


def _top(counts, limit=TOP):
    items = list(counts.items())
    shown = ", ".join(f"{name} {value}" for name, value in items[:limit])
    return shown + (f", +{len(items) - limit} more" if len(items) > limit else "")


def _damage(table, limit=TOP):
    items = list(table.items())
    shown = ", ".join(f"{name} {entry['total']:.0f}/{entry['hits']} hits" for name, entry in items[:limit])
    return shown + (f", +{len(items) - limit} more" if len(items) > limit else "")


def render(summary, quest_lines=12):
    """The compact text form: one line per topic, then the slowest quests."""
    lines = [f"analytics: {summary['sessions']} session(s), {summary['rows']} rows, play {summary['play_seconds']:.0f} s"
             f" ({summary['automation']} automation), max level {summary['max_level']}"]
    lines.append(f"deaths: player {summary['player_deaths']}" +
                 (f" by {_top(summary['deaths_by_killer'])}" if summary["deaths_by_killer"] else "") +
                 (f" in {_top(summary['deaths_by_region'])}" if summary["deaths_by_region"] else ""))
    lines.append(f"kills: {sum(summary['kills'].values())}" + (f" ({_top(summary['kills'])})" if summary["kills"] else ""))
    taken, dealt = summary["damage_taken"], summary["damage_dealt"]
    if taken or dealt:
        lines.append(f"damage taken: {sum(e['total'] for e in taken.values()):.0f}" + (f" ({_damage(taken)})" if taken else ""))
        lines.append(f"damage dealt: {sum(e['total'] for e in dealt.values()):.0f}" + (f" ({_damage(dealt)})" if dealt else ""))
    else:
        lines.append("damage: none recorded (totals are in session_end rows; older sessions have none)")
    gained, spent = sum(summary["gold_in"].values()), sum(summary["gold_out"].values())
    lines.append(f"gold: +{gained} -{spent} net {gained - spent:+d}" +
                 (f" | in: {_top(summary['gold_in'])}" if gained else "") +
                 (f" | out: {_top(summary['gold_out'])}" if spent else ""))
    quests = summary["quests"]
    done = sum(1 for q in quests.values() if q["completed"])
    unfinished = sorted(name for name, q in quests.items() if q["started"] and not q["completed"] and not q["failed"])
    lines.append(f"quests: {len(quests)} seen, {done} completed, {sum(1 for q in quests.values() if q['failed'])} failed, "
                 f"{len(unfinished)} started and never finished")
    timed = sorted(((name, q) for name, q in quests.items() if q["median_seconds"] is not None),
                   key=lambda item: -item[1]["median_seconds"])
    mark = "~" if summary["wall_clock_quests"] else ""
    for name, quest in timed[:quest_lines]:
        lines.append(f"  {name}  n={quest['completed']}  median {mark}{quest['median_seconds']:.0f} s")
    if len(timed) > quest_lines:
        lines.append(f"  ... {len(timed) - quest_lines} quicker quest(s) not shown (--json has all)")
    if unfinished:
        lines.append("  unfinished: " + ", ".join(unfinished[:quest_lines]) +
                     (f", +{len(unfinished) - quest_lines} more" if len(unfinished) > quest_lines else ""))
    return "\n".join(lines)


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("command", choices=("summary",))
    parser.add_argument("paths", nargs="*", type=Path, help="session files or directories (default: the user directory)")
    parser.add_argument("--dir", type=Path, help="a directory to read instead of the default")
    parser.add_argument("--latest", action="store_true", help="only the newest session")
    parser.add_argument("--last", type=int, help="only the N newest sessions")
    parser.add_argument("--json", action="store_true", help="one JSON line instead of text")
    args = parser.parse_args(argv)
    sources = list(args.paths) or [args.dir or default_directory()]
    files = find_sessions(sources)
    if args.latest:
        files = files[-1:]
    elif args.last:
        files = files[-max(1, args.last):]
    if not files:
        print(f"analytics: no session_*.jsonl under {', '.join(str(s) for s in sources)}")
        return 2
    summary = summarize([read_rows(path) for path in files])
    summary["files"] = len(files)
    summary["newest"] = str(files[-1])
    print(json.dumps(summary, ensure_ascii=False) if args.json else render(summary) + f"\nnewest: {files[-1]}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
