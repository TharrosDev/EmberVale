"""Run a C# screenshot suite, or take one picture, and read the result back as one line.

    python tools/embervale.py shots list                       # the suites (no engine)
    python tools/embervale.py shots panelshots --list          # a suite's shot names
    python tools/embervale.py shots panelshots --only "0?-map*"
    python tools/embervale.py shots spellshots --spell-filter fire --film 16x3
    python tools/embervale.py shots shot --cell ember_crown.embermarket --yaw 90 --pitch -20 --hour 19.5
    python tools/embervale.py shots shot --ui panelshots/00-map
    python tools/embervale.py shots shot --spell fireball --phase impact --film
    python tools/embervale.py shots shot --spec views.json

What it adds to launching the flag by hand: the stale-binary rebuild, an isolated user directory
with a fresh game in it (or `--save DIR`, a save slot folder copied in), a fixed 60 fps step so
timing does not depend on machine load, a timeout that fits a foreground command (540 s unless
--timeout), the run's manifest read into the result, labelled contact sheets, and a comparison with
`--baseline DIR` or with the previous run of the same suite (`--diff-last`).

It prints one line, `SHOTS {json}`: suite, ok, captured, failed, flagged, dir, sheets, and what
changed. Open a sheet first; open a full PNG only for a shot that is failed, flagged or changed.
"""
from __future__ import annotations

import json
import re
import shutil
from pathlib import Path

HELP = "run a screenshot suite or one --shot; result as one JSON line plus contact sheets"

# Suite name -> the game's flag. The session suites are SessionHarnesses.All (src/Bootstrap); the test
# in tools/test_shots_command.py fails when the two lists drift.
SESSION_SUITES = {
    "hudshots": "--hudshots", "panelshots": "--panelshots", "uishots": "--uishots",
    "shrine-shots": "--shrine-shots", "guild-shots": "--guild-shots", "enemy-shots": "--enemy-shots",
    "combat-shots": "--combat-shots", "look-shots": "--look-shots", "metashots": "--metashots",
    "tradeshots": "--tradeshots", "spellshots": "--spellshots", "camshots": "--camshots", "shot": "--shot",
}
TITLE_SUITES = {"shellshots": "--shellshots"}
SUITES = {**SESSION_SUITES, **TITLE_SUITES}
INPUT_SUITES = {"spellshots", "camshots", "shot"}
DEFAULT_TIMEOUT = 540
RESULT_PREFIX = "EMBERVALE_RESULT "
# (flag, kind) of the one-off request; each is passed to the game as --flag=value.
SHOT_VALUES = ("name", "cell", "location", "at", "yaw", "pitch", "distance", "height", "fov", "hour",
               "weather", "view", "settle", "ui", "spell", "phase")
SHOT_SWITCHES = ("hud", "show-player")


def arguments(parser):
    parser.add_argument("--only", help="comma list of shot names to photograph (* and ? are wildcards)")
    parser.add_argument("--list", action="store_true", help="print the suite's shot names; nothing is rendered")
    parser.add_argument("--film", nargs="?", const="", metavar="FRAMESxSTRIDE",
                        help="timed suites and shot: one filmstrip PNG per shot (default 12x4)")
    parser.add_argument("--save", type=Path, help="a save slot folder to continue instead of starting a new game")
    parser.add_argument("--ui-scale", type=float, help="UI scale for the run (0.75..1.5)")
    parser.add_argument("--no-thumbs", action="store_true")
    parser.add_argument("--no-analyze", action="store_true", help="skip the contact sheets and comparisons")
    parser.add_argument("--diff-last", action="store_true", help="compare with the previous run of this suite")
    parser.add_argument("--input-route", action="store_true",
                        help="spell/cam suites: press real buttons (needs a focused window) instead of driving components")
    parser.add_argument("--movie", action="store_true", help="also record the run with the engine's --write-movie (AVI)")
    parser.add_argument("--spell-filter", help="spellshots: comma list of spell ids or schools")
    parser.add_argument("--spell-view", choices=("tp", "fp", "both"), help="spellshots: which views")
    parser.add_argument("--tier", help="spell effect tier: performance|low|medium|high|ultra")
    parser.add_argument("--spec", type=Path, help="shot: a JSON file of one or more requests")
    for name in SHOT_VALUES:
        parser.add_argument("--" + name, help=f"shot: {name}")
    for name in SHOT_SWITCHES:
        parser.add_argument("--" + name, action="store_true", help=f"shot: {name}")


def user_arguments(args, suite, save_slot=None):
    """The arguments after `--` for one run. Pure, so it is unit-tested."""
    flag = SUITES[suite]
    user = [flag]
    if suite == "shot":
        if args.spec:
            user = [f"{flag}={Path(args.spec).resolve()}"]
        for name in SHOT_VALUES:
            value = getattr(args, name.replace("-", "_"), None)
            if value is not None:
                user.append(f"--{name}={value}")
        for name in SHOT_SWITCHES:
            if getattr(args, name.replace("-", "_"), False):
                user.append("--" + name)
        if args.region:
            user.append(f"--region={args.region}")
    if suite in SESSION_SUITES:
        user.append(f"--slot={save_slot}" if save_slot else "--new-game")
    if args.only:
        user.append(f"--only={args.only}")
    if args.list:
        user.append("--list")
    if args.film is not None:
        user.append("--film" + (f"={args.film}" if args.film else ""))
    if args.no_thumbs:
        user.append("--no-thumbs")
    if suite in INPUT_SUITES and not args.input_route:
        user.append("--direct-input")
    return user


