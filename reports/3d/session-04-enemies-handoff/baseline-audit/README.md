# Embervale 3D audit

This folder records a point-in-time production-model audit for its containing work session.

## Scope and run

- Production assets audited: **156** (`assets/models/**/*.glb|gltf`)
- Categories: animations 1, architecture 22, characters 15, creatures 33, equipment 3, props 81, weapons 1
- Findings: 163 (high 108, info 22, medium 33)
- Recommendations: IMPROVE 76, KEEP 79, REPLACE 1
- Blender: C:\Program Files\Blender Foundation\Blender 5.1\blender.exe (PASS)
- Godot imported-scene probe: C:\Users\magnu\Downloads\Godot_v4.7.1-stable_mono_win64\Godot_v4.7.1-stable_mono_win64\Godot_v4.7.1-stable_mono_win64_console.exe (PASS)

- Committed diagnostic render files: **0**

## Read next

1. `prioritized-findings.md` — ordered worklist.
2. `production-inventory.md` — complete human-readable inventory.
3. `visual-qa-index.md` — truthful Blender views and sampled rig poses.
4. Domain reports (`materials`, `scale-origin`, `rig-animation`, `collision`, `texture-performance`, `duplicates`).
5. `inventory.json` and `findings.json` for automation.

Production assets were inspected only; the audit does not rewrite them.
