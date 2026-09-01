# Embervale agent guide

This repository is Godot 4.7 / C# .NET 8. Before changing it, read `docs/NOW.md` and the
documents it links. For art work also read `docs/ART_STYLE.md`, `docs/ASSET_POLICY.md`, and the
most recent folder under `reports/3d/`.

## Working rules

- Preserve unrelated working-tree changes. Generated `.uid` files may be local editor residue;
  establish ownership before adding them.
- `data/regions/*.tres` is generated. Edit `tools/region_spec_<region>.py`, then run
  `tools/gen_regions.py`; never hand-edit generated region resources.
- Search the approved vendored Quaternius libraries before sourcing or creating 3D art. Adopt
  production assets into `assets/models/`, keep shared textures shared, and update
  `assets/CREDITS.md`.
- Match `docs/ART_STYLE.md`: readable low-poly silhouettes, grounded forms, restrained detail,
  nonmetallic plaster/wood/stone, and coherent material families.
- Never approve 3D work from bounds alone. Use the permanent audit plus eye-level and multi-angle
  renders. Architecture must show front, back, left, right, front three-quarter, and rear
  three-quarter views.
- Prefer authored reusable scenes over runtime procedural complexity. Building regeneration and
  collision contracts are in `docs/ARCHITECTURE_KIT.md`.
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
python tools/check_architecture_kit.py
godot --headless --path . --script res://tools/building_collision_probe.gd
python tools/world_quality_check.py --mode engine
python tools/world_quality_check.py --mode visual
```

Run `python tools/negative_tests.py` and `world_quality_check.py --mode full` only after the
implementation commit: the negative battery intentionally refuses dirty `data/` or `scenes/`.

