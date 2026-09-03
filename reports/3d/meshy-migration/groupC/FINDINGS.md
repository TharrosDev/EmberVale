# Group C (quadrupeds) — pilot result: DO NOT MIGRATE

Date: 2026-09-02. Branch `claude/meshy-migration-groupc`.
Spent: **18 credits** (829 → 811). Budgeted 23; the rig never charged.

## Verdict

**Meshy cannot rig a quadruped. Group C stays on its legacy models.**

`meshy_rig` rejects the wolf mesh outright with HTTP **422 — "Pose estimation failed, please
provide a valid model"**. Reproduced twice, once via `input_task_id` and once via `model_url`, so it
is the model that is refused and not the plumbing. The auto-rig's pose estimator is humanoid and
finds no pose on a four-legged body; it never reaches the point of producing a bad rig, which is why
this cost 5 credits less than budgeted.

This is a **hard stop, not a quality judgement**. There was no wolf-with-arms to photograph and no
bent spine to measure — the service declines the input. The remaining four quadrupeds would fail
identically and were not attempted, per the brief.

## What the 18 credits bought

| Step | Result | Credits |
| --- | --- | --- |
| `meshy_text_to_image` (nano-banana, 3:4) | `wolf_concept.png` — good: gaunt four-legged wolf, ash-grey, one ember-orange eye | 3 |
| `meshy_image_to_3d` (meshy-t2 smart-topology, textured, triangle, 3500) | task `01a06532-e29d-7664-bcdb-5d82fed2321c`, **3,651 tris**, in budget | 15 |
| `meshy_rig` (height_meters 1.0) | **422, twice. Not charged.** | 0 |

Prompt verbatim in `wolf_prompt.txt` (592 chars). It deliberately drops the humanoid stem's
"T-pose, arms straight out to the sides" and "7.5 heads tall" clauses, which are meaningless for a
quadruped, and substitutes "standing on all four legs, legs straight and evenly spaced". `pose_mode`
was omitted for the same reason. Palette, ember-orange accent and the semi-realistic clause are
unchanged from the stem.

The mesh itself was staged and rendered in Godot before being discarded. It is a genuine
four-legged wolf, origin at the feet, sitting correctly on the ground plane. Two defects worth
recording for any future attempt: the mesh arrives **normalised to a unit box** (X 0.291, Y 0.961,
Z 1.000 — not metres, so `height_meters` never got a chance to under-deliver here), and
**smart-topology at 3.5k tris terraces the neck ruff** into visible stair-steps, because the budget
cannot hold a fur silhouette. The GLB was not kept; the task id above still resolves it.

## Why this is the right outcome, not a workaround

An unrigged mesh cannot inherit the legacy `AnimalArmature` clips — those are bound to the legacy
skeleton. Adopting the mesh alone would make the wolf a static prop. There is no partial adoption.

## What the swap would have cost anyway

Traced through `AnimationClips.Resolve` (`src/Animation/AnimationClips.cs`) against the real clip
lists. The legacy wolf ships 24 clips; the resolver only ever reaches five of them.

| Slot | Legacy (`AnimalArmature|*`) | After a Meshy swap |
| --- | --- | --- |
| idle | `Idle` — quadruped stand | `Idle_Loop` — human standing |
| run | `Walk` — quadruped walk | `Running` — Meshy biped run |
| attack | `Attack` — lunge | `Sword_Attack` — two-handed human sword swing |
| hit | `Idle_HitReact_Left` | `Hit_Chest` — human upper-body flinch |
| death | `Death` — collapses on four legs | `Death01` — human falls backward |
| block / cast / channel / ride | empty | `Sword_Idle` / `Spell_Simple_Shoot` / `Spell_Simple_Idle_Loop` / `Sitting_Idle_Loop` |

Every slot a wolf actually uses moves from species-correct to humanoid, and the model gains four
slots a beast should never have. `Gallop` and `Eating` are dropped outright — note the resolver
never reached `Gallop` anyway, because the `run` alias list puts `walk` ahead of it.

**So even a working humanoid rig would have been a downgrade for this group.** The 422 removes the
decision rather than changing it.

## Carried forward for Groups D and E

- **Group D (dragons) will hit the same 422.** Four-legged winged bodies are further from a humanoid
  pose than a wolf is. Their clips (`Flying_Idle`, `Fast_Flying`, `Headbutt`) and
  `DragonMeleeComponent`'s three hitboxes have no humanoid equivalent. Do not spend 92 credits to
  confirm this; spend 23 on one drake if evidence is wanted.
- **Group E (formless) may still work as mesh-only.** `AshMaw`, `CinderWisp`, `RuinCrawler` and
  `StormMote` carry in-house 5-clip sets. If a Meshy mesh were adopted it would lose them the same
  way, so the same objection applies unless the maintainer accepts static creatures.
- **The rig gate is only reachable for humanoids.** `tools/meshy_rig_probe.gd` was never run this
  session because no rigged asset existed to run it on.

## Retracted: `ThornbackBoar` is NOT a shipped defect

⚠️ **An earlier version of this report called `enm_thornback_boar.glb` a third shipped model-path
defect. That was wrong, and the error is worth recording because it is a repeatable one: the bare
GLB was rendered instead of the assembled actor.**

The bull base is deliberate and documented. `assets/CREDITS.md:375` records
`enemy.thornback_boar → animals/Bull`, and `tools/build_enemy_identity_assets.py:234` states the
intent in a comment:

> "Thornback: the retained cattle rig gains a low snout, paired tusks and a thorned back ridge."

This is the standing strategy for the **whole** non-humanoid roster, not a one-off: keep a sound
vendored animal rig and bolt species identity onto it from `enemy_identity_kit.glb` via
`EnemyVisualKit`. `DireWolf` is the same wolf with a Mane and Fangs; `FrostStalker` is a **husky**
with a Ridge and Mask; `AshfallElk` is a Stag. Judged from the bare mesh, every one of them is
"the wrong animal". Judged in game, they are not.

**The lesson: `--enemy-shots` is the tool, not a raw-GLB render.** `src/Debugging/EnemyShots.cs`
builds real archetypes through `EnemyArchetypeFactory`, so it exercises the authored model path,
the identity attachments, gameplay scale and the animation resolver. Run
`godot --path . -- --enemy-shots` (**without `--headless`** — it needs a framebuffer) and read
`user://enemy_shots`. It is the fifth instance of this repo's standing "RENDER IT" trap, and the
first where the wrong render produced a confident false defect report.

## The real defect, and it is one line

`BoarHead` — a snout, paired tusks and a brow, authored specifically for this archetype — was
**the only one of the identity kit's 40 pieces that no profile referenced.** It was built and never
wired up. The boar's Head slot borrowed `AshMawJaws` instead, so the archetype wore the AshMaw's
plating and read as a generic armoured beast with the bull's horns showing through.

Fixed in `src/Enemies/EnemyVisualKit.cs` by putting the boar's own head piece in its own Head slot.
`AshMawCarapace` stays on the Torso — that is shared bulk, not identity. `EnemyVisualKitTests` now
asserts `BoarHead` rather than `AshMawCarapace`, because the carapace's presence never proved the
archetype had a silhouette of its own; it did not.

Zero credits. No new assets.

`boar_before_left.png` and `boar_after_left.png` are the `--enemy-shots` left views either side of the
change: before, a bovine muzzle inside the AshMaw's curved jaw band; after, the crown mass, blunt
snout and tusks of the piece that was always meant to be there.
