"""Did I break what I touched: the git diff mapped to the smallest set of gates that covers it.

    verify            run the gates for every changed, staged and untracked file
    verify --plan     print the selection and its estimated cost; run nothing
    verify --base origin/main     also count commits since that merge base
    verify --budget 60            skip any single gate estimated above 60 s (reported, not hidden)

RULES below is the whole mapping. A gate's own script selects that gate without a rule. A path
no rule knows is reported as UNMAPPED (a warning, so --strict fails on it): that is the cue to
add a rule, not a pass. Gates judged by eye or measured in minutes are never selected; ADVICE
names them when a change calls for one.
"""
from __future__ import annotations

import json
import sys
import time
from fnmatch import fnmatchcase

from quality_common import ROOT
from .. import costs

HELP = "run the minimal gate set for the current git diff (--plan prints it with cost estimates and runs nothing)"
LIGHT = True
VERSION = False
FOREGROUND_SECONDS = 480

# (path patterns, gates). `*` crosses folders. Gates run in registry order whatever the order here.
RULES = (
    (("src/*", "addons/*", "tests/*", "*.csproj", "*.sln", ".editorconfig"), ("build", "tests")),
    (("src/*", "project.godot", "scenes/*"), ("content",)),
    (("src/Bootstrap/*", "src/Debugging/*", "addons/*", "Embervale.csproj", "tools/check_shipping_assembly.py"),
     ("shipping-assembly",)),
    (("src/Bootstrap/*", "src/Core/*", "project.godot"), ("lifecycle", "save-reload", "story")),
    (("src/Save/*",), ("save-audit", "save-reload", "lifecycle")),
    (("src/Quests/*", "src/Dialogue/*", "src/Narrative/*", "data/story/*", "data/quests/*", "data/dialogue/*",
      "data/bosses/*"), ("story",)),
    (("src/Magic/*", "data/spells/*", "data/status_effects/*"),
     ("magic-core", "magic-lifetime", "magic-status", "magic-learning", "magic-content")),
    (("src/Combat/*", "data/weapons/*"),
     ("melee", "ranged", "action-clip", "combat-offence", "combat-defence", "combat-feedback", "combat-ranged")),
    (("src/Enemies/*", "src/Companions/*", "data/enemies/*", "data/ai_profiles/*"),
     ("melee", "combat-defence", "magic-content")),
    (("src/Player/*", "src/Movement/*"),
     ("camera", "facing", "view-switch", "grounding", "stepup", "combat-offence")),
    (("src/Animation/*", "assets/models/characters/*", "assets/models/creatures/*", "assets/models/animations/*"),
     ("sockets", "anim-library", "anim-tree", "action-clip", "facing")),
    (("src/World/*",), ("world-audit", "transition", "environment-climate", "building-collision", "map",
                        "regressions", "streaming-stress", "traversal")),
    (("src/Items/*", "src/Loot/*", "src/Crafting/*", "src/Economy/*"), ("runtime-audit",)),
    (("src/Debugging/*",), ("save-audit", "world-audit", "runtime-audit", "scene-audit-rules")),
    (("data/*",), ("content", "tests")),
    (("data/items/*", "data/item_sets/*", "data/unique_effects/*", "data/affixes/*", "data/recipes/*", "data/loot/*",
      "tools/items/*", "tools/gen_items.py", "tools/gen_recipes.py"),
     ("item-generation", "recipe-generation", "loot-generation")),
    (("data/map_locations/*", "data/locale/*", "tools/gen_map_locations.py"), ("map-locations",)),
    (("data/regions/*", "tools/region_spec_*.py", "tools/gen_regions.py", "tools/world_bake.py", "data/world_bake/*",
      "data/biomes/*", "data/terrain_layers/*", "data/world_gen/*"),
     ("generation", "world-bake", "seams", "layout", "composition", "atlas", "cell-scenes", "content")),
    (("scenes/regions/*",), ("cell-content", "districts", "cell-scenes", "map-locations", "world-bake",
                             "scene-audit-rules", "scenes", "meshes", "building-collision")),
    (("assets/models/*",), ("content", "architecture", "meshes")),
    (("tools/region_spec_template.py",), ("template",)),
)
# Steps that are not registry gates.
EXTRA = (
    (("tools/embervale_sdk/*", "tools/quality_common.py", "tools/process_tree.py", "tools/world_quality_check.py",
      "tools/test_*.py", "tools/negative_tests.py", "tools/godot_mcp_check.py", "tools/embervale.py",
      "tools/sdk_engine_tests.py", "tools/headless/*"), "tool-tests"),
    (("*.md",), "docs-lines"),
    (("assets/models/*",), "assets-validate"),
)
# Changes no gate reads. Anything else that matches nothing is UNMAPPED.
IGNORED = (".github/*", ".claude/*", ".gitignore", ".gitattributes", "artifacts/*", "reports/*", "assets/library/*",
           "*.uid", "*.import", "docs/*", "assets/CREDITS.md")