def environment(args):
    """The variables a run sets for the game beyond the SDK's own."""
    env = {}
    if args.resolution:
        env["EMBERVALE_RES"] = args.resolution[0]
    if args.ui_scale is not None:
        env["EMBERVALE_SHOT_UISCALE"] = str(args.ui_scale)
    if args.spell_filter:
        env["EMBERVALE_SPELLSHOTS_FILTER"] = args.spell_filter
    if args.spell_view and args.spell_view != "both":
        env["EMBERVALE_SPELLSHOTS_VIEW"] = args.spell_view
    if args.tier:
        env["EMBERVALE_SPELLSHOTS_TIER"] = args.tier
    return env


def result_line(output):
    """The last EMBERVALE_RESULT object in a process's output, or None."""
    for line in reversed(output.splitlines()):
        at = line.find(RESULT_PREFIX)
        if at >= 0:
            try:
                return json.loads(line[at + len(RESULT_PREFIX):])
            except ValueError:
                return None
    return None


def find_manifest(artifacts):
    """The manifest a run wrote: <artifacts>/<suite folder>/manifest.json (a --shot that asked another
    suite for a state writes into that suite's folder)."""
    found = sorted(p for p in Path(artifacts).glob("*/manifest.json") if p.parent.name != "user")
    return found[0] if found else None


def summarize(manifest, directory):
    """The compact view of a manifest: what an agent needs before opening any image."""
    shots = manifest.get("shots", [])
    return dict(suite=str(manifest.get("suite") or "").lstrip("-"), ok=bool(manifest.get("ok")), registered=manifest.get("registered", len(shots)),
                captured=manifest.get("captured", 0), seconds=manifest.get("seconds"),
                # A timed suite's closing zz-summary check is driven but writes no image in a filtered or one-off run.
                selected=sum(1 for x in shots if x.get("selected") and (x.get("file") or x.get("name") != "zz-summary")),
                failed=manifest.get("failed", []), flagged={s["name"]: s["flags"] for s in shots if s.get("flags")},
                focus_lost=manifest.get("focus_lost", []), films=[s["film"] for s in shots if s.get("film")],
                dir=str(directory), manifest=str(Path(directory) / "manifest.json"))


def analyze(directory, manifest, baseline=None):
    """Contact sheets for the run and, with a baseline directory, what changed against it."""
    import shot_analyze
    directory = Path(directory)
    names = [s["file"] for s in manifest.get("shots", []) if s.get("file")]
    paths = [directory / name for name in names if (directory / name).is_file()]
    flagged = {s["file"] for s in manifest.get("shots", []) if s.get("file") and (s.get("flags") or s.get("problem"))}
    # One image needs no contact sheet: the sheet would be a smaller copy of it.
    out = dict(sheets=[str(p) for p in shot_analyze.make_sheets(paths, directory / "sheet.png", flagged=flagged)]
               if len(paths) > 1 else [])
    if baseline and Path(baseline).is_dir():
        results = shot_analyze.diff_dirs(directory, baseline, directory / "diff")
        out["baseline"] = str(baseline)
        out["changed"] = {r["image"]: dict(pct=r["changed_pct"], boxes=r["boxes"], triptych=r.get("triptych"))
                          for r in results if r.get("changed") and not r.get("missing_current")}
        out["missing_baseline"] = [r["image"] for r in results if r.get("missing_baseline")]
        # In the baseline, not captured by this run: expected after --only, a defect otherwise.
        out["missing_current"] = [r["image"] for r in results if r.get("missing_current")]
    return out


def wrapped(head, names, width=280):
    """Names packed into lines short enough for the compact output (it clips a line at 300)."""
    lines, line = [], head
    for name in names:
        if len(line) + 1 + len(name) > width:
            lines.append(line)
            line = " "
        line += " " + name
    return lines + [line]


