# Embervale 3D assets

> **Developer SDK:** [Tooling reference](TOOLING.md). Use `python tools/embervale.py` for local/CI automation,
> structured results, safe scenarios, screenshots, and shared process/artifact handling.
> `world_quality_check.py` is now a compatibility entry point to this SDK.


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

`tools/assets.py` is the only entry point you need. It wraps about twenty scripts and encodes the order
they run in; you should not have to know which is which.

| Command | What it is for |
| --- | --- |
| `status` | The production inventory and manifest drift. No engine, no Blender, about two seconds. |
| `validate` | The hard gates. Run before every commit that touches a model. |
| `adopt SRC DEST` | A source model becomes a validated production asset. One command. |
| `adopt-batch` | Many generated static models at once: prep, write, one import pass, the texture budget, the manifest (outdoor world, [below](#outdoor-world-generated)). |
| `audit` | Full Blender + Godot inspection and the report set. Slow; run it for a broad pass. |
| `audit-weight` | Estimated texture video memory by class, and anything off its budget. Pure python, one second. |
| `build TARGET` | A Blender rebuild plus the follow-up steps it must not skip. |

---

## The five families

Every model is exactly one of these. The family is **derived**, not declared — `assets.py` reads
the glTF and its `.import` sidecar and works it out, so it cannot drift from the files.

| Family | What it is | Rig | Animation |
| --- | --- | --- | --- |
| [HUMANOID](#humanoid) | People and people-shaped enemies | Retargeted to `GeneralSkeleton` | Two shared libraries + its own clips |
| [QUADRUPED](#quadruped) | Beasts, mounts, dragons | Its own, untouched | Its own clips only |
| [STATIC PROP](#static-prop) | Furniture, containers, nature | None | None |
| [ARCHITECTURE](#architecture) | Buildings and wall modules | None | None |
| ANIMATION | `anim_*` clip sources — a skeleton and its clips, no mesh at all | Retargeted to `GeneralSkeleton` | *is* the animation |

**Naming is the family's first signal and it is enforced.** `chr_` player · `npc_` NPC bodies ·
`enm_` enemies · `boss_` bosses · `mnt_` mounts · `prp_` props · `bld_` buildings
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

**An ITEM names its model in its own `.tres`, through `ItemResource.WorldModelPath`**, because which
model a sword uses is content, not code. One field serves all three places an item is seen: the
wielder's socket (`EquipmentComponent`), the ground (`ItemPickupFactory`) and a trophy plinth
(`TrophyStandComponent`). ⚠️ **It lives on the base `ItemResource` and used to live on
`EquippableItemResource`** — where the two places that show an item the player is *not* wearing could
not reach it, so a potion on the floor was a glowing cube. Empty is legal and means the
rarity-tinted primitive; `ContentValidator.ValidateItemWorldModels` fails a non-empty path that stops
resolving or drifts out of the manifest.

⚠️ **A REFERENCE COUNT THAT SEARCHES FOR A LITERAL PATH CANNOT SEE A COMPUTED ONE.** `assets.py
status` reported `mod_roof_6x10`, `mod_stairs_exterior` and all 25 animation sources as
**unreferenced** and an audit proposed deleting them — but `compose_building.py` asks for a roof by
size (`roof_{wide*2}x{deep*2}`) and `build_meshy_anim_library.gd` reads its whole source folder, so
those names appear nowhere in the repository. `referenced_by_name` in `tools/assets.py` is the
allowlist; it still reports a genuinely dead file, and that was negative-tested. This is also why
`ModelAssets.cs` keeps every path a complete literal.

---

## Where models come from

**Three lanes (owner direction 2026-10-06, which supersedes "the four packs first, do not mix kits" for
the outdoor world). Which lane depends on what you are making.**

| Lane | What | Source |
| --- | --- | --- |
| Cast | characters, creatures, dragons, weapons | Meshy, semi-realistic (this section) |
| Outdoor world | trees, rocks, ice, landmarks, ruins, outdoor props | Meshy text-to-3D into `assets/models/world/`, plus two procedural ground-cover meshes ([below](#outdoor-world-generated)) |
| Kept kit | modular housing and architecture kit, enterable buildings, interiors, a listed set of small interactive props | Quaternius CC0 under `assets/library/`, on purpose: Meshy cannot make hollow, enterable buildings |

**Characters and creatures — generate with Meshy.** The cast is custom Meshy generations and the
direction is **semi-realistic**, matching the player, Kael, the goblin and the Iron King. This is a
deliberate departure from `docs/ART_STYLE.md` §1's faceted low-poly direction, which now governs only
the kept housing kit. A faceted character will not sit beside the existing cast as one world.

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

### ⚠️ INVENTORY BEFORE YOU GENERATE, AND THE INVENTORY IS THIS REPOSITORY

**The cast is already generated, adopted and in the game**: 32 characters and creatures by
2026-09-06, then the finish run's seven (the Storm Tyrant, Beast Lord, Crimson Prophet, Hollow
Queen, Ashen Knight, Morthul and the Archivist); `assets.py status` has the live count. Before spending a
credit on a living actor or a world model, read `reports/3d/archive/meshy-migration/manifest.csv` —
one row per generation, each carrying the task ids it came from — and `python tools/assets.py status`.
The player, Kael, the goblin and the Iron King are `chr_player_base`, `npc_kael`, `enm_goblin` and
`boss_iron_king`; they do not need making again.

⚠️ **DO NOT GO LOOKING FOR THEM IN THE MESHY ACCOUNT — THEY ARE NOT THERE ANY MORE** (verified
2026-09-06). Meshy tasks expire, and `GET /openapi/v2/text-to-3d` and `/openapi/v1/image-to-3d` both
return `[]` for this account: the four hand-exported bodies were made in the web app and were never
API tasks at all (the ledger says so in its own `prompt` column), and the rest have aged out. Only
the 25 `/openapi/v1/animations` tasks survive, which is why `anim_meshy.res` can be rebuilt for free.
**An empty API listing is not an empty library — it means the ledger is the library.** Reading it as
"nothing exists, generate it" is how the cast gets paid for twice.

The REST API is reachable directly with the `MESHY_API_KEY` already in the environment, and that is
worth knowing because the `meshy` MCP server does not always connect:

```bash
curl -s -H "Authorization: Bearer $MESHY_API_KEY" https://api.meshy.ai/openapi/v1/balance
```

⚠️ **The key lives in two places, both outside this repo, and this repo is public — never commit it.**
The user-level `MESHY_API_KEY` environment variable (REST calls above) and the `meshy` entry's `env`
in the user-level Claude config (the MCP server). **Rotating it means updating both**, then restarting
Claude Code, because MCP env is read at startup: `claude mcp remove meshy -s user`, then `claude mcp
add meshy -s user -e MESHY_API_KEY=<new key> -- node <path to @meshy-ai/meshy-mcp-server/dist/index.js>`,
and set the environment variable to match. Confirm with the balance call above (a bad key is a 401).

⚠️ **Finish-run lessons (2026-09-27).** The `meshy` MCP's `meshy_image_to_3d` fails for
`smart-topology`; call the REST endpoint (`POST /openapi/v1/image-to-3d`) with `$MESHY_API_KEY`
instead. A subagent asked to run Meshy may be denied the tools by permissions, so run generation from
the main session or check the subagent's permissions first.

**Outdoor world — generate, in `assets/models/world/`.** Text-to-3D with `meshy-5`, never the image
route; the full pipeline, texture classes, wrappers and the list of what is kept is in
[OUTDOOR WORLD](#outdoor-world-generated).

**Kept kit — the vendored packs.** `assets/library/` holds 1,136 vendored CC0 models behind a
`.gdignore`. The medieval megakit, interiors and animation library still supply the housing and
architecture kit, enterable buildings, interiors and the kept small props. `ls` the pack and read
`manifest.json` before concluding it lacks something — the library has been declared empty from
memory twice and was wrong both times. Then the other vendored bundles, then the open web (CC0/MIT
only), then Blender.

⚠️ **Do not mix kits inside the kept kit.** A model from a fifth source next to the housing kit reads
as a mistake even when it is better made. The generated outdoor world is the deliberate exception and
is bounded by the variety rule (two or three models per type).

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
`detect_3d/compress_to=0`, and `adopt` writes it together with the class size cap rather than
waiting for an editor session to do it (Materials → *Texture weight*).

⚠️ **`adopt` can classify a fresh Meshy rig as QUADRUPED on its first run.** Re-run `adopt` for that
one file alone; it classifies as HUMANOID the second time. Check the family in `manifest.json` (and
`assets.py validate`) before committing.

⚠️ **Measure in the engine, not by parsing glTF accessors.** Instantiate the imported `PackedScene`.
A skinned mesh's raw AABB is bind-space and can be hundreds of metres.

---

## HUMANOID

People and people-shaped enemies (`assets.py status` has the live count, 44 on 2026-10-07), and they are uniform: a bone map in the `.import`
retargets each onto `SkeletonProfileHumanoid`, and the importer's bone renamer unifies every
skeleton as **`GeneralSkeleton`**.

That name is the whole contract. `CharacterAnimationComponent.AddSharedLibrary` attaches the shared
46-clip library **only** when the imported `Skeleton3D` is literally called `GeneralSkeleton` — it
is the retarget's own marker, and it is why an unretargeted body gets no library rather than a
broken one.

**There are TWO shared libraries and they are different things, not two sizes of one.**

| Library | Built by | Holds | Why |
| --- | --- | --- | --- |
| `anim_library.res` | `tools/extract_anim_library.gd` | 46 Quaternius clips, **upper body only, rotation only** | Its Rigify source is `root → Hips`; the Quaternius bodies are `Root → Body → Hips`, so hip translation lands on top of a lift the body already has and stands the actor 1.63 m in the air with its legs strung out below. Its extractor therefore strips every position/scale track and all eight leg bones. In practice it buys three slots — `block`, `cast`, `channel`. |
| `anim_meshy.res` | `tools/build_meshy_anim_library.gd` | The Meshy clips plus two derived ones (`idle_alert`, `strafe_right`), **full body**, named for Embervale's own gameplay slots and processed at build ([below](#the-library-build)) | Generated on an existing Meshy character rig, which `meshy_adopt.py` fingerprints to the same shape hash (`s3_c188e7a9`) — and therefore the same bone map — as 31 of the 33 humanoid bodies. There is no hierarchy mismatch to compensate for, so **nothing is stripped and it drives legs.** |

⚠️ **The old library cannot be made to do what the new one does**, and that is worth knowing before
anyone tries: its stripping is a fix for a real defect, not tidying. If you need legs, hips, or
locomotion, the answer is `anim_meshy.res`.

**Its clips are named for gameplay slots, not for Meshy actions** — `idle`, `run`, `attack1`,
`parry` — so `AnimationClips.Resolve` matches them exactly instead of guessing through an alias
table. `AnimationClips.SharedSlots` is the required set and `--validate` fails a build that loses
one, because a missing clip is otherwise completely silent: `Resolve` returns empty, every caller
reads that as "this body has no such animation", and the actor stands in its bind pose.

**Regenerating it costs no credits and no character.** The clips come from `meshy_animate` against a
**rig task that already exists** (`reports/3d/archive/meshy-migration/manifest.csv` carries 28 of
them), so nothing is regenerated and no rig is re-paid for. `tools/strip_anim_glb.py` then throws the
body away — Meshy returns a whole skinned character per clip, and a 24-clip set is 190 MB of the same
townsman in 24 poses; stripped, it is 1.4 MB.

`AnimationClips.Resolve` offers a model's **own** clips first and lets the library answer only what
it alone can. It strips armature prefixes and a leading `Female_`/`Male_`, and recognises
`HitReact` and `Idle_HitReact` alongside `HitRecieve`.

### The library build

⚠️ **The clips are not stored as they arrive** (the 2026-10 camera and animation pass).
`tools/build_meshy_anim_library.gd` does four things to them, each a visible defect before it, and
prints what it did per clip, so the build log is the check.

1. **Root travel is removed.** Meshy bakes the distance a clip covers into the Hips position
   track: the sprint carried the hips 3.18 m forward per loop and snapped them back. The game
   moves the capsule itself, so that travel played on top of the real movement and the body ran
   out from under the camera every stride. Hips X/Z is detrended linearly, so each clip ends where
   it starts, for `sprint`, `walk_back`, `strafe_left`, `walk`, `run`, `combat_walk_fwd`,
   `combat_walk_back`, `dodge`, `hit`, `jump`, `attack3` and `heavy_overhead`. Y is left alone (a
   jump still rises), and so are `death`, `knockdown` and `getup`, whose travel is the animation.
   A one-shot does not travel at a steady rate, so a linear detrend leaves some mid-clip excursion
   there; the log prints it.
2. **The idle is calmed and turned to face forward.** The source idle is a look-around whose hips
   rest about 40 degrees off forward and sweep through 90, so a standing character seemed to face
   the camera. The `idle` slot is the original with every key pulled to 15% of its motion about
   the clip's mean pose (the Hips position too, so the feet stay under the body), then turned so
   the mean hips yaw is zero. The untouched original is kept as **`idle_alert`**.
3. **The strafe's chest is squared to the front.** The source is a sideways walk with a weapon
   held across the body and the torso turned 28 degrees toward its travel. The upper body is turned
   back on the hips (half at the Spine, the rest at the Chest) until the chest's mean facing is
   forward, and the neck so the head's is too. The Hips track is untouched: the legs still step
   exactly sideways and the crouch is as deep as the source's.
4. **`strafe_right` is `strafe_left` mirrored**, after the squaring (left and right tracks
   swapped, quaternions and positions reflected). Meshy shipped only the one.

It fails if any track is compressed, and it writes `anim_meshy.res` and nothing else. Re-run it
whenever the sources or their `.import` retarget settings change. `AnimationClips.SharedSlots` now
requires `strafe_left` and `strafe_right`, so `--validate` fails on a library built before this.

⚠️ **A facing or sliding fault is a clip fault first.** "The character faces the camera in third
person" was reported as a camera bug and was the idle clip, the baked root travel, blend points at
speeds nobody moves at, and a body mesh yawed half a turn by a load (`ARCHITECTURE.md` §2.4).
Nothing was wrong with the camera, and none of it shows in a clip list.

### The gate

```powershell
godot --headless --path . --script res://tools/meshy_rig_probe.gd -- --asset res://path.glb
godot --headless --path . --script res://tools/anim_library_probe.gd
godot --headless --path . --script res://tools/equipment_socket_probe.gd
godot --headless --path . --script res://tools/locomotion_tree_probe.gd
godot --headless --path . --script res://tools/facing_probe.gd
godot --headless --path . --script res://tools/camera_probe.gd
```

**This is a gate, not a spot check**, and `assets.py validate` runs it over every humanoid. It
proves the skeleton is named `GeneralSkeleton` and carries all 22 required profile bones.

`anim_library_probe.gd` also holds the library to its build, on the real body: every locomotion
loop ends within 2 cm of where it starts and never strays more than 0.3 m, the idle's mean hips
yaw is within 8 degrees of forward and turns less than 20, and `strafe_right` puts each hand and
foot within 5 cm of the reflection of `strafe_left`'s. `facing_probe.gd` (gate `facing` in
`world_quality_check.py`) builds the body the way `PlayerFactory` does and checks, at idle, walk,
jog, sprint, both strafes and a backpedal, that the chest faces within 20 degrees of the body on
average over a stride and the hips stay within 0.3 m of the capsule. `camera_probe.gd` holds the
first-person eye and its cut-outs to account (below). ⚠️ These probes build a body with no
`MountComponent` and no save, so they cannot see a fault that a load introduces; `--camshots` is
the check for that.

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

### NPC bodies, and no cosmetic layer

⚠️ **There are no bolt-on kits any more** (2026-10-06). `EnemyVisualKit`, `NpcVisualKit`,
`enemy_identity_kit.glb`, `npc_kit_embervale.glb` and the player's pauldrons and utility pouch were
deleted: the boar's boxes, the crowns and masks, the 70 NPC outfit profiles. A body's identity is now
its own mesh. An NPC's look comes from the body assigned to its scene node (`Model` instances
`res://assets/models/characters/npc_*.glb`; there is no data table), so **two NPCs differ by body,
and body reuse is visible**. Six generated bodies were added to break it up and assigned by role
(`tools/repoint_models.py`, 28 nodes in ten scenes): `npc_dawnwarden` (guards, marshals, wardens),
`npc_clansman` (Frostfang clansfolk, the chief), `npc_elder` (village elders, the clockkeeper),
`npc_scholar_f` (Veiled Archive keepers), `npc_smith` (smiths, trainers, labourers) and `npc_ranger_f`
(Ash Hunters). Women stay on women's bodies: the female-only scholar and ranger were not given to men.
What is still worn is `eqp_armor_leather`, `eqp_armor_mail` and `eqp_shield_round`, through the
sockets below. The six bodies came from `meshy_text_to_3d` with T-pose spelled out strictly, then the
Meshy rig (20 credits each, [recipe](RECIPES.md)).

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

A piece may name its authored bone as the *preferred* one; the socket's candidate list is only the
fallback for a rig that lacks that exact bone. Quadruped rigs carry both a `Spine` and a `Torso`, and
resolving those purely through the humanoid preference order would walk a piece up the animal's back.

⚠️ **A `BoneAttachment3D` OVERWRITES ITS OWN TRANSFORM with the bone pose, at bind and on every skeleton
update. Put the authored transform on the CHILD, never on the mount** (2026-10-06, from the engine
source and the commit history, not from a frame). Every grip rotation and weapon scale in the game had
been written onto the mount and discarded: the original `PlayerFactory.AttachWeaponVisual` did it.
`EquipmentPresentationComponent` now leaves the mount untransformed and the piece carries
`WeaponGrip.Local(offset, rotationDegrees, scale)`, the same TRS a `Node3D` would have. Shields and
helms pass none and get identity. If a weapon sits wrong, tune `WeaponGrip.Hand`; never move the
transform back onto the attachment. ⚠️ Every held weapon changed orientation the day this landed, on a
grip basis nobody has seen applied.

`WeaponGrip.Hand` holds the one grip correction, derived from the basis that used to live privately
inside `PlayerFactory` — which is why every companion, NPC and enemy that carried a weapon carried it
unrotated.

Two gates keep it honest: `EquipmentSocketTests` pins the alias table without an engine, and
`tools/equipment_socket_probe.gd` proves it against **every humanoid rig on disk** (manifest-driven, so the six new bodies are included) plus one real
attachment that has to end up on the hand bone. A bone-name miss used to be completely silent — the
player's visual sword was `QueueFree`d on every spawn for an entire phase — and it now warns.

Human bodies were once given JSON material corrections only (geometry, skins, inverse binds and
animations never re-exported); `tools/patch_human_materials.py` is that legacy tool. It is called by
nothing and its hard-coded body list does not include the six 2026-10 bodies.

---

## QUADRUPED

Beasts, mounts and dragons. 15 of them (`assets.py status` has the live count), and **they do not go through the humanoid system.**

No bone map, no retarget, no shared animation library — by design. A quadruped keeps its own rig
and its own clips, and `AnimationClips` carries the aliases that map gameplay slots onto their
vocabulary (`Bite_Front`, `Flying_Idle`, `Jog_Fwd`, `gallop`). `HumanoidBones.FindHand` returns
empty for them, which is correct: a wolf has no hand.

**Do not try to force a quadruped onto `SkeletonProfileHumanoid`.** It was attempted and closed as
not migratable. The old identity kit that dressed the shared animal meshes is gone (2026-10-06): each
beast and dragon is now a generated mesh with its own look. An archetype sharing one mesh can still
set `EnemyArchetypeResource.BodyTint` (a flat colour over every surface), but **no archetype uses it
now** and it would paint over a generated texture.

### Generated beasts and dragons (2026-10-06)

Meshy text-to-3D makes the mesh and texture (15 credits, no Meshy rig); `tools/rebind_creature.py`
(headless Blender 5.1, one at a time) puts it on a skeleton and renders a contact sheet so the result
is judged without Godot.

- **Dragons** (`enm_ancient_dragon` 22 m, `enm_ash_dragon` 14 m, `enm_wild_dragon` 11 m,
  `enm_frost_drake` 5 m) are real **four-legged winged** dragons at real size. `--mode dragon` fits a
  **new 20-bone armature** to the mesh from per-asset landmarks in `tools/rebind_creature_landmarks.json`
  (the source file's glTF axes, left side only): `Root`, `Torso`, `Neck`, `Head`, `Body1..4` (tail),
  `Wing1..4.L/R`, four single leg bones. Weights are distances smoothed over the mesh graph (no bone-heat
  solve); the **eight clips are authored procedurally under the old names** (`Flying_Idle`,
  `Fast_Flying`, `Punch`, `Headbutt`, `HitReact`, `Death`, `Yes`, `No`), so `AnimationClips` and the
  `Head` breath anchor resolve unchanged. Fallback rungs: A smooth weights, B rigid segments, C a
  six-bone rig; the old Quaternius dragon is never a fallback. The old rig was an upright hovering body
  and no scale maps it onto a horizontal dragon.
- **Beasts** (`enm_wolf`, `enm_thornback_boar`, `enm_ashfall_elk`) keep their existing rigs and clips;
  `--mode beast` keeps the rigged `.glb` byte for byte, warps the new mesh to the rig (body fit, then each
  leg column until the paw sits on the old foot) and weights from the nearest old vertex. A static
  `.glb` can **not** be rescaled without its skeleton, so size lives on the scene root.
  `enm_dire_wolf.glb` and `enm_frost_stalker.glb` are the wolf recoloured by `meshy_prep_static.py`
  (`--tint`, `--lighten`, `--desaturate`, `--root-scale`) on the wolf's 51-bone rig and 24 clip names.
  **Size is on the file's root node, not `ModelScale`**, because two Ashen scenes instance the dire wolf
  file directly (`tools/audit_3d.py` `RECOLOURED_COPIES` declares the family so duplicate geometry is
  info, not critical). Antlers are part of the elk mesh, so every elk and the 0.55 calf carry them.
- **Data that must follow a new body** (a model swap inherits no collision): capsule, hit zones, hover.
  Dragon capsules are 2.4x5.9, 2.1x5.0, 1.7x4.25 and 1.0x2.5; hover and takeoff 24, 20 and 18 m with
  `ClimbSpeed` scaled to keep climb seconds. Three append-only fields were added.
  `HitZoneResource.RotationDegrees` turns a zone capsule off the vertical (X -90 lays it along the
  body's forward axis, Z 90 across it), so a tail, neck and wings each get a zone that lies along them;
  each boss dragon has six zones (`head`, `neck`, `torso`, `wings`, `body`, `tail`) and `body` is the
  capsule along legs and belly, the only part a sword reaches. `EnemyArchetypeResource.CastOrigin`
  is where spells and breath leave the body from its feet (zero keeps the chest point); the four dragons
  set it to the measured snout, because the capsule no longer says where the head is.
  `python tools/check_hit_zones.py [--check]` reports how much of each part's rest-pose vertices sit in
  a zone (Ancient 77%, Ash 79%, Wild 94%).
- **Bite and wing arcs** in `DragonMeleeComponent.BuildArcs` now reach the ground at any capsule height
  (the old box sat above the 1.8 m player hurtbox, so no frontal bite could land): a difficulty change
  to confirm in a fight. Dragons still hover when "grounded" and their legs tuck, they do not walk.
- **Look versus reach for scaled bosses.** Reach (melee hitbox and its offset, telegraph ring, nav
  agent) derives from `CapsuleHeight / 1.8`; `ModelScale` scales the picture only. A boss meant to tower
  without out-reaching its fight is authored `ModelScale` x f with `CapsuleHeight` and `CapsuleRadius`
  x sqrt(f), no `AttackRange` change, and `VisualHeight` set to the height the model really stands at.
  The whole-body hurtbox, floating plate, status marks, cast origin, spell body fit and lock-on
  framing read `VisualHeight` (via `BodyMetrics`); collision, nav and reach read the capsule. The ten
  scaled bodies (Morthul 1.58, Beast Lord 1.5, Storm Tyrant 1.6, Stone Sentinel 1.58, Ward Golem 1.62,
  Hollow Queen 1.39, Iron King 1.23, Ashen Knight 1.43, Crimson Prophet 1.25, Grimtusk 1.7) have never
  been fought. `ContentValidator` rejects `ModelScale <= 0` and a `VisualHeight` below the capsule.
- **Held weapons are archetype data:** `EnemyArchetypeResource.HeldWeaponPath` and `HeldWeaponScale`
  hang a model on the hand socket (visual only; the blow still comes from `WeaponPath`).
  `HeldWeaponScale` multiplies on top of `ModelScale`. The Iron King holds `wpn_mace_iron` at 1.5 and
  the clan shaman `wpn_staff_oak`.
- **Not yet true:** an airborne breath cannot reach the ground (cones 12, 11 and 14 m from a mouth
  23-33 m up), and the Wild dragon lost a hit it used to land. Decide: longer cones, lower hover, or a
  cosmetic air phase.

**A mount is a state of the rider, not a second body.** `MountComponent` parents the horse GLB
under the player body as `MountVisual`, rotates it π for the glTF `+Z` → Godot `-Z` convention, and
drives its own `AnimationPlayer` for `idle`/`run`/`gallop` while suppressing the rider's run loop.
`SaddleHeight` and `SaddleForward` are hand-measured against the imported model and are **not
derivable from the file** — if you replace the horse, re-measure them.

---

## STATIC PROP

Furniture, containers, generated world models, everything a scene places and nothing animates. The
largest family (`assets.py status` has the count) and the simplest.

- Adopt as a **container change only** — the buffer is copied byte for byte. `--kit` handles this,
  and `--kit` now takes a **`.glb` as well as a `.gltf`**. ⚠️ It used to take only a `.gltf`, which
  meant a static `.glb` had **no adoption path at all** — and every vendored bundle except the four
  MegaKits ships `.glb`. Both routes failed unhelpfully: `--kit` `json.load`ed the binary and died on
  a `UnicodeDecodeError`, the default Meshy route walked `doc["skins"]` and died on a `KeyError`, and
  `assets.py` discarded the child's stderr so the command printed nothing at all. All three are
  fixed; **`--kit` means "static model", not "MegaKit `.gltf`".**
- ⚠️ **THE `rpg_items` PACK IS AUTHORED AT DISPLAY SCALE, NOT IN METRES, AND NOTHING IN THE FILES
  SAYS SO.** Adopted raw it gives a gold coin **0.74 m across** and a dagger **1.39 m long**. This is
  the `rts` trap in the other direction, and it is not visible from a filename, an import log or an
  audit finding — only from a render with the 1.8 m reference in it. Measure, then pass
  `--root-scale`. The shipped corrections run 0.04 (coin) to 0.56 (leather vest).
- Import as `StaticBody3D`-ready with **author-time collision** (`-col`/`-convcol` name suffixes),
  never runtime-parsed visual-mesh collision.
- Scale corrections go in the `.import` as `nodes/root_scale`, never in one cell's node transform —
  the `.import` reaches every placement. ⚠️ The `rts` pack is roughly **1/6 scale** and nothing in
  the files says so. Measure any candidate against a 1.8 m reference.
- **Shared textures stay shared (kept kit).** Wall modules and the remaining Quaternius props resolve to
  one shared texture each. `assets.py validate` checks this both statically and in the engine, because
  embedding them per-model is how twelve wall modules once cost 204 MB of the same textures twelve times
  over. The opposite holds for generated world models: each owns one atlas ([below](#outdoor-world-generated)),
  and **two world models must not share one** (`audit_3d.py` fails duplicate world textures; a
  recoloured copy re-encodes its own image).

⚠️ **There are no ground textures and there must not be.** The terrain is six painted noise layers
from `data/terrain_layers/`. A CC0 PBR ground pack is the reflex here and it would make the terrain
the only photographed thing in a hand-painted world.

---

## OUTDOOR WORLD (generated)

Since 2026-10-06 the outdoor world is Meshy text-to-3D in `assets/models/world/` (`prp_tree_*`,
`prp_pine_*`, `prp_rock_*`, `prp_ice_*`, `prp_lm_*` landmarks, and the props `prp_tent_a`,
`prp_lamp_post_a`, `prp_fence_a`, `prp_campfire_a`, `prp_clutter_pile_a`, `prp_cart_a`, `prp_well_a`,
`prp_banner_a`), plus two **procedural** meshes in `assets/models/props/` from `tools/gen_ground_cover.py`
(pure Python, no Meshy, no Blender): `prp_grass_clump_a` (126 triangles, 0.70 m) and `prp_fern_clump_a`
(216 triangles, 0.80 m). **Variety rule (owner): two or three models per type**; variety comes from the
scatter transform and tint, so there are three trees (`oak_a`, `dead_a`, `fir_a`), four rocks
(`boulder_a`, `rubble_a`, `crag_a`, `outcrop_a`), two ice pieces and one of each prop. The elder tree is
`oak_a` at x2.4-2.7, the blasted spires `crag_a` at x5-6 with a dark tint, ice chunks `rubble_a` with an
ice tint. **Look:** stylised-realistic, muted and matte (`docs/ART_STYLE.md` §1). **Landmarks are solid,
walk-around set pieces: nothing generated is roofed or enterable.** The authoring recipes are in
[`RECIPES.md`](RECIPES.md): *a new generated world model* and *a new landmark*.

**The pipeline** (every tool resumes, nothing is paid twice):

| Step | Tool | Does |
| --- | --- | --- |
| Generate | `tools/meshy_batch.py PLAN.json OUT --cap 1300` | preview (5), refine (10), optional rig (5), `meshy-5`; `state.json` records each task id the instant it exists; stops at the credit cap; per-item `pbr` flag |
| Prep | `tools/meshy_prep_static.py` | one mesh, uniform scale to `--height`, base-centre origin baked into the vertices, emission and specular dropped, metallic 0 and roughness set, PNG re-encoded at the class cap and named by role; `--roll --widen --squash-above --tint --lighten --root-scale` for weapons and recolours |
| Adopt | `python tools/assets.py adopt-batch --plan P --source-dir D [--import]` | prep and write a tier, one import, the texture budget once, a second import, one manifest write; refuses an existing model without `--replace` |
| Wrap | `tools/make_landmark_wrapper.py` | `scenes/props/lm_<id>.tscn` (below) |
| Place | `tools/repoint_models.py` | per node, never per file path; dry-run diff; `delete` refuses while a referrer remains |
| See it | `tools/asset_stage_shots.gd` | one asset at 3, 10, 30 and 100 m, front and back, with a 1.8 m reference, at the game's 75 degree FOV |

**Prompts are short.** Subject and shape in a sentence or two, then `Single isolated object, no ground,
no base, game asset.` The texture prompt names colours and carries `muted ash-grey and earth-brown
palette, weathered, matte, flat even overcast lighting, no baked shadows`, because `meshy-5` bakes
lighting into the albedo and has no `remove_lighting`. Ask for chunky leaf clumps with gaps and thick
geometry: nothing hair-thin (the bow is unstrung, the lamp is a thick post with a block lantern, the tent
has no guy ropes), nothing hollow, no stooped pose on a rigged body. Cost: 15 credits for a static model
or a beast or dragon mesh, 20 for a rigged NPC body. `target_polycount` is set at preview; there is no
remesh.

**Textures.** Scatter models (trees, rocks, ice spire, small props) are refined with `pbr: false`, so one
base-colour atlas, which is what the scatter shader needs for wind, wetness, snow and `Tint`. Landmarks
are `pbr: true` and carry BaseColor, Normal and ORM. `tools/audit_3d.py` classes: `world_hero` (iron
citadel, colossus, god hall) 2048 / 1024 / 1024; `world_landmark` (`prp_lm_*`, trees, outcrop, ice wall)
1024 / 1024 / 512; other world props 512. Dragons are in the `player_boss` class at 2048 whatever the file
is called.

**Wrappers.** A cell instances `scenes/props/lm_<id>.tscn`, not the bare `.glb`: a `Node3D` carrying
`assets/shaders/world/landmark_detail.gd`, the `Model`, and a `StaticBody3D` whose shapes are **primitive
boxes and cylinders only** (never a trimesh, never `-convcol`), written from the `SHAPES` table in
normalised bounds coordinates so a re-rolled model keeps its fit (`--slices ID` prints the numbers). The
script builds one shared material per source material and setting from the imported albedo, normal and
ORM, so sixty monoliths are one material. It exposes `albedo_tint`, `detail_scale` (0.8 m) and
`detail_strength`; the shader adds a world-space triplanar grain overlay that fades out by about 25 m,
because one atlas over a 20-36 m piece is only 20-40 texels per metre at the base. A scripted root also
keeps the wrapper out of `WorldArchitectureBatcher`, so rock and ice wrappers are one draw each. The small
props (fence, lamp post, tent, banner, campfire, cart, well, clutter pile) are placed as bare `.glb` files
with their existing colliders.

**Landmark scale and draw.** Placed heights run monolith 8-18 m, gate tower 14-20 m, bell tower 16-20 m,
ruin tower 17-34 m, arch 20 m, god hall 21-45 m, colossus 29-36 m, elder oak 29-32 m, iron citadel 21 m.
Only placements of about 20 m and up join the `world_landmark` group, which is **always drawn to the
Backdrop radius** (about 45 nodes); monoliths and columns use the normal visibility tier. Giants sit on
flat ground and are sunk 0.3-2.5 m rather than padded, so terrain and the lattice are untouched. None of
the new monuments has a scatter exclusion yet (open item in `docs/NOW.md`).

**Scatter.** Every layer in the six realm specs names one of nine meshes. Trees scatter at 0.7-1.5 of
their 9-14 m, with low-count `Layer_elder` (1.8-2.4) and `Layer_tor` (2.5-4.0) layers that have HLOD and
**no collision** (a player walks through an elder, tor or crag). Each profile's triangle load is at or
below what it replaced; real counts are dead tree 2,851, fir 3,010, rubble 1,350, crag 804, ice spire
518. Gravel, pebble, flower, clover and mushroom layers are deleted. Wind follows the scene path: a path
containing `tree`, `pine`, `grass`, `bush`, `flower` or `fern` sways, a rock never does. `HlodColor` must
equal `Tint` or the far tier pops in colour.

**Kept on purpose (Quaternius):** the 36 `mod_*` modules, the five enterable buildings and 19 composed
`bld_*` buildings (including `bld_ruin_house`; `bld_ruin_tower` stays on disk but is no longer
instanced), 22 interior props, `prp_tome_stand`, `prp_relic` (the town hub Relic), the three crafting
stations, `prp_bench`, `prp_cauldron`, `prp_timber_stack`, the cache chest, the training dummy, the dock,
jetty, fishing hut, gazebo and mine head, and the housing decor named by file name in
`src/Housing/PlaceableTemplates.cs` (`prp_ruin_pillar`, `prp_brazier`, `prp_banner_guild`, `prp_crate`):
those files stay on disk so a saved decor piece never falls back to a box. **Retired** Quaternius outdoor
files (`prp_tree_broadleaf`, `prp_pine_dead`, `prp_boulder*`, `prp_rock_*`, `prp_glacier*`, the cliff
faces, `prp_pebble_*`) go only through `repoint_models.py delete`, which refuses while any scene, spec,
C# file or tool names them; `assets.py status` lists what is left.

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

## FIRST PERSON

**There is no viewmodel, and that is the contract.** First person is TRUE first person: the camera
sits at the player body's own head and the body stays visible. You see its arms, its weapon and
its equipment because they are the same arms, weapon and equipment the world sees.

What this replaced (2026-09-04) was `fp_arm_left.glb` / `fp_arm_right.glb` — two rigless meshes whose
every motion was C# arithmetic in `FirstPersonArmsComponent`: walk bob, a slash arc alternating by
combo index, a guard blend, cast and interaction beats. It meant a second skeleton, a second action
state and a second weapon to keep in step with the first, plus a fake second FOV that rescaled the
arms by a half-angle-tangent ratio. All of it is deleted, along with the VIEWMODEL rig family.

**The head is cut out by the body shader, and only there** (the 2026-10 camera pass; before it
the eye sat 0.14 m in front of the head bone and relied on the near plane, which let the skull
into frame at a sprint and went black looking down). `player_body.gdshader` takes two per-instance
spheres and discards what is inside either, in every pass but the shadow pass, so the head still
casts its shadow:

| Uniform | Sphere | Sized by |
| --- | --- | --- |
| `fp_head` | round the animated head: 0.22 m, centred 5 cm above and 3 cm ahead of the head bone | `CameraRigMath.HeadSphereRadius`, `HeadSphereCentre` |
| `fp_eye` | round the camera itself, as wide as its near plane's corners | `CameraRigMath.EyeSphereRadius` from near plane, FOV and aspect: 0.12 to 0.20 m, narrowing to 0.12 m at full look-down so it cannot open the chest |

Radius 0, the default, is off. They are instance uniforms because `CorruptionAppearanceController`
duplicates the material, and because the creator's preview shares the mesh and must not be cut.
`PlayerAppearance.SetHeadCutout` and `SetEyeCutout` are the only writers. Anything hung on a head
socket (a helm, hair) has its own material, so the rig sets it shadows-only while the head is
hidden; a helm equipped while already in first person can stay visible until the next rescan. If
a future body's head survives the sphere, change the radius or the centre, not the mechanism.

⚠️ **The eye is anchored to where the head RESTS and takes a fraction of where it goes.** It
ignores the head bone's rotation entirely: taking it would hand the player every head turn in
every clip as an involuntary camera movement, which is the fastest way to make a first-person game
unplayable. Of the animated head's travel from rest it takes 30% vertically and 10% horizontally
(none under Reduced Motion), damped, and the result is clamped to 0.45 m from the fixed pivot, so
a knockdown or a death throws the head without throwing the camera. A neck pivot turns the eye
with pitch, and looking down carries it up to 0.24 m forward so it clears the chest and there is
still a body to look down at. The numbers are `CameraRigMath` constants measured on
`chr_player_base.glb`; a player body with different proportions needs them measured again, and
`camera_probe.gd` and `--camshots` both print what they measure.

**The casting arm is the body's own arm.** `FirstPersonArmComponent` swings it at the shoulder
with a `SkeletonModifier3D` while a spell is wound up, charged or channelled, so the hand is in
frame. It is one bone's pose rewritten after the animation, not a second set of arms.

**The player's atlas is drawn as emission.** Every Meshy body ships one material with the atlas
as both base colour and emissive texture and no metallic factor, which glTF reads as metallic 1:
the stock material is drawn by its emission. `player_body.gdshader` copied the metallic and had
dropped the emission, which left the player as bare metal, a near-black silhouette beside the
NPCs. It now has `emission_amount`, copied from the source material by `PlayerAppearance` and
applied to the recoloured atlas, so tints, ash and the skin wash all show. A new body shader for
a Meshy character has to carry the emission the same way.

**Weapons are shared between views.** One `wpn_*` model, one hand socket, one grip correction
(`WeaponGrip.Hand`). Build a separate hero version only when the world mesh demonstrably fails in
gameplay framing — and note that "demonstrably" now means a render, because both views show the same
mesh.

**The coordinate contract is unchanged and still applies:**

- 1 Blender unit = 1 metre, exported at scale `1.0`, transforms applied. The imported Godot root
  must remain identity scale.
- The functional long axis is local **+Y** — for a sword, grip to point. **+Z** is the face, **+X**
  the wielder's right.
- The origin sits **on the centreline of the grip**, at the point the hand should own. Not the mesh
  centroid, not the point.
- Reference size, the iron sword (regenerated 2026-10-06): `0.209 × 0.960 × 0.104 m`, Y from -0.09 to
  +0.87, wrapped grip centre at local `Y ≈ 0.035 m`, guard at 0.12-0.15, blade 49 mm wide (the old sword's
  was about 104 mm, so it is a much slimmer sword). Written by `meshy_prep_static.py --roll 180 --height
  0.96 --widen 1.25 --offset 0,-0.09,0 --roughness 0.6` (the Meshy source is point-down). One-handed
  grips should be 28–36 mm in diameter; **this one is 26-33 mm by 20-24 mm, a contract gap** that has
  not been looked at in a hand. The dagger is cut from it: 0.7x about the hand point, then the blade
  alone compressed to 0.45 m total, so the hilt stays hand-sized. Its grip is only 18-23 mm by 14-16 mm,
  about half the contract, so the hand may swallow it; `git checkout 2815e335 -- assets/models/weapons/wpn_dagger_iron.glb`
  restores the old curved knife. Both are non-metal (like the other generated weapons), so the blade
  reads painted rather than polished.

⚠️ **Do not add a second compensating transform inside a weapon GLB.** The correction belongs at the
socket, in `WeaponGrip`, where one value serves every wielder in the game.

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

⚠️ **`repair_architecture_materials.py` SWEEPS `props/`, `weapons/` AND `equipment/` — RUN IT AFTER
EVERY ADOPTION, NOT ONLY AFTER A BLENDER BUILD.** It swept props alone until 2026-09-06, so the
Hunting Bow, the arrows and the round shield all shipped at the pack's **0.4 metallic on their wood
and their cloth**, which is exactly the defect the tool exists for, one folder over. And `"Steel"`
does not contain the substring `"metal"`: a blade reaching `response()` fell through every metal
branch to the non-metal default and came out **matte**. There is now a `steel|iron|brass|bronze`
branch at 0.86 / 0.40, matching the hand-authored `wpn_sword_iron`.

⚠️ **TWO FILES ARE SKIPPED BY THAT SWEEP AND MUST STAY SKIPPED** — `wpn_sword_iron` and
`wpn_dagger_iron`. Each is one textured atlas written by `meshy_prep_static.py` (metallic 0, roughness
0.6: the steel is painted) and its material is named after the file, so `response()` would read the
`iron` in the name and turn the whole atlas, leather grip included, into 0.86 metal. (The other
art-directed build outputs the sweep once skipped, the two kit models and the pauldron and pouch, are
deleted.)

**A colour cast baked into a TEXTURE is not a material defect and the material lever cannot reach
it.** `npc_hooded`'s green-teal cloak was one: the body has a single material with a base-colour
texture and a white `baseColorFactor`, so the only factor available multiplies the face and the
leather too. `python tools/neutralize_palette_cast.py <asset.glb>` is the repeatable script for that
case — it rewrites the embedded PNG, correcting only pixels that are *already* nearly grey and grey
in the wrong direction, never touching value. It repacks the buffer and verifies every `bufferView`
survived, because the image is not the last region and a naive resize would move the geometry out
from under every accessor.

### Texture weight

Every texture a model uses is a `.png` beside it, and what it costs in video memory is decided by
that png's `.import`, not by the model. A Meshy body's atlas is extracted to
`<model>_texture_0.png` (`gltf/embedded_image_handling=1`), so its cap goes on that file.

```powershell
python tools/assets.py audit-weight           # MB per class, and everything off its budget
python tools/assets.py audit-weight --check   # the same, exit 1 when anything is off budget
python tools/assets.py audit-weight --fix     # write the budget, then run a Godot import pass
python tools/assets.py audit-weight -v        # every texture, heaviest first
```

`--check` and the plain report read `.import` text and never start the engine; `--fix` edits
the `.import` files and the import pass that follows is what makes the caps real. A changed model
texture is also a shared world-bake input, so a `--fix` is followed by the master bake.

The budget is `data/rendering/VisualContract.json`'s three caps by class and map role, written as
`process/size_limit` (longest edge). The table lives in `tools/audit_3d.py` (`TEXTURE_BUDGET`):

| Class | Which textures | BaseColor | Normal | ORM / Roughness |
| --- | --- | ---: | ---: | ---: |
| `player_boss` | `chr_player*`, `boss_*` and the four dragons | 2048 | 2048 | 1024 |
| `character` | every other body in `characters/` and `creatures/` | 1024 | 1024 | 512 |
| `architecture_trim` | the BaseColor/Normal/ORM sets in `architecture/` | 1024 | 1024 | 512 |
| `prop_trim` | `T_Trim_*` | 1024 | 512 | 512 |
| `nature_hero` | legacy `T_Nature_*` leaves, bark and rock faces (go with the retired models) | 1024 | 512 | 512 |
| `world_hero` | `prp_lm_iron_citadel`, `prp_lm_colossus*`, `prp_lm_godhall*` | 2048 | 1024 | 1024 |
| `world_landmark` | the rest of `assets/models/world/`: `prp_lm_*`, `prp_tree_*`, `prp_pine_*`, `prp_rock_outcrop`, `prp_ice_wall` | 1024 | 1024 | 512 |
| `prop` | everything else, including the small generated world props | 512 | 512 | 512 |

A trim sheet tiles across a whole building and is read at arm's length, so it is a hero surface
and not a prop. Every budgeted texture is also `compress/mode=2`, `mipmaps/generate=true` and
`detect_3d/compress_to=0`, and a `*_Normal` map carries `compress/normal_map=1` (RGTC). Nothing
needs setting for the other two choices: the importer drops an all-opaque alpha channel by itself
(BC1 rather than BC3), and whether a map is read as sRGB is the material's decision, not the file's.

⚠️ **A texture nobody opened in the editor stays lossless, and lossless is eight times the size.**
The flip to VRAM compression happens on first 3D use in an editor with a real renderer; a headless
import never triggers it. Six bosses, a whole brick set and every nature texture shipped as
uncompressed RGBA8 for that reason, and the texture set estimated at 266 MB came to 56 MB once the
budget was written. `adopt` now writes it after the first import and imports again.

⚠️ **Three data textures are lossless and unmipped on purpose**: `T_Prop_Colormap`,
`T_Nature_Grass` and `chr_player_base_mask`. A palette is swatches and gradient strips sampled at
a point, so block compression bands it and a mip level averages a swatch with its neighbour; the
mask is read with `filter_linear` in `player_body.gdshader`, which never samples a mip, and a
blurred mask smears hair over clothing. They are pinned with
`detect_3d/compress_to=0` so the editor cannot flip them.

The figure is an estimate of what the importer produces (BC1 half a byte per pixel, BC3 and RGTC
one, uncompressed four, plus a third for mips), not a measurement, and a lower cap is not visual
validation: look at the surface at eye level after the reimport.

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
python tools/assets.py build primitive-creatures | environment | player-weapons | anim-library
```

`primitive-creatures` (`build_enemy_identity_assets.py`) now builds only the four primitive creatures and
holds `build_replacement`, the rigid-skin-on-a-3-bone-rig path; the kit halves of that script and of
`build_player_weapon_assets.py` are gone. `environment` (`build_environment_assets.py`) writes the retired
Quaternius-derived nature models and is to be retired with them.

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
