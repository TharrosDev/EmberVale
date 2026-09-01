# Embervale 3D audit

This folder records the fresh Session 2 baseline before player-facing assets were changed.

## Scope and run

- Production assets audited: **151** (`assets/models/**/*.glb|gltf`)
- Categories: animations 1, architecture 22, characters 13, creatures 33, props 81, weapons 1
- Findings: 197 (high 143, info 22, medium 32)
- Recommendations: IMPROVE 83, KEEP 67, REPLACE 1
- Blender: C:\Program Files\Blender Foundation\Blender 5.1\blender.exe (PASS)
- Godot imported-scene probe: C:\Users\magnu\Downloads\Godot_v4.7.1-stable_mono_win64\Godot_v4.7.1-stable_mono_win64\Godot_v4.7.1-stable_mono_win64_console.exe (PASS)

- Committed diagnostic render files: **22**

## Read next

1. `prioritized-findings.md` — ordered worklist.
2. `production-inventory.md` — complete human-readable inventory.
3. `visual-qa-index.md` — truthful Blender views and sampled rig poses.
4. Domain reports (`materials`, `scale-origin`, `rig-animation`, `collision`, `texture-performance`, `duplicates`).
5. `inventory.json` and `findings.json` for automation.

Production assets were inspected only; the audit does not rewrite them.