# Gates verify never selects: judged by eye, machine-sensitive, or forty minutes long.
EXCLUDED = frozenset({"visuals", "performance", "environment-route", "negative"})
ADVICE = (
    (("src/UI/*",), "UI changed and no gate sees pixels: render it (`run uishots`, `run hudshots`, `run panelshots`)"),
    (("src/Magic/Vfx/*",), "spell effects changed: look at them (`run spellshots`, `run camshots`, `run vfxperf`)"),
    (("src/Debugging/ContentValidator*", "tools/negative_tests.py"),
     "validator rules changed: only the negative battery proves they still fire (`job start tool negative_tests`, about 40 min)"),
    (("scenes/regions/*", "data/regions/*", "assets/models/world/*", "assets/models/architecture/*"),
     "the world changed: frames are judged by eye (`world --mode visual`), not by verify"),
)


def matches(path, patterns):
    return any(fnmatchcase(path, pattern) for pattern in patterns)


def region_of(path, regions):
    """The region a path belongs to, or None when it could affect any of them."""
    for key, resource in regions.items():
        if path in (resource, f"tools/region_spec_{key}.py") or path.startswith(
                (f"scenes/regions/{key}/", f"data/world_bake/cells/region_{key}/")):
            return key
    return None


def plan(paths, registry, regions):
    """The selection for a list of changed paths: {"gates": names in registry order, "extra":
    [...], "regions": the regions per-region gates run for (None = all), "advice": [...],
    "unmapped": [...]}. Pure: it reads nothing but its arguments."""
    by_name = {gate.name: gate for gate in registry}
    chosen, extra, advice, unmapped, regional = set(), [], [], [], set()
    for path in paths:
        hit = set()
        for patterns, names in RULES:
            if matches(path, patterns):
                hit.update(names)
        for gate in registry:   # a gate's own script selects that gate
            if gate.name not in EXCLUDED and any(part in (path, "res://" + path) for part in gate.command):
                hit.add(gate.name)
        steps = [step for patterns, step in EXTRA if matches(path, patterns)]
        extra += [step for step in steps if step not in extra]
        advice += [text for patterns, text in ADVICE if matches(path, patterns) and text not in advice]
        if any(by_name[name].per_region for name in hit if name in by_name):
            regional.add(region_of(path, regions))
        if not hit and not steps and not matches(path, IGNORED):
            unmapped.append(path)
        chosen.update(hit)
    return dict(gates=[gate.name for gate in registry if gate.name in chosen], extra=extra,
                regions=None if (not regional or None in regional) else sorted(regional),
                advice=advice, unmapped=unmapped)


def estimates(selection, registry, regions, history):
    """[(step name, seconds)] for a plan, per-region gates counted once per region."""
    rows = []
    for gate in registry:
        if gate.name in selection["gates"]:
            count = len(selection["regions"] or regions) if gate.per_region else 1
            engine = "godot" in str(gate.command[0]).lower()
            rows.append((gate.name, count * costs.estimate(gate.name, history, engine)))
    return rows + [(step, costs.estimate("assets" if step == "assets-validate" else step, history)) for step in selection["extra"]]


