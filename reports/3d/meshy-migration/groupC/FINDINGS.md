# Group C (quadrupeds) — pilot result: DO NOT MIGRATE

Date: 2026-09-02. Branch `claude/meshy-migration-groupc`.
Spent: **18 credits** (829 → 811). Budgeted 23; the rig never charged.

## Verdict

**Group C stays on its legacy models.** Not because a quadruped cannot be rigged at all — it can,
in the web app — but because Meshy ships **only a walk cycle** for one, which is fewer clips than
these five already have.

### What the API does

`meshy_rig` rejects the wolf mesh with HTTP **422 — "Pose estimation failed, please provide a valid
model"**. Reproduced twice, once via `input_task_id` and once via `model_url`, so it is the model
that is refused and not the plumbing. It never charged, which is why the pilot cost 5 credits less
than budgeted.

This is documented behaviour, not a bug. The Rigging API's request body is exactly
`input_task_id` / `model_url` / `height_meters` / `texture_image_url` — **there is no character-type,
rig-type or skeleton parameter to set** — and the docs state:

> "Please note that programmatic rigging currently only works well with standard humanoid (bipedal)
> assets with clearly defined limbs and body structure at this time."

listing "Non-humanoid assets" among the unsupported inputs.

### What the web app does, and why it still does not help

⚠️ **The web app is not limited the same way, and an earlier draft of this report was wrong to say
Meshy cannot rig a quadruped.** The web rigging flow offers a manual character-type choice —
**Humanoid**, **Quadruped** (four-legged animals), or **Smart Rig (Beta)** — so the wolf mesh
generated here *can* be rigged by hand, exactly the way the maintainer produced `chr_player_base`,
`npc_kael`, `enm_goblin` and `boss_iron_king` before this migration. The mesh is sitting in the
workspace as task `01a06532-e29d-7664-bcdb-5d82fed2321c`.

**The blocker is animation coverage, not rigging.** Meshy's help documentation states:

> "Currently, walking is the only animation we support for quadrupeds."

The 600+ motion presets are humanoid-only. So a web-app-rigged wolf would arrive with **one clip**,
and it could not borrow the shared 46-clip `anim_library.res` either, because that retarget runs
through `GeneralSkeleton` / `SkeletonProfileHumanoid` and a quadruped rig is not one.

Measured against what these archetypes already have, that is a straight loss:

| | Legacy `AnimalArmature` | Web-app quadruped rig |
| --- | --- | --- |
| idle | `Idle` | *(empty — bind pose)* |
| run | `Walk` | `Walk` |
| attack | `Attack` | *(empty — never bites)* |
| hit | `Idle_HitReact_Left` | *(empty)* |
| death | `Death` | *(empty — never falls)* |

`AnimationClips.Resolve` returns empty for a slot with no match, and callers guard on length, so
this fails silently: the wolf would walk at you and then stand in its bind pose through the entire
fight and its own death.

### The one path that would actually work

Retarget the **legacy `AnimalArmature` clips onto a new Meshy quadruped rig**. Both are quadruped
skeletons, so the motion is compatible in principle. It needs a custom `SkeletonProfile` for a
four-legged body plus a hand-checked `BoneMap` per species — `bonemap_meshy.tres` and
`meshy_adopt.py`'s hierarchy-walking derivation are both built around the humanoid profile and
would not carry over. That is a real engineering task, not a pipeline flag, and it buys a better
mesh on the same five animations these models already play. **Not recommended without a specific
reason to want the new silhouettes.**

The remaining four quadrupeds were not attempted, per the brief.

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

## Why the mesh alone is not adoptable

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

- **Group D (dragons) will hit the same 422 on the API**, and the web app will not save them either:
  a winged quadruped is not covered by the Humanoid or Quadruped rig, and "walking is the only
  animation we support for quadrupeds" is fatal for creatures whose locomotion clips are
  `Flying_Idle` and `Fast_Flying`. `DragonMeleeComponent`'s three hitboxes (Bite / Wing / Tail) have
  no Meshy equivalent at all. **Smart Rig (Beta) is the only untested option** — if the maintainer
  wants evidence, it is one web-app rig on one existing mesh, not 92 credits of new generation.
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