def brief_lines(summary):
    """The run in a few plain lines: what an agent reads before it opens an image."""
    from ..compact import short_path
    lines = [f"SHOTS {summary['suite']} {'ok' if summary['ok'] else 'FAILED'} captured {summary['captured']}"
             f"/{summary['selected']} selected ({summary['registered']} registered) in {summary['seconds']}s dir={short_path(summary['dir'])}"]
    if summary["failed"]:
        lines += wrapped("  failed:", summary["failed"])
    if summary["flagged"]:
        lines += wrapped("  flagged:", [f"{name}[{','.join(flags)}]" for name, flags in summary["flagged"].items()])
    if summary["focus_lost"]:
        lines += wrapped("  focus lost:", summary["focus_lost"])
    if summary.get("films"):
        lines += wrapped("  films:", summary["films"])
    if summary.get("sheets"):
        lines += wrapped("  sheets:", [short_path(p) for p in summary["sheets"]])
    if "baseline" in summary:
        changed = summary.get("changed", {})
        lines.append(f"  against {short_path(summary['baseline'])}: {len(changed)} changed, "
                     f"{len(summary.get('missing_baseline', []))} with no baseline")
        lines += [f"    {image} {row['pct']}% {short_path(row['triptych']) if row.get('triptych') else ''}".rstrip()
                  for image, row in list(changed.items())[:10]]
    if summary.get("movie"):
        lines.append(f"  movie: {short_path(summary['movie'])}")
    return lines


def run(run, args, passthrough):
    suite = (args.target or "").lstrip("-")
    suite = {"combatshots": "combat-shots"}.get(suite, suite)
    if suite == "list" or not suite:
        run.result["metrics"]["shots"] = dict(suites=sorted(SUITES))
        run.note("SHOTS " + json.dumps(dict(suites=sorted(SUITES)), ensure_ascii=False))
        for line in wrapped("SHOTS suites:", sorted(SUITES)):
            run.brief(line)
        return
    if suite == "shot" and (args.ui or "").split("/")[0].lstrip("-") == "shellshots":
        # The title suite has no session to attach --shot to; it takes the same filter directly.
        suite, args.only, args.ui = "shellshots", args.ui.split("/", 1)[1] if "/" in args.ui else None, None
    if suite not in SUITES:
        raise ValueError(f"unknown suite '{suite}'; one of: {', '.join(sorted(SUITES))} (--vfxperf measures real "
                         "frame times and is launched directly, not through this fixed-step runner)")
    resolution = (args.resolution or ["1280x720"])[0]
    if not re.fullmatch(r"[1-9]\d{2,3}x[1-9]\d{2,3}", resolution):
        raise ValueError("resolution must be WIDTHxHEIGHT")
    if args.timeout is None:
        run.timeout = min(run.timeout, DEFAULT_TIMEOUT)

    save_slot = None
    if args.save:
        source = Path(args.save)
        if not (source / "save.json").is_file():
            raise ValueError(f"--save needs a slot folder holding save.json: {source}")
        save_slot = source.name
        shutil.copytree(source, run.artifacts / "user" / "saves" / save_slot)

    run.env.update(environment(args))
    engine = ["--fixed-fps", "60", "--resolution", resolution]
    if args.movie:
        engine += ["--write-movie", str(run.artifacts / f"{suite}.avi")]
    step = run.godot("shots-" + suite, engine, user_arguments(args, suite, save_slot), render=not args.list)

    if args.list:
        listed = result_line(step.output) or {}
        names = listed.get("facts", {}).get("shots")
        if names is None:
            run.issue("shots.incomplete", "the suite did not print its shot list; see the step's stdout log")
            return
        run.result["metrics"]["shots"] = dict(suite=suite, shots=names)
        run.note("SHOTS " + json.dumps(dict(suite=suite, count=len(names), shots=names), ensure_ascii=False))
        for line in wrapped(f"SHOTS {suite} {len(names)} shots:", names):
            run.brief(line)
        return

    manifest_path = find_manifest(run.artifacts)
    if manifest_path is None:
        reported = result_line(step.output) or {}
        for failure in reported.get("failures", []):
            run.issue("shots.failed", failure)
        if reported.get("failures"):
            return  # the harness said why it stopped; "wrote no manifest" would only repeat it
        run.issue("shots.incomplete", "the run wrote no manifest.json: it did not reach its first shot "
                  "(no session, a crash, or the timeout); see the step's stdout log")
        return
    manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
    directory = manifest_path.parent
    summary = summarize(manifest, directory)
    for shot in manifest.get("shots", []):
        if shot.get("selected") and shot.get("problem"):
            run.issue("shots.failed", shot["problem"], path=str(directory / (shot["name"] + ".png")))
    for failure in manifest.get("failures", []):
        run.issue("shots.failed", failure)

    pointer = run.artifacts.parent / f"latest-shots-{suite}.json"
    previous = None
    if pointer.is_file():
        try:
            previous = json.loads(pointer.read_text(encoding="utf-8")).get("dir")
        except ValueError:
            previous = None
    if not args.no_analyze:
        baseline = args.baseline or (previous if args.diff_last else None)
        try:
            summary.update(analyze(directory, manifest, baseline))
        except ImportError as error:
            run.issue("shots.analysis", f"contact sheets skipped: {error}", "info")
    pointer.write_text(json.dumps(dict(dir=str(directory), run=run.artifacts.name)), encoding="utf-8")
    if args.movie:
        summary["movie"] = str(run.artifacts / f"{suite}.avi")
    run.result["metrics"]["shots"] = summary
    run.note("SHOTS " + json.dumps(summary, ensure_ascii=False))
    for line in brief_lines(summary):
        run.brief(line)
