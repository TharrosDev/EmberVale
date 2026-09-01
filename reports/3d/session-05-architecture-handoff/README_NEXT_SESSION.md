# Session 5 → Session 6: architecture handoff

Session 5 is closed. It expanded Embervale's existing architecture foundation instead of replacing
working assets, and did not begin the general nature/environment pass.

## What was preserved

- Retained monolithic production buildings:
  `assets/models/architecture/bld_cottage.glb`, `bld_house_a.glb`, `bld_house_b.glb`,
  `bld_inn.glb`, and `bld_blacksmith.glb`.
- Retained the original modular plaster/timber walls, windows, doors, gables, floors, corners,
  roofs, timber pieces, chimneys, and the enterable `scenes/props/bld_ashfall_house.tscn`.
- Kept the bespoke live inn/blacksmith/house forms where they already gave a settlement a useful
  silhouette. Session 5 targeted repeated rectangular shells, not blanket replacement.

## Modules added

Twenty-one compatible shared Quaternius CC0 modules were adopted into
`assets/models/architecture/`: four uneven-stone wall/opening pieces; two shutter pieces; an
alternate chimney; dormer, awning and roof supports; two balconies; exterior stair and stone floor;
four larger roof spans; an 8 m gable; wall arch; and vine weathering. Shared textures are
`T_UnevenBrick_*` and `T_VineLeaf_png.png`. Exact source families and licence are in
`assets/CREDITS.md`.

## Prefabs created

All live in `scenes/props/` and embed their exact `compose_building.py` regeneration command:

- `bld_cottage_shuttered.tscn`
- `bld_farmhouse_long.tscn`
- `bld_shop_awning.tscn`
- `bld_townhouse_balcony.tscn`
- `bld_townhouse_wide.tscn`
- `bld_workshop_open.tscn`
- `bld_longhouse_stone.tscn`
- `bld_inn_courtyard.tscn`
- `bld_ruin_house.tscn`
- `bld_ruin_tower.tscn`

The offline composer now varies footprint, one/two storeys, roof axis, door bay, chimney side,
plaster/stone wall family, shutters, dormer, awning, balcony, stair, weathering and collision/access
mode. See `docs/ARCHITECTURE_KIT.md`; there is no runtime procedural-building system.

## Live integration

- `scenes/regions/ember_crown/town_hub.tscn`: shop, balcony townhouse and shuttered cottage replace
  three repeated shells.
- `scenes/regions/ember_crown/embermarket.tscn`: awning shop, shuttered cottage and balcony
  townhouse diversify the main market.
- `scenes/regions/ember_crown/tarn_landing.tscn` and `hollowreach.tscn`: one repeated cottage each
  becomes the shuttered variant.
- `scenes/regions/frostfang_reach/clan_hold.tscn`: stone longhouse and open workshop split the prior
  three-identical-shell composition.

## Materials

The coherent family is bone/earth plaster, uneven fieldstone/ruined stone, dressed foundation
stone, dark/weathered timber, warm dark round tiles, and sparse vine weathering. Five retained
monolithic GLBs had embedded metallic/roughness values repaired without changing mesh binary data.
`tools/repair_architecture_materials.py` is the reproducible repair.

## Collision and navigation

`compose_building.py` now emits collision by access intent:

- exterior-only buildings: one simplified solid shell;
- hollow buildings: floor plus individual wall colliders and a real door opening;
- open workshops: floor and three walls, no invisible front wall;
- ruins: surviving wall pieces only, no invisible full shell or floor cap.

`tools/building_collision_probe.gd` uses the production 0.4 m-radius, 1.8 m player capsule and proves
door entry, adjacent-wall blocking, floor hold, open-workshop entry, and ruin-breach entry. The full
world traversal/layout/scene gates pass, so the settlement swaps did not invalidate authored routes
or navigation.

## Visual QA

- Baseline evidence: `reports/3d/session-05-architecture-baseline/`.
- Final six-angle evidence: `visual-qa/` — 15 important buildings, six required views each,
  **90/90 PASS**. Front, back, left, right, front 3/4 and rear 3/4 were inspected for missing backs,
  roofs/gaps, floating pieces, normals, entrance alignment, stairs, scale and foundations.
- Live contact sheets in `visual-qa/live-*.png` cover all five changed settlements.
- Final permanent audit: `final-audit/` — self-test PASS, Blender PASS, Godot probe PASS,
  **178 production assets / 43 architecture assets** inventoried.
