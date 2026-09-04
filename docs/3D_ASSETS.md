# Embervale 3D assets

The contract for every model in this game. It is the only 3D document you need to read: the rules
here are current, and nothing under `reports/3d/archive/` is required reading.

```
source / generated asset  ->  assets.py adopt  ->  assets.py validate  ->  gameplay
```

**Start here:**

```powershell
python tools/assets.py status      # what exists, in which rig family, and what drifted
python tools/assets.py validate    # every hard gate, in the order they have to run
```

`tools/assets.py` is the only entry point you need. It wraps twenty scripts and encodes the order
they run in; you should not have to know which is which.

| Command | What it is for |
| --- | --- |
| `status` | The production inventory and manifest drift. No engine, no Blender, about two seconds. |
| `validate` | The hard gates. Run before every commit that touches a model. |
| `adopt SRC DEST` | A source model becomes a validated production asset. One command. |
| `audit` | Full Blender + Godot inspection and the report set. Slow; run it for a broad pass. |
| `build TARGET` | A Blender rebuild plus the follow-up steps it must not skip. |

---

## The five families

Every model is exactly one of these. The family is **derived**, not declared — `assets.py` reads
the glTF and its `.import` sidecar and works it out, so it cannot drift from the files.

| Family | What it is | Rig | Animation |
| --- | --- | --- | --- |
| [HUMANOID](#humanoid) | People and people-shaped enemies | Retargeted to `GeneralSkeleton` | Shared 46-clip library + its own clips |
| [QUADRUPED](#quadruped) | Beasts, mounts, dragons | Its own, untouched | Its own clips only |
| [STATIC PROP](#static-prop) | Furniture, containers, nature | None | None |
| [ARCHITECTURE](#architecture) | Buildings and wall modules | None | None |
| [FIRST-PERSON / VIEWMODEL](#first-person--viewmodel) | The arms you see in first person | None | Procedural, written in C# |

**Naming is the family's first signal and it is enforced.** `chr_` player · `npc_` NPC bodies ·
`enm_` enemies · `boss_` bosses · `mnt_` mounts · `fp_` viewmodel · `prp_` props · `bld_` buildings
· `mod_` wall modules · `eqp_` equipment · `wpn_` weapons · `anim_` animation.

### The manifest

`assets/models/manifest.json` is **derived, never hand-edited**. Regenerate with
`python tools/assets.py status --write` and commit it alongside the `.glb` and its `.import`.

```json
{ "id": "chr_player_base", "path": "res://assets/models/characters/chr_player_base.glb",
  "type": "HUMANOID", "rig": "general_skeleton", "anim": "shared_library",
  "bone_map": "bonemap_meshy_s3_c188e7a9", "root_scale": 1.0,
  "height_m": null, "refs": 4, "status": "active" }
```

It holds runtime truth only. Prompts, Meshy task ids and session commentary belong in
`reports/3d/archive/meshy-migration/manifest.csv`, which is the provenance ledger.

`height_m` is null until a local `assets.py audit` measures it, and that is deliberate: the number
must come from Blender's evaluated geometry, and a stale one is worse than none because a collision
capsule gets authored against it.

### The runtime boundary

Gameplay code names a model through `src/Core/ModelAssets.cs`, never a `res://` literal.
`ContentValidator.ValidateModelAssets` checks every name resolves and is in the manifest, so a path
that stops working fails `--validate` instead of silently greyboxing.

Scene-placed models are the exception and stay that way: the `ext_resource` entries in `scenes/`
are direct engine references that already fail loudly.

---

## Where models come from

**Two lanes. Which lane depends on what you are making.**

**Characters and creatures — generate with Meshy.** The cast is custom Meshy generations and the
direction is **semi-realistic**, matching the player, Kael, the goblin and the Iron King. This is a
deliberate departure from `docs/ART_STYLE.md` §1's faceted low-poly direction, which still governs
everything else. A faceted character will not sit beside the existing cast as one world.

The prompt is this stem plus a per-entity clause naming role, faction and region. Under 600
characters (the API limit), `pose_mode: "t-pose"`, `aspect_ratio: "3:4"`:

> Full-body front view of \<SUBJECT>, T-pose, arms straight out to the sides, plain flat grey
> background. \<SILHOUETTE, GARMENTS, ROLE AND FACTION DETAIL>. Muted desaturated ash-grey and
> faded-earth-brown palette, cold iron buckles, one small ember-orange accent. Semi-realistic AAA
> fantasy game character, grounded weathered Skyrim-like realism, physically based materials,
> believable cloth folds, worn leather and edge-worn metal. Adult proportions, 7.5 heads tall.
> No weapon, no scenery.

The ember-orange accent is the one warm colour in the palette. Keep it in every prompt and keep it
small — it is what makes separately generated characters read as one faction set.

Pipeline: `meshy_text_to_image` (nano-banana, 3cr) → `meshy_image_to_3d` (`smart-topology`,
textured, 3,000–4,000 tris, 15cr) → `meshy_rig` (5cr, walk + run included). **23 credits per
character.**

**Props, architecture and nature — the four packs first.** `assets/library/` holds 1,136 vendored
CC0 models behind a `.gdignore`; the medieval megakit, interiors, nature megakit and the animation
library cover almost everything. `ls` the pack and read `manifest.json` before concluding it lacks
something — the library has been declared empty from memory twice and was wrong both times. Only
then the other vendored bundles, then the open web (CC0/MIT only), then Blender.

⚠️ **Do not mix kits.** Four kits by one author read as one world; a model from a fifth source
reads as a mistake even when it is better made.

**Crediting is not required.** The build is personal, never published, never sold, and everything
in it is CC0. `assets/CREDITS.md` is frozen as history — do not add to it, and do not treat a
missing entry as unfinished work.

---

## Adopting a model

```powershell
python tools/assets.py adopt <source.glb> assets/models/characters/npc_name.glb
python tools/assets.py adopt <source.gltf> assets/models/props/prp_name.glb --kit
```

That one command repacks the payload at a 1024 texture cap, derives the bone map, normalises clip
names, patches the `.import`, runs the Godot import, regenerates the manifest and — for a humanoid —
runs the retarget gate. Then commit the `.glb`, its `.import` and `manifest.json` together.

**Things it handles so you do not have to:**

- **Never hand-write a BoneMap.** `meshy_adopt.py` derives it by walking the hierarchy to the
  shoulder-carrying joint. Hand-writing one is how the spine gets mapped in name order, which is
  wrong — see HUMANOID below.
- **Never round-trip a rigged model through Blender.** It destroys bone-parented children
  (`npc_hooded` carries a `Sword` under `Middle1.R`). When a rig already fits, the correct
  adaptation is a **file copy** and an `.import` edit.
- **Never blindly normalise a root scale.** `nodes/root_scale` corrections are per-model and
  intentional. `mnt_horse` measures 4.76 m and is a normal horse, because its armature carries a
  100× scale that `root_scale=0.5` corrects.

**Things you still have to think about:**

⚠️ **A replacement inherits its predecessor's `.import`, root scale included.** `npc_woman_dress`
carried `root_scale 0.384`; a replacement dropped in on top would have imported at 38% of its size.
`adopt` warns when it sees this. Confirm the value is still right or pass `--root-scale`.

⚠️ **A model swap does not inherit its predecessor's collision.** Re-fit the capsule and hitbox to
the measured bone rest heights. Render geometry, navigation, physics collision, hurtboxes and
hitboxes are related but separate contracts.

⚠️ **Adopt, import, then commit — in that order.** Godot's `detect_3d` pass rewrites a texture's
`.import` after its first 3D use, flipping `compress/mode` 0 → 2. Commit before importing and you
commit a file the engine is about to change. The shipped convention is `compress/mode=2` with
`detect_3d/compress_to=0`.

⚠️ **Measure in the engine, not by parsing glTF accessors.** Instantiate the imported `PackedScene`.
A skinned mesh's raw AABB is bind-space and can be hundreds of metres.

---

## HUMANOID

People and people-shaped enemies. 33 of them, and they are uniform: a bone map in the `.import`
retargets each onto `SkeletonProfileHumanoid`, and the importer's bone renamer unifies every
skeleton as **`GeneralSkeleton`**.

That name is the whole contract. `CharacterAnimationComponent.AddSharedLibrary` attaches the shared
46-clip library **only** when the imported `Skeleton3D` is literally called `GeneralSkeleton` — it
is the retarget's own marker, and it is why an unretargeted body gets no library rather than a
broken one.

**What the library actually buys is three slots, not animation.** Every adopted body already ships
its own clips, and `idle`, `run`, `attack`, `hit` and `death` resolve from them. Only `block`,
`cast` and `channel` come from the library. Do not plan work assuming characters are unanimated.

`AnimationClips.Resolve` offers a model's **own** clips first and lets the library answer only what
it alone can. It strips armature prefixes and a leading `Female_`/`Male_`, and recognises
`HitReact` and `Idle_HitReact` alongside `HitRecieve`.

### The gate

```powershell
godot --headless --path . --script res://tools/meshy_rig_probe.gd -- --asset res://path.glb
```

**This is a gate, not a spot check**, and `assets.py validate` runs it over every humanoid. It
proves the skeleton is named `GeneralSkeleton` and carries all 22 required profile bones.

⚠️ **A T-posing NPC is the only symptom an unresolved rig ever has.** A body whose retarget did not
run imports cleanly, compiles, passes the tests, passes `--validate`, and then stands in the market
in its bind pose. `npc_woman_dress` did exactly that from the day she was adopted until someone
looked at her. Nothing but this probe and a render will tell you.

### The two silent failures

⚠️ **The Meshy spine naming is inverted.** The hierarchy is `Hips → Spine02 → Spine01 → Spine`, so
`Spine02` is the **lowest** spine bone and `Spine` the highest. Mapping them in name order mangles
the retarget. This is why the bone map is derived and not hand-written.

⚠️ **The `_subresources` path key names the node in the SOURCE scene, and the two sources differ.**

| Source | Key |
| --- | --- |
| Meshy | `PATH:Armature/Skeleton3D` — starts at the scene root's **first child** |
| Quaternius | `PATH:RootNode/CharacterArmature/Skeleton3D` — starts at the **root** |

Godot names the root after the file, so anchoring a Meshy asset on the root never matches. When the
key matches nothing the model still imports fine, keeps its raw bone names, never becomes
`GeneralSkeleton`, never receives the library, and T-poses — with no error at all. This is how
`npc_merchant_m` un-retargeted itself mid-session.

### A root node that carries translation

⚠️ **A model whose root node has a translation cannot be retargeted until that is fixed.**
`chr_player_base`'s `RootNode` had `T = [0, 4.8237, 0]`, cancelling the skeleton's own offset —
two errors that cancelled, so it rendered correctly and broke in all four rest-fixer settings
(sinks 4.8 m, or the spine shears and the player bends double at the waist). There is no third
setting.

`python tools/normalize_rig_root.py <file.glb>` collapses the cancelling transform into the
armature and the root bone. ⚠️ The animation keyframes have to move with the rest pose, and
**several animations share one output accessor**, so each must be rewritten exactly once.

### The NPC outfit kit

Human NPCs get their identity from a cosmetic layer over the shared bodies, not from recolouring:
`assets/models/equipment/npc_kit_embervale.glb` (22 rigid pieces) and the profile table in
`src/Npc/NpcVisualKit.cs`, keyed by `Entity.TemplateId`.

The source body, `Skeleton3D`, `BoneMap`, skin, animation library and collision remain
authoritative. Kit pieces are rigid followers of `Chest` or `Hips` using the animated bone delta
while preserving the model's world axes — required because the retargeted bodies do not share
identical bone-local axes.

**Attachment is one system now** (2026-09-04, the combat/animation overhaul). `EquipmentSockets`
is the contract — a socket vocabulary (`HandR`, `HandL`, `BackPrimary`, `Shield`, `Bow`, `Quiver`,
`Head`, `Chest`, `Hips`, `ShoulderL/R`, …), the bone names each accepts in preference order, and the
space a piece on it is oriented in. `EquipmentPresentationComponent` is the only thing that hangs
anything on a body: player, NPC, enemy, companion and boss. The five implementations it replaced —
`PlayerFactory.AttachWeaponVisual`, `PlayerFactory.AttachGear`, `NpcKitFollower`, `EnemyKitFollower`
and their bone-name guessing — are deleted.

⚠️ **The motion review that had been deferred was done, and its answer is `SocketSpace`.** Both
behaviours were correct and genuinely different, which is why one could not simply replace the other:

| Space | Basis | For |
| --- | --- | --- |
| `BoneLocal` | the bone's own — a native `BoneAttachment3D`, no per-frame script | held things: a sword rolls with the wrist |
| `BodyAligned` | `pose · rest⁻¹` applied to the character's axes | worn things: the retargeted bodies do not share bone-local axes, so a pauldron authored upright on one chest lies on its side on the next |

Every kit piece passes `BodyAligned` **explicitly** and names its authored bone as the *preferred*
one, so nothing moved in the migration — the socket's own candidate list is only the fallback for a
rig that lacks that exact bone. Several kit pieces sit on quadruped rigs carrying both a `Spine` and
a `Torso`, and resolving those purely through the humanoid preference order would walk a carapace up
the animal's back.

`WeaponGrip.Hand` holds the one grip correction, derived from the basis that used to live privately
inside `PlayerFactory` — which is why every companion, NPC and enemy that carried a weapon carried it
unrotated.

Two gates keep it honest: `EquipmentSocketTests` pins the alias table without an engine, and
`tools/equipment_socket_probe.gd` proves it against **all 32 humanoid rigs on disk** plus one real
attachment that has to end up on the hand bone. A bone-name miss used to be completely silent — the
player's visual sword was `QueueFree`d on every spawn for an entire phase — and it now warns.

`Build.Slim/Standard/Broad` alter cosmetic width only — they never scale a skeleton or move
vertices in a skinned body. Profiles are deterministic, authored from profession, wealth, faction,
location and story importance, never a random roll. **Keep the four-piece ceiling.** A missing kit,
profile or bone degrades to the plain body and must not affect gameplay.

Existing human GLBs receive **JSON material corrections only** — geometry, skins, inverse binds,
animations, nodes and accessors are never re-exported.

---

## QUADRUPED

Beasts, mounts and dragons. 15 of them, and **they do not go through the humanoid system.**

No bone map, no retarget, no shared animation library — by design. A quadruped keeps its own rig
and its own clips, and `AnimationClips` carries the aliases that map gameplay slots onto their
vocabulary (`Bite_Front`, `Flying_Idle`, `Jog_Fwd`, `gallop`). `HumanoidBones.FindHand` returns
empty for them, which is correct: a wolf has no hand.

**Do not try to force a quadruped onto `SkeletonProfileHumanoid`.** It was attempted and closed as
not migratable. Their identity comes from bolt-on pieces from
`assets/models/equipment/enemy_identity_kit.glb` via `src/Enemies/EnemyVisualKit.cs`, attached to
body bones (`Torso`, `Head`, `Back`) rather than `Chest`/`Hips`, plus an optional body tint.

**Keep the working legacy quadrupeds.** They are sound vendored animal rigs and there is no
superior safe replacement.

Multi-hit-zone bodies (dragons) come from `EnemyArchetypeResource.HitZones` + `HitZoneResource`,
with a zone-blob greybox fallback.

**A mount is a state of the rider, not a second body.** `MountComponent` parents the horse GLB
under the player body as `MountVisual`, rotates it π for the glTF `+Z` → Godot `-Z` convention, and
drives its own `AnimationPlayer` for `idle`/`run`/`gallop` while suppressing the rider's run loop.
`SaddleHeight` and `SaddleForward` are hand-measured against the imported model and are **not
derivable from the file** — if you replace the horse, re-measure them.

---

## STATIC PROP

Furniture, containers, nature, everything a scene places and nothing animates. The largest family
(99) and the simplest.

- Adopt as a **container change only** — the buffer is copied byte for byte. `--kit` handles this.
- Import as `StaticBody3D`-ready with **author-time collision** (`-col`/`-convcol` name suffixes),
  never runtime-parsed visual-mesh collision.
- Scale corrections go in the `.import` as `nodes/root_scale`, never in one cell's node transform —
  the `.import` reaches every placement. ⚠️ The `rts` pack is roughly **1/6 scale** and nothing in
  the files says so. Measure any candidate against a 1.8 m reference.
- **Shared textures stay shared.** Nature families resolve to one `T_Nature_*.png` each. `assets.py
  validate` checks this both statically and in the engine, because embedding them per-model is how
  twelve wall modules once cost 204 MB of the same textures twelve times over.

⚠️ **There are no ground textures and there must not be.** The terrain is six painted noise layers
from `data/terrain_layers/`. A CC0 PBR ground pack is the reflex here and it would make the terrain
the only photographed thing in a hand-painted world.

---

## ARCHITECTURE

Buildings and wall modules. Composed offline into authored scenes; there is no runtime procedural
building logic and there should not be.

```powershell
python tools/compose_building.py <name> <wide> <deep> <storeys>
  [--hollow | --open | --ruined] [--wall-family plaster|stone-ground|stone]
  [--roof-axis x|z] [--door-index N] [--chimney left|right]
  [--shutters] [--dormer] [--awning] [--balcony] [--stairs] [--weathering]
```

Width and depth are module counts on the 2 m wall grid. **Every generated scene embeds its exact
regeneration command**, and `check_architecture_kit.py` asserts it — so a building is always
reproducible from its own file.

**A variant changes structure, not dressing.** Footprint, storeys, roof direction, wall family,
access or attached structure. Do not publish a prop swap as a new building.

**Collision is the load-bearing distinction:**

| Form | Collision |
| --- | --- |
| Solid shell | Exterior set piece. Intentional — do not imply its decorative door opens. |
| Hollow / open / ruined | The enterable forms. **Must retain per-wall collision.** |

⚠️ Architecture that is placed and has no collision anywhere is a **critical** audit finding, not an
advisory one. Validate entrances, adjacent walls, floors, breaches and stairs with the real player
capsule: `godot --headless --path . --script res://tools/building_collision_probe.gd`.

Shared material families (`MI_Plaster`, `MI_UnevenBrick`, `MI_WoodTrim`, `RoundTile`) reuse shared
production textures. Do not fork a texture per prefab for cosmetic variation.

---

## FIRST-PERSON / VIEWMODEL

**Structurally separate from world characters, and the separation is the point.** These meshes are
never seen by anyone but the player holding them, and they follow different rules from every body
in the world.

`fp_arm_left.glb` and `fp_arm_right.glb` are authored as two real meshes — the left is not a
negative-scale mirror. They live under a `FpArms` node on the player camera, not in the world.

- **No rig, no baked clips.** All motion is procedural in `FirstPersonArmsComponent`: walk bob, a
  slash arc alternating by combo index, guard blend, cast and interaction beats.
- **No collision.** Cosmetic viewmodel arms and body equipment have none. The melee hitbox stays
  owned by `MeleeWeaponComponent` and is never inferred from render geometry.
- **A fake second camera.** `ViewmodelFov = 55` and `ApplyViewmodelScale()` scale the arms by the
  half-angle tangent ratio rather than rendering a separate pass.
- **Semantic sockets are the interface**: `WeaponSocket`, `SpellSocket`, `InteractionSocket`. VFX
  and equipment attach to those names and never to a mesh path.

### Weapons in the hand

First-person and third-person share the same weapon GLB. Build a separate hero version only when
the world mesh demonstrably fails in gameplay framing.

**The coordinate contract:**

- 1 Blender unit = 1 metre, exported at scale `1.0`, transforms applied. The imported Godot root
  must remain identity scale.
- The functional long axis is local **+Y** — for a sword, grip to point. **+Z** is the face, **+X**
  the wielder's right.
- The origin sits **on the centreline of the grip**, at the point the hand should own. Not the mesh
  centroid, not the point.
- Reference size, the iron sword: `0.223 × 0.960 × 0.051 m`, wrapped grip centre at local
  `Y = 0.03 m`. One-handed grips 28–36 mm in diameter.

⚠️ **Do not add a second compensating transform inside a weapon GLB.** First-person placement is
derived from a measured fist point in `FirstPersonArmsComponent.GripTransform()`. Class-specific
offsets belong in equipment data or a socket profile, not in a differently-rotated mesh.

Scabbards, sheaths and quivers are separate `eqp_*` assets on named body sockets, so a drawn weapon
and its empty scabbard coexist without duplicating gameplay state.

---

## Materials

Name materials by physical role (`LightSteel`, `WornLeather`, `GripWrap`), not numbered Blender
defaults. One material per physical response; do not merge metal, leather, wood, cloth or skin just
to lower a count.

| Surface | Metallic | Roughness |
| --- | --- | --- |
| Iron / steel | 0.8–1.0 | 0.28–0.55 |
| Leather / wood / grip wrap | 0.0 | 0.58–0.82 |
| Cloth | 0.0 | 0.78–0.95 |
| Skin | 0.0 | 0.58–0.78 |
| Stone / plaster | 0.0 | ~0.75 |

⚠️ **A metallic factor on skin, cloth, wood or stone is a defect the audit flags as high**, and it
is the single most common thing a Blender export reintroduces — the exporter resets material
factors on every write. That is why `assets.py build` runs `repair_architecture_materials.py`
afterwards and why you should use it rather than calling the build scripts directly.

Normal maps use the Godot/glTF tangent convention. Inspect both lit sides after import; **never fix
inverted normals with a double-sided material.**

Wear is restrained and readable at gameplay distance: bevel highlights, varied roughness, localised
edge wear — not noisy full-surface damage.

---

## Validating

```powershell
python tools/assets.py validate    # manifest drift, static audit, rig gate, textures, architecture
python tools/assets.py audit       # full Blender + Godot inspection, into reports/3d/runs/
```

`audit` is **read-only** — it imports into temporary scenes and never writes a production asset.
`inventory.json` and `findings.json` are the machine-readable outputs; the Markdown files are the
review surface.

**Flags are triage evidence, not permission to edit.** Before changing any shared model, trace its
dependents through the inventory's `usage.files` list. The audit's usage list is where the review
starts, not the whole of it.

### Visual QA is not optional and nothing automated replaces it

Compilation, import, tests and `--validate` are all necessary and **none of them is visual
validation**. Every one of the traps in this document was invisible in a log and visible only in a
render.

`python tools/assets.py audit --render all` produces six views per model — front, back, left,
right, front three-quarter, rear three-quarter — each with a ground plane, a 1.8 m human reference,
a bounding box and RGB origin axes.

⚠️ **Judge a candidate from behind and at eye level.** An open-backed cottage nearly shipped twice.
A hi-vis vest and hard hat stood in a medieval market until someone rendered it close up. This trap
has fired at least four times, and it has never once been visible from a filename.

For a rigged actor, also review idle, movement, and attack or equipment poses. **Never approve a
model from audit text alone.**

⚠️ **The world visual gate is nondeterministic and its result is advisory.** It renders on a
GPU-less CI runner under xvfb, which is not the renderer its baselines were captured on. Run
`python tools/world_quality_check.py` locally, where a frame can actually be looked at.

---

## Rebuilding derived assets

```powershell
python tools/assets.py build npc-kit | enemy-identity | environment | player-weapons | anim-library
```

⚠️ **Use this rather than calling the `build_*` scripts directly.** Blender's glTF exporter
re-embeds the shared rock atlas and resets material factors on every write, so an export must be
followed by `share_nature_textures.py` and then `repair_architecture_materials.py`, in that order.
`assets.py build` runs the sequence; a bare script call does not, and the result is a duplicated
200 MB texture set and metallic plaster.

`anim-library` regenerates `anim_library.res` from its `.glb`, which keeps the source file's
Mannequin mesh out of the build. Re-run it whenever that `.glb` or its retarget settings change.

⚠️ **The library is stripped to an upper-body pose from the hips up**, and that is correct.
Its feet are IK goals parented to `Root`, not FK bones — writing FK foot rotation onto a
root-parented goal pins the boot while the leg swings, as a black spike out of the ankle. Its
position tracks are stripped too: nothing in this game consumes root motion, and the hip track
landed on top of the body bone's own lift and stood a merchant floating at 1.63 m.

---

## Preserving sources

`assets/library/` is source art behind a `.gdignore` — Godot never imports or exports it. A model
enters the game only by being adopted into `assets/models/`. **Never make an irreversible edit in
`assets/library/`**, and never adapt anything except through a repeatable script.
