# Meshy migration

Tracks the replacement of Embervale's 3D character art with custom Meshy generations.
`manifest.csv` is the record; this file is the standing procedure.

## Scope of the current wave

Characters and creatures only. Props, architecture and equipment are deferred to a funded
Wave 2 -- a full 150-asset migration costs ~3,200-4,000 credits and the balance was 1,185.
The Meshy MCP exposes no Marketplace search or download, so every asset here is
custom-generated rather than sourced.

## Per-model procedure

1. `meshy_text_to_image` (nano-banana, 3 credits) with the ART_STYLE stem, then
   `meshy_image_to_3d` with `model_type: "smart-topology"`, textured, 3,000-4,000 tris
   (15 credits), then `meshy_rig` (5 credits, walk + run included). 23 credits per character.
2. `python tools/meshy_adopt.py <export.glb> assets/models/<dir>/<name>.glb --patch-import`
   -- repacks at a 1024 texture cap and points the sidecar at `bonemap_meshy.tres`.
3. `godot --headless --path . --import`
4. `godot --headless --path . --script res://tools/meshy_rig_probe.gd -- --asset res://...`
   This is the gate, not a spot check. See below.
5. Re-fit the capsule/hitbox to the measured bone rest heights; a model swap does not inherit
   its predecessor's collision (`CLAUDE.md` 12).
6. Record the row in `manifest.csv`, then delete the legacy asset.

## The two silent failures this pipeline has already hit

**The spine naming is inverted.** The Meshy hierarchy is
`Hips -> Spine02 -> Spine01 -> Spine`, so `Spine02` is the LOWEST spine bone and `Spine` the
highest. Mapping them in name order mangles the retarget.

**The `.import` `_subresources` key starts at the scene root's FIRST CHILD**
(`PATH:Armature/Skeleton3D`), not at the root. Godot names the root after the file, so
anchoring on the root never matches -- and when it does not match, the model still imports
fine, keeps its raw 24 Meshy bone names, never becomes `GeneralSkeleton`, never receives the
shared animation library, and the actor T-poses with no log and no error.
`tools/meshy_rig_probe.gd` exists to turn that into a hard failure.

## Animation

Meshy rigging ships walk and run free; the other seven gameplay slots (idle, block, attack,
hit, death, cast, channel, ride) come from the existing 46-clip `anim_library.res` through the
retarget, which costs nothing and keeps combat timing intact. `meshy_animate` clips were
scoped out for this wave.

Meshy clips are in-place -- measured hips translation is a few centimetres of bob and sway per
cycle, not forward travel -- so no root-motion strip is needed.

## The prompt stem (maintainer-confirmed 2026-09-02)

Characters are **semi-realistic**, matching the four models the maintainer generated before this
migration (player, Kael, goblin, Iron King). This deliberately departs from `docs/ART_STYLE.md`
§1's "low-poly but detailed / carved, not sculpted" direction, which still governs environment
art. The first pilot image was generated faceted, per the art bible, and was rejected for that
reason -- it would not have sat beside the player and the Iron King as one world.

Every character prompt is this stem plus a per-entity clause:

> Full-body front view of <SUBJECT>, T-pose, arms straight out to the sides, plain flat grey
> background. <SILHOUETTE, GARMENTS, ROLE AND FACTION DETAIL>. Muted desaturated ash-grey and
> faded-earth-brown palette, cold iron buckles, one small ember-orange accent. Semi-realistic AAA
> fantasy game character, grounded weathered Skyrim-like realism, physically based materials,
> believable cloth folds, worn leather and edge-worn metal. Adult proportions, 7.5 heads tall.
> No weapon, no scenery.

Keep it under 600 characters (the API limit). `pose_mode: "t-pose"`, `aspect_ratio: "3:4"`.
The ember-orange accent is the one warm colour in the palette (`ART_STYLE.md` §2) and is what
makes a roster of separately generated characters read as one faction set -- keep it in every
prompt, and keep it small.

Per-entity clauses carry role, faction and region, not just clothing: the point is that
`npc_hooded` serves seven roles (Emberbound hierarch and seeker, hunter tracker, gate hand,
Ash Dunmore, Odo, Sedge) and has to read as plausible for all of them.

## Costs actually incurred

| Step | Model | Credits |
| --- | --- | --- |
| `text_to_image` | nano-banana | 3 |
| `image_to_3d` | meshy-t2 smart-topology, textured, 3.5k tris | 15 |
| `meshy_rig` | includes walk + run | 5 |
| **per character** | | **23** |

The first `npc_hooded` image (faceted, rejected on art direction) cost 3 credits and is sunk.
