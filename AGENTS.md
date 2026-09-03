# Embervale agent guide

This repository is Godot 4.7 / C# .NET 8. Before changing it, read `docs/NOW.md` and the
documents it links. **For 3D asset work, `docs/3D_ASSETS.md` is the contract and
`python tools/assets.py status` is the state** — those two, and nothing else. Nothing under
`reports/3d/archive/` is required reading.

## Working rules

- Preserve unrelated working-tree changes. Generated `.uid` files may be local editor residue;
  establish ownership before adding them.
- `data/regions/*.tres` is generated. Edit `tools/region_spec_<region>.py`, then run
  `tools/gen_regions.py`; never hand-edit generated region resources.
- 3D assets have two lanes: characters and creatures are generated (semi-realistic); props,
  architecture and nature come from the vendored Quaternius library, searched before anything is
  sourced or authored. `docs/3D_ASSETS.md` has both. `assets/CREDITS.md` is frozen — do not add
  to it; the manifest is derived by `python tools/assets.py status --write`.
- Adopt with `python tools/assets.py adopt`, then `python tools/assets.py validate`. It encodes the
  ordering; calling the underlying scripts by hand skips steps that are not optional.
- Match `docs/ART_STYLE.md` for the world: grounded forms, restrained detail, nonmetallic
  plaster/wood/stone, coherent material families. The cast is the documented exception.
- Never approve 3D work from bounds alone. Use the audit plus eye-level and multi-angle renders.
  Architecture must show front, back, left, right, front three-quarter, and rear three-quarter
  views.
- Prefer authored reusable scenes over runtime procedural complexity. Building regeneration and
  collision contracts are in `docs/3D_ASSETS.md` → ARCHITECTURE.
- Use simplified collision where possible. Validate entrances, adjacent walls, floors, breaches,
  stairs, and navigation with the real player capsule.
- A world visual-baseline change is reviewed evidence, not a way to silence a failing gate. Use
  `tools/merge_world_baseline.py` for explicitly reviewed changed cells.

## Validation spine

`godot` and `python` are not on the default shell PATH on the maintainer machine; `docs/NOW.md`
records the working absolute executables.

```text
dotnet build Embervale.sln
dotnet test tests/Embervale.Tests
python tools/gen_regions.py --check
godot --headless --path . -- --validate
python tools/assets.py validate
python tools/check_architecture_kit.py
godot --headless --path . --script res://tools/building_collision_probe.gd
python tools/world_quality_check.py --mode engine
python tools/world_quality_check.py --mode visual
```

Run `python tools/negative_tests.py` and `world_quality_check.py --mode full` only after the
implementation commit: the negative battery intentionally refuses dirty `data/` or `scenes/`.

