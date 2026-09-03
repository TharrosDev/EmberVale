# reports/3d — archive

⚠️ **None of this is required reading.** The 3D contract is
[`docs/3D_ASSETS.md`](../../docs/3D_ASSETS.md) and the current state is
`python tools/assets.py status`. Every operational rule that used to live only in these folders
has been lifted into that document.

This is kept as **evidence**: renders, concept art, measurements and the reasoning behind
decisions that have already been made. Read a folder when you want to know *why* something is the
way it is, never to find out *how* to do something.

⚠️ **Every "read exactly these first" and "next session" section in here is a dead pointer.** They
chain from one session to the next and the chain ended; following one leads to a plan that was
superseded. The dates below are what they are relative to.

| Folder | What it recorded | Dated |
| --- | --- | --- |
| `session-1-foundation/` | The first full audit — the baseline every later one is compared against | 2026-08-31 |
| `session-02-player-weapons-handoff/` | First-person arms and the player weapon set rebuilt; the measured grip | 2026-09-01 |
| `session-03-npc-handoff/` | The modular NPC outfit kit, and proof the human GLB edits changed materials only | 2026-09-01 |
| `session-04-enemies-handoff/` | Enemy identity meshes; `ENEMY_VISUAL_DECISIONS.md` is the per-enemy log | 2026-09-01 |
| `session-05-architecture-baseline/` | Per-building audits taken before the architecture pass | 2026-09-01 |
| `session-05-architecture-handoff/` | The authored building family and its collision contracts | 2026-09-01 |
| `session-06-baseline/` | The audit snapshot taken before the environment pass | 2026-09-02 |
| `session-06-environment-handoff/` | Boulder, cliff and ice families; the shared rock atlas | 2026-09-02 |
| `meshy-migration/` | The generated character wave. `manifest.csv` is the provenance ledger — prompts, task ids and per-model history for every generated body | 2026-09-02 |

## What was removed, and why it was safe

115 files were deleted in the move: byte-identical copies of the same regenerated audit output
sitting in several folders at once. `session-05-architecture-baseline/` stored one 2.1 MB audit six
times over; `rig-animation-analysis.md` was identical in seven places. All of it is reproducible
with `python tools/assets.py audit`, and every distinct copy was kept.

**Nothing unique was deleted.** All 1,235 renders are intact — no two of them were even identical.
Concept art, contact sheets, `manifest.csv`, `groupC/FINDINGS.md` and every handoff document remain
exactly as they were, and git history holds the rest regardless.

## Where the rules went

| Was only here | Now |
| --- | --- |
| The Meshy procedure, prompt stem, and its two silent failures | `docs/3D_ASSETS.md` → *Where models come from*, *HUMANOID* |
| `meshy_rig_probe.gd` is a gate, not a spot check | `docs/3D_ASSETS.md` → *HUMANOID / The gate*, and `assets.py validate` runs it |
| "Do not hand-write a BoneMap" | `docs/3D_ASSETS.md` → *Adopting a model* |
| The `detect_3d` texture `.import` convention | `docs/3D_ASSETS.md` → *Adopting a model* |
| A replacement inherits its predecessor's root scale | `docs/3D_ASSETS.md`, and `assets.py adopt` now warns |
| Quadrupeds are not migratable to the humanoid rig | `docs/3D_ASSETS.md` → *QUADRUPED* |
| The world visual gate is nondeterministic and advisory | `docs/3D_ASSETS.md` → *Validating*, and `docs/NOW.md` |
