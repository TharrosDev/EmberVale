"""Run one C# headless gate or session harness through the SDK: run NAME [-- extra game args].

    run story                      a headless gate: validate state economy worldgen worldmap
    run save-reload                world-bake lifecycle save-reload story
    run hudshots                   a session harness from src/Bootstrap/SessionHarnesses.cs (rendered)
    run spellshots --env EMBERVALE_SPELLSHOTS_TIER=low -- --quit-after=120
    run my-new-flag --headless     any other `-- --flag` the game understands

Compared with launching Godot by hand this adds the stale-build rebuild, the heavy-run lock, an
owned timeout (--timeout), an isolated save directory, a run directory with the log, and the
gate's EMBERVALE_RESULT facts in the output.

A session harness needs a session. `--save new` (the default) starts one with `--new-game` in the
run's own save directory; `--save real` continues the newest real save, as a hand launch does (the
harness can then write to it); `--save DIR` copies a user directory (the folder holding `saves/`)
into the run first. A rendered harness needs a real, focused window: it cannot run headless.
"""
import re
import shutil
from pathlib import Path

from quality_common import ROOT
from ..contract import parse_result, result_brief

HELP = "run a C# gate or session harness flag (story, save-reload, hudshots, spellshots, ...) with SDK guards"
PASSTHROUGH = True
HEADLESS = ("validate", "state", "economy", "worldgen", "worldmap", "world-bake", "lifecycle", "save-reload", "story")
TABLE = "src/Bootstrap/SessionHarnesses.cs"


def arguments(parser):
    parser.add_argument("--headless", action="store_true", help="run a flag this command does not know without a window")
    parser.add_argument("--save", default="new", metavar="new|real|DIR",
                        help="session harnesses: fresh game (default), the real saves, or a user directory to copy in")
    parser.add_argument("--env", action="append", default=[], metavar="EMBERVALE_X=VALUE",
                        help="an environment variable for the game (EMBERVALE_* only); repeat")


def harnesses(root=ROOT):
    """Session harness names (without the dashes), read from the table the game itself uses, so a
    harness added there is runnable here with no edit."""
    try:
        text = (Path(root) / TABLE).read_text(encoding="utf-8")
    except OSError:
        return []
    return sorted({name[2:] for name in re.findall(r'(?:new\(|Alias:\s*)"(--[a-z0-9-]+)"', text)})


def environment(pairs):
    out = {}
    for pair in pairs:
        key, separator, value = pair.partition("=")
        if not separator or not re.fullmatch(r"EMBERVALE_[A-Z0-9_]+", key) or key in ("EMBERVALE_USER_DIR", "EMBERVALE_ARTIFACTS"):
            raise ValueError(f"--env takes EMBERVALE_NAME=value (not the user or artifact directory): {pair}")
        out[key] = value
    return out


def run(run, args, passthrough):
    name = (args.target or "").lstrip("-")
    sessions = harnesses()
    if not re.fullmatch(r"[a-z0-9][a-z0-9-]*", name):
        raise ValueError("run needs a flag name. headless: " + " ".join(HEADLESS) + " | harness: " + " ".join(sessions + ["shellshots"]))
    session = name in sessions
    if name in HEADLESS or args.headless:
        render = False
    elif session or name == "shellshots" or args.render:
        render = True
    else:
        raise ValueError(f"unknown flag --{name}: pass --headless or --render to run it anyway. headless: "
                         + " ".join(HEADLESS) + " | harness: " + " ".join(sessions + ["shellshots"]))
    run.env.update(environment(args.env))
    user = ["--" + name, *passthrough]
    if session:
        if args.save == "real":
            run.env.pop("EMBERVALE_USER_DIR", None)
            run.brief("saves: the real user directory (the harness can write to it)")
        elif args.save != "new":
            source = Path(args.save)
            if not (source / "saves").is_dir():
                raise ValueError(f"--save {args.save}: not a user directory (it has no saves/ folder)")
            shutil.copytree(source, run.artifacts / "user", dirs_exist_ok=True)
        elif not {"--new-game", "--play"}.intersection(passthrough):
            user.append("--new-game")
    result = run.godot(name, [], user, render=render)
    report = parse_result(result.stdout)
    if report:
        run.result["metrics"]["gate_result"] = report
        run.brief(result_brief(report))
        for failure in (report.get("failures") or [])[:10]:
            run.brief(f"GATE-FAIL {failure}")
        if not report.get("ok") and result.returncode == 0:
            run.issue("run.result", f"--{name} reported ok=false but exited 0")