def docs_lines(run, paths):
    """CLAUDE.md section 6: no documentation line over 2000 characters."""
    started, bad = time.monotonic(), 0
    for path in paths:
        file = ROOT / path
        if not path.endswith(".md") or not file.is_file():
            continue
        for number, line in enumerate(file.read_text(encoding="utf-8", errors="replace").splitlines(), start=1):
            if len(line) > 2000:
                bad += 1
                run.issue("process.docs-lines", f"{path}:{number} is {len(line)} characters (limit 2000)", path=path)
    run.add_step(dict(name="docs-lines", command=[], duration=time.monotonic() - started,
                      exit_code=1 if bad else 0, success=not bad))


def run(run, args, passthrough):
    from world_quality_check import gates, REGIONS
    from ..changed import changed_paths
    from ..cli import REGISTRY, run_command
    paths, _ = changed_paths(args.base)
    registry = gates(None)
    selection = plan(paths, registry, REGIONS)
    rows = estimates(selection, registry, REGIONS, costs.load())
    total = sum(seconds for _, seconds in rows)
    over = [name for name, seconds in rows if args.budget is not None and seconds > args.budget]
    if args.plan or not rows:
        if args.json or args.json_full:
            print(json.dumps(dict(changed=len(paths), estimate_seconds=round(total), over_budget=over,
                                  steps=[dict(name=n, seconds=round(s)) for n, s in rows], **selection)))
            return 0
        if not rows:
            print(f"VERIFY nothing to run: {len(paths)} changed file(s), none with a gate")
        else:
            print(f"PLAN {len(rows)} steps ~{costs.clock(total)} for {len(paths)} changed file(s): "
                  + ", ".join(f"{n} {costs.clock(s)}" for n, s in rows)
                  + (f" | regions: {' '.join(selection['regions'])}" if selection["regions"] else ""))
        if over:
            print(f"SKIP over --budget {args.budget:g}s: {', '.join(over)}")
        for text in selection["advice"]:
            print(f"ADVISE {text}")
        if selection["unmapped"]:
            print(f"UNMAPPED {len(selection['unmapped'])}: {', '.join(selection['unmapped'][:12])}"
                  + (" ..." if len(selection["unmapped"]) > 12 else ""))
        if total > FOREGROUND_SECONDS:
            print("NOTE longer than a foreground window: run it as `job start verify`")
        return 0

    def body(run, args, passthrough):
        live = gates(str(run.engine) if run.engine else None)
        run.result["metrics"]["verify"] = dict(changed=paths, estimate_seconds=round(total), over_budget=over, **selection)
        if selection["unmapped"]:
            listed = ", ".join(selection["unmapped"][:12]) + (" ..." if len(selection["unmapped"]) > 12 else "")
            run.issue("verify.unmapped", f"{len(selection['unmapped'])} changed path(s) have no gate: {listed}", "warning")
            run.brief(f"UNMAPPED {len(selection['unmapped'])}: {listed}")
        for text in selection["advice"]:
            run.brief(f"ADVISE {text}")
        if over:
            run.brief(f"SKIP over --budget {args.budget:g}s: {', '.join(over)}")
        if "docs-lines" in selection["extra"]:
            docs_lines(run, paths)
        items = []
        for gate in live:
            if gate.name in selection["gates"] and gate.name not in over:
                names = (selection["regions"] or sorted(REGIONS)) if gate.per_region else [None]
                items += [(gate, region) for region in names]
        run.run_gates(items)
        if "tool-tests" in selection["extra"] and "tool-tests" not in over:
            run.process("tool-tests", [sys.executable, "-m", "unittest", "discover", "-s", "tools", "-p", "test_*.py"])
        if "assets-validate" in selection["extra"] and "assets-validate" not in over:
            run.guard_tool(ROOT / "tools/assets.py", ["validate"])
            run.process("assets-validate", [sys.executable, "tools/assets.py", "validate"])
        done = []
        for step in run.result["steps"]:
            verdict = "cached" if step.get("cached") else ("ok" if step.get("success") else "FAIL")
            done.append(f"{step['name']} {verdict}" + ("" if step.get("cached") else f" {costs.clock(step.get('duration', 0))}"))
        run.brief("gates: " + ", ".join(done))

    return run_command(args, passthrough, REGISTRY["verify"], body)


def arguments(parser):
    parser.add_argument("--plan", action="store_true", help="print the selected gates and their estimated cost; run nothing")
    parser.add_argument("--budget", type=float, metavar="SEC", help="skip any step estimated to take longer than this")