- The world baseline was merged only for the five reviewed changed cells with
  `tools/merge_world_baseline.py`; the final 260-frame visual gate passes.

## Performance

`quality-full/performance.json` is a machine-sensitive report, not a hard gate. On the Intel Iris Xe
reference machine Ember Crown averaged 876 draws / 1.11 M primitives / 18.97 ms, worst 22.73 ms at
Hollowreach; Frostfang averaged 178 draws / 0.218 M primitives / 12.76 ms, worst 18.87 ms at Clan
Hold. Shared module textures limit material duplication, but modular scenes do increase node/draw
count relative to a monolithic shell. Keep authored mesh budgets authoritative and consider static
baking/merging only if a later profiling pass demonstrates a real bottleneck.

## Known limitations

- Most new exterior prefabs are deliberately non-enterable solid shells; their doors are visual.
  Use hollow/open modes for interiors rather than removing the shell collider in a live cell.
- `bld_inn_courtyard` is a large courtyard-facing inn shell, not a generated enclosed courtyard.
- Exterior stair geometry is visually validated but is not yet a route-critical traversal contract.
  Add a dedicated stair probe before making it the only entrance to gameplay.
- Ruins are reusable shells without interior debris dressing. Nature scatter and broad moss growth
  belong to the later environment pass and were intentionally not started.
- The permanent asset audit reports advisory collision findings on raw GLB/glTF modules. Production
  collision belongs to the authored `.tscn` assemblies and is independently gated.

## Validation result

- `world_quality_check.py --mode full`: all 21 pass/fail gates PASS; performance report generated.
- Negative battery: **111/111** deliberately broken rules caught and restored.
- Build: 0 warnings, 0 errors. Tests: **1713 passing**. `--validate`: PASS.
- Architecture checker: 11 authored prefabs, five integrated settlements, five repaired GLBs PASS.
- Building capsule probe: PASS. World visual gate: PASS.

## Git commits

- `20cebd1` — `Overhaul modular architecture kit` (implementation, assets, scenes, tools, credits,
  material repair and reviewed visual baseline).
- Handoff commit — the commit containing this file. Resolve it exactly with
  `git log -1 --oneline -- reports/3d/session-05-architecture-handoff/README_NEXT_SESSION.md`.

Both commits are on `main`.

## Session 6: read exactly these first

1. `AGENTS.md`
2. `docs/NOW.md`
3. `docs/ARCHITECTURE_KIT.md`
4. `docs/ART_STYLE.md`
5. `docs/ASSET_POLICY.md`
6. `reports/3d/session-05-architecture-handoff/README_NEXT_SESSION.md`
7. `reports/3d/session-05-architecture-handoff/visual-qa/README.md`
8. `reports/3d/session-05-architecture-handoff/final-audit/README.md`
9. `reports/3d/session-05-architecture-handoff/final-audit/prioritized-findings.md`
10. `reports/3d/session-05-architecture-handoff/quality-full/summary.json`

## Session 6 startup commands

Run from `C:\Users\magnu\Embervale` in PowerShell:

```powershell
git status --short
git log -3 --oneline --decorate
Get-Content AGENTS.md
Get-Content docs/NOW.md
Get-Content reports/3d/session-05-architecture-handoff/README_NEXT_SESSION.md
dotnet build Embervale.sln --no-restore
dotnet test tests/Embervale.Tests --no-build
& 'C:\Users\magnu\Downloads\Godot_v4.7.1-stable_mono_win64\Godot_v4.7.1-stable_mono_win64\Godot_v4.7.1-stable_mono_win64_console.exe' --headless --path . -- --validate
& 'C:\Users\magnu\.cache\codex-runtimes\codex-primary-runtime\dependencies\python\python.exe' tools/check_architecture_kit.py
& 'C:\Users\magnu\Downloads\Godot_v4.7.1-stable_mono_win64\Godot_v4.7.1-stable_mono_win64\Godot_v4.7.1-stable_mono_win64_console.exe' --headless --path . --script res://tools/building_collision_probe.gd
& 'C:\Users\magnu\.cache\codex-runtimes\codex-primary-runtime\dependencies\python\python.exe' tools/audit_3d.py --output reports/3d/session-06-baseline --render none
```

Do not rerun the broad architecture pass or begin nature work unless Session 6 explicitly calls for
it. `docs/NOW.md` remains authoritative: the main roadmap resumes at 42C.

