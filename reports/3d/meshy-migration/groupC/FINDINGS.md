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

## Second finding: `ThornbackBoar` is the Quaternius bull

Confirmed, and not a matter of stylisation. `assets/models/creatures/enm_thornback_boar.glb`:

- its single mesh is named **`Cow`**;
- it carries the same 25 clips as `assets/library/animals/bull.glb` and `cow.glb`
  (`Attack_Headbutt`, `Attack_Kick`, `Eating`, `Gallop`, `Gallop_Jump`, …);
- rendered in Godot at eye level it is a black bovine — long straight back, tufted cow tail, cloven
  hooves, short straight horns from the crown of the skull. No tusks, no wedge snout, no dorsal
  bristle ridge, no shoulder hump.

**This is a third shipped model-path defect** alongside the two fixed in `af1b34c`. It is *not*
fixed here, because every route to fixing it is blocked or is the maintainer's call:

1. The vendored library has no boar. `assets/library/animals/` is 12 models
   (alpaca, bull, cow, deer, donkey, fox, horse, husky, shiba_inu, stag, white_horse, wolf) and the
   Poly Pizza manifest's only near-hit is "Pigeon".
2. Meshy cannot produce a rigged one — this session's finding.
3. Re-pointing the archetype at another vendored animal does not produce a boar either.
4. A CC0 web pull (`ASSET_POLICY` step 3) would work, but this wave was scoped
   **custom-generation-only**, so it needs the maintainer to reopen sourcing.
5. Renaming the archetype to match the model is a content decision, not an engineering one.
