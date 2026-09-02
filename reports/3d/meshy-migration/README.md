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
