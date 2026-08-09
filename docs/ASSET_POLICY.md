# Asset Acquisition Policy — 3D models

> **Authority.** This document governs **every 3D asset** that enters the project. It is
> mandatory, and it **supersedes** the prior build-from-scratch defaults in `CLAUDE.md` §1
> and `ART_STYLE.md` §6.3 wherever they disagree. Set as a standing maintainer policy after
> Phase 35; recorded here because a policy that lives only in a chat session governs nothing.
>
> **What changed.** Through Phase 30 every model in `assets/models/` was authored from
> scratch in Blender via the MCP. That is no longer the default. **Search first, adapt
> second, create last.**

---

## 0. Standing direction — the art set is Quaternius (2026-08-05)

> **Maintainer instruction, and it overrides §1–§4 for anything a vendored pack already covers.**
> Embervale's art set **standardises on Quaternius CC0 packs**. The point is coherence: one artist,
> one style, one skeleton. A slightly better model from elsewhere is the *wrong* answer.

### §0.1 The four packs, and the order to reach for them (2026-08-08)

**Four Quaternius MegaKits are the art set.** `CLAUDE.md` §1 carries the short version; this is the
detail. The near-entirety of the game is to be built from these, and the search order is fixed:
**the four packs → the other vendored bundles → the open web → Blender MCP.** Stop at the first that
works.

| Bundle | Covers | Models |
| --- | --- | --- |
| `medieval_megakit/` | modular architecture: walls, roofs, doors, windows, shutters, floors, stairs, balconies, overhangs, chimneys | 176 |
| `medieval_interiors/` | interiors and props: beds, cabinets, bookcases, shelves, tables, chairs, chests, anvil, workbench, market stalls | 94 |
| `nature_megakit/` | trees (common/pine/dead/twisted), bushes, ferns, grass, clover, flowers, mushrooms, pebbles, rocks, rock paths | 68 |
| `animations/` | `AnimationLibrary_Godot_Standard.glb` — 46 clips on one shared skeleton | 1 |

⚠️ **What the four packs do NOT cover: characters, creatures and weapons.** Those come from the
older vendored bundles (`men/`, `women/`, `monsters/`, `animals/`, `rpg_items/`), which is why step 2
exists and is not optional.

### §0.2 The animation library — retargeting, proved (2026-08-08)

The library is a **Rigify-style** 53-bone rig (`root`, `DEF-hips`, `DEF-spine.001`, `DEF-shoulder.L`);
every adopted Quaternius body is a **62-bone** rig (`Root`, `Body`, `Hips`, `Abdomen`, `Torso`,
`Chest`). **They share zero bone names**, so the library animates nothing as it ships.

**Retargeting works, was proved end to end on `npc_merchant_m`, and needs no Blender round-trip.**
Both sides are mapped onto `SkeletonProfileHumanoid` by a `BoneMap` referenced from the `.import`
(`assets/models/animations/bonemap_rigify.tres`, `bonemap_quaternius.tres`); the importer's bone
renamer rewrites each file's own tracks and unifies both skeletons as `%GeneralSkeleton`. 53 of the
profile's 56 bones map on each side. The clips reach a character as a shared `AnimationLibrary`
added at runtime by `CharacterAnimationComponent`, gated on the skeleton being named
`GeneralSkeleton` — which is the retarget's own marker.

⚠️ **What the library actually buys is three slots, not animation.** Every adopted body already
ships 24 clips, and `idle`, `run`, `attack`, `hit` and `death` already resolved before any of this.
Only **`block`, `cast` and `channel`** were empty. Do not plan work on the assumption that
characters are unanimated.

**Four traps, each of which produced a wrong build before it was written down:**

1. **The importer strips a trailing `_Loop`** and sets the clip's loop mode instead, so the pack's
   `Idle_Loop` arrives as `Idle` and `Jog_Fwd_Loop` as `Jog_Fwd`. A mapping written from the `.glb`
   names sends two slots to clips that do not exist. **Read the imported resource, not the file.**
2. **That stripping also makes the library ship a clip literally called `Idle`**, bare-identical to
   the body's own. Neither an exact-match nor a prefix pass can separate them, so the winner was
   whatever the engine listed first — alphabetical luck turning on the library's name.
   `AnimationClips.Resolve` now offers a model's **own** clips first and lets the library answer only
   what it alone can.
3. **Strip the library's position tracks.** Its rig is `root → Hips`, so hip translation carries the
   whole standing height; the Quaternius rig is `Root → Body → Hips` and its `Body` bone already
   carries that lift. The track landed on top of it and stood the merchant at **1.63 m, floating**.
   Nothing in this game consumes root motion, so the tracks are not merely broken here, they are
   unwanted.
4. ⚠️ **The Quaternius feet are IK goals, not FK bones.** `Foot.L`/`Foot.R` and the pole targets
   `PT.L`/`PT.R` are parented to **`Root`**, not to the shin — `PT.L`'s rest sits 63 cm up and 30 cm
   forward of the foot, which is what gave it away; it is a pole target, not a toe. FK foot rotation
   written onto a root-parented goal leaves the boot pinned while the leg swings, as a black spike
   out of the ankle. **The library is therefore stripped to an upper-body pose from the hips up**
   (`tools/extract_anim_library.gd`), which is exactly right for the three standing poses it is here
   to fill and wrong for its Jog/Crouch/Sitting/Swim clips — none of which are wired to a slot.

Every one of those four was invisible in a log and visible only in a render. **The clip plays either
way.** `tools/extract_anim_library.gd` regenerates the committed `.res`; re-run it whenever the
`.glb` or its retarget settings change.

**Two more from rolling it out to the rest of the cast:**

5. ⚠️ **The `_subresources` path key names the node in the SOURCE scene, not the retargeted one.**
   `"PATH:RootNode/CharacterArmature/Skeleton3D"` — even for a file that already imports as
   `GeneralSkeleton`. Point it at the retargeted name and the key matches nothing, **the retarget
   silently stops applying, and the model reverts to its raw rig** with no error at all. This is how
   `npc_merchant_m` un-retargeted itself mid-session.
6. ⚠️ **A model whose root node carries a translation cannot be retargeted until that is fixed —
   and `tools/normalize_rig_root.py` fixes it.** `chr_player_base`'s `RootNode` has `T = [0, 4.8237, 0]` (and a 0.9161 scale) where
   every other body has zero — a standing violation of §6's "root node carries **scale only, no
   translation**", which had been harmless only because the node transform cancelled the skeleton's
   own offset. With the flag on, the rest fixer consumes the node transform and the character
   **sinks 4.8 m**; with it off, the un-applied scale and the rewritten rest disagree and the spine
   **shears — the player bends double at the waist**. There is no third setting.
   **`chr_player_base` IS now retargeted**, via `tools/normalize_rig_root.py` — see §0.7.

**Status: 11 of the 12 skinned bodies are retargeted** (`chr_player_base` excepted, above;
`fp_arm` has no skin and needs none). Three BoneMaps cover them — `bonemap_quaternius.tres` for the
62-bone rig (9 bodies), and `bonemap_quaternius_lite_fist.tres` / `_palm.tres` for the two 31-bone
reduced rigs (`npc_kael`, `npc_woman_dress`), which have no fourth spine bone and aggregate finger
bones that are deliberately left unmapped.

⚠️ **`npc_woman_dress` had been resolving NOTHING and standing in the Embermarket in its bind pose.**
Its clips are named for the body rather than the beat — `HumanArmature|Female_Idle`, `Female_Run`,
`Female_SwordSlash` — and that prefix emptied every slot she has. `AnimationClips.Bare` now strips a
leading `Female_`/`Male_` alongside the armature prefix, so the next gendered pack works on import.
**A T-posing NPC is the only symptom an unresolved slot ever has**, and it had been there since she
was adopted.

Prefer this import-dock route over a Blender round-trip, which is recorded below as the thing that
destroys bone-parented children — and `npc_hooded` carries a `Sword` bone-parented under `Middle1.R`.

⚠️ **Do not mix kits.** Four kits by one author read as one world; a stray model from a fifth source
reads as a mistake even when it is better made. If the open web or Blender is reached, match the
flat-shaded, untextured-looking style or do not adopt it.

**Crediting is not required** (maintainer direction, 2026-08-08). The build is personal, never
published and never sold, and everything in it is CC0, so no attribution was ever legally owed.
`assets/CREDITS.md` is **frozen as history**: read it for the traps it records, do not add to it, and
do not treat a missing entry as unfinished work. What replaced it is one line: **the manifest**.

### §0.3 Adopting a pack model — the container, and the scale (2026-08-08)

**`tools/gltf_to_glb.py` is how a MegaKit model enters the game.** The packs ship `.gltf` + `.bin` +
shared `.png`; `assets/models/` is `.glb` with images embedded. That is a **container** change and
nothing else, so it must not go through Blender: a round-trip re-exports every vertex, normal and UV
through another tool's opinion of them. The script copies the buffer byte for byte and only appends
the sidecar textures as bufferViews.

⚠️ **THE NATURE MEGAKIT IS NOT UNIFORMLY 1 m = 1 unit.** Its **trees are** — `Pine_1` is 7.32 m,
`CommonTree_3` is 9.43 m, all sane. Its **ground cover is four to ten times life size**:

| shipped as | measures | is called |
| --- | --- | --- |
| `Grass_Common_Short` | **1.33 m tall** | *short* grass |
| `Clover_1` | **1.14 m tall** | clover |
| `Flower_4_Group` | **2.49 m tall** | a flower |
| `Fern_1` | **2.83 m across** | a fern |
| `Pebble_Round_1` | **0.50 m** | a pebble |

Nothing in the files says so, and it is the same class of trap as the `rts` pack's 1/6 scale in the
opposite direction. Every adopted ground-cover prop carries a measured `nodes/root_scale`; the
correction belongs in the `.import` so all nine cells get it, never in one cell's transform.
(Whoever adopted `prp_bush_flowering` hit this and applied 0.70 without recording why.)

⚠️ **Measure in-engine, not by parsing the glTF accessors.** Accessor bounds ignore node scale, and
that reads `prp_boulder` as a 1 cm pebble and `prp_rock_cluster` 35% too large. Instantiate the
imported scene and merge the `MeshInstance3D` world AABBs — the same numbers the game will use.

⚠️ **Four of the five props Phase B was to "replace" were already this pack** — `prp_pine_dead`
(`Bark_DeadTree`), `prp_tree_broadleaf` (`Bark_NormalTree`), `prp_bush_flowering` (`Flowers`) and
`prp_rock_cluster` (`Rocks`) all carry nature-megakit materials. Only `prp_boulder` was foreign
(material `Stone`, no textures, mesh `Resource_Rock_2`) and only it was replaced. Check the material
and texture names before planning a swap: replacing a pack model with another pack model changes 20
cells' look and buys nothing.

### §0.4 Composing a modular building (2026-08-08)

`tools/adopt_kit_model.py` has **two modes and the wrong one is expensive**. `embed` writes one
self-contained `.glb` — right for a prop that owns its textures (nature megakit props are 0.8–3 MB).
`--shared` copies the `.gltf` + `.bin` and puts the textures *alongside*, so a whole kit references
one set. ⚠️ A medieval-megakit wall module carries **~0 MB of geometry and six shared PBR maps
totalling ~17 MB**: embedding fourteen of them cost **204 MB of the same textures fourteen times
over**, where shared they are **43 MB once**.

**The grid.** Walls are **2.00 m wide × 3.12 m tall**; a storey is one wall. Floors are 2×2 m tiles.
Roofs and gables are cut for whole numbers of modules — `Roof_RoundTiles_4x6` and `Roof_Front_Brick4`
fit a 2×3-module shell to the millimetre.

⚠️ **A wall module's outer face is its local −Z.** Yaw each face to turn that face outward: back 0,
left 90, front 180, right 270. Get one wrong and the building is right from three sides and shows
its plaster backing on the fourth — the open-backed-cottage trap one layer down.

⚠️ **THIS KIT SEPARATES A HOLE FROM THE THING THAT FILLS IT, AND A MISSING FILLER IS SILENT.** Four
bit while composing the first building, each of which looked finished from the angle it was built at:

1. **A pitched roof with no gable end** is open to the sky at both ends.
2. **A window WALL is an opening.** Without a window INSERT you see through the house and out the far
   side.
3. **The door leaf is a separate piece from the doorway**, and it hangs on its hinge — its origin is
   *not* its centre (local x −0.05…1.07), so under yaw 180 the node sits 0.51 m off centre.
4. **`Wall_Plaster_WoodGrid` is an OVERLAY FRAME, NOT A WALL.** On its own the storey is a
   see-through lattice; it layers *on* a plain wall at the same transform.

None of these logged anything. `tools/compose_building.py` writes a shell from `<wide> <deep> <storeys>` and carries all four in
its header; `scenes/props/bld_townhouse.tscn` and `bld_cottage_modular.tscn` are its output.

**How the composed shells measure against the monoliths they can replace** (in-engine, not parsed):

| | footprint | height | tris |
| --- | --- | --- | --- |
| composed cottage (2×2×1) | 5.51 × 5.56 | **7.47** | 5 447 |
| composed town house (2×3×2) | 5.51 × 7.57 | 10.59 | 11 669 |
| `bld_house_a` ×3 | 4.74 × 5.88 | **7.50** | 5 758 |
| `bld_house_b` ×5 | 4.26 × 5.03 | 6.80 | 2 288 |
| `bld_cottage` ×3 | 4.84 × 4.70 | **4.20** | 2 336 |
| `bld_inn` ×1 | 8.66 × 8.64 | 7.50 | 7 756 |
| `bld_blacksmith` ×1 | 8.42 × 7.11 | 6.50 | 7 659 |

⚠️ **The kit has exactly one wall height, 3.12 m, and that sets the floor on how low a building can
be.** The composed cottage is a near-exact stand-in for `bld_house_a` (7.47 m against 7.50, and
*cheaper* in triangles) and an acceptable one for `bld_house_b`. It is **78% too tall for
`bld_cottage`**, whose 4.20 m silhouette this kit cannot make — a genuinely low cottage would need a
half-height wall the pack does not ship. Do not force it; that is a real limit, not an oversight.

**What was actually swapped: 11 of 16 placements, 5 cells.** `bld_house_a` and `bld_house_b` are
generic houses and became composed shells, alternating `bld_townhouse` and `bld_cottage_modular` so a
street reads as a street rather than as eleven of the same house. Each cell's own building collider
was **deleted** in the swap — the composed scene carries its own, and two colliders on one building
is two carves in the navmesh.

⚠️ **Three kinds of building were deliberately LEFT monolithic, and that is the more useful result:**

| left alone | why |
| --- | --- |
| `bld_cottage` ×3 | 4.20 m tall; the kit's single 3.12 m wall height cannot go that low. |
| `bld_blacksmith` ×1 | **It reads as a blacksmith** — forge canopy, anvil, open front. A composed 3×3 hall is a generic barn, so swapping *loses* information. |
| `bld_inn` ×1 | Same: its dormers and frontage say "inn". The composed 4×4×2 shell was handsomer and said nothing. |

**A generic wall kit composes generic buildings well and special-purpose buildings badly.** Bespoke
geometry is what made those two readable, and no arrangement of walls and windows replaces it. Both
were composed, rendered against the monolith, judged worse and **deleted** rather than shipped as
scaffolding with no caller — along with the four roof/gable modules adopted only for them.

### §0.5 Interiors (38D, 2026-08-09)

Eight `medieval_interiors` models adopted with `--shared` (13 textures, one set). Four went straight
onto existing callers; four are the prerequisites 38P–38R were promised, and sit unplaced by design —
`docs/playbook/` owns that split and it is not scaffolding.

| swapped | was | now |
| --- | --- | --- |
| `town_hub/StationForge` | `prp_station_forge` | `prp_anvil_log` |
| `town_hub/StationWorkbench` | `prp_station_workbench` | `prp_workbench` |
| `embermarket/StallW3`, `StallE2` | **`prp_gazebo`** | `prp_stall_empty` |
| `emberdeep_mine/CompanyStore` | **`prp_gazebo`** | `prp_stall_cart` |

⚠️ **`prp_station_forge` was a pastel-blue anvil on bright-orange legs** — an off-palette in-house
placeholder standing in the middle of the town hub, and the same class of defect as the hi-vis vest
in the market: invisible in a filename, obvious the moment it is rendered next to the pack.

⚠️ **`prp_gazebo` was standing in as a market stall in three places.** A gazebo is a roof on posts
with no counter; it was collided as **two separate posts**, so the swap had to replace the collider
shape as well as the model. A stall is one counter box.

⚠️ **`Shape_station` is shared by all three crafting stations**, so resizing it for the anvil would
have silently moved the alchemy table's collider too. Each swapped station got its own measured box.

Also re-measured `Shape_boulder` in `emberdeep_mine` — Phase B swapped the model for `Rock_Medium_3`
and left the previous model's numbers behind (3.38 × 2.20 × 3.73 against the real 3.42 × 2.32 × 3.48).
**Swapping a model and keeping its collider is the 38O trap repeating**, and I did it to myself.

### §0.6 The sweep (38E, 2026-08-09)

Scoped as "whatever is left from a non-Quaternius source, especially the `rts` bundle at ~1/6 scale".

**The licence question is closed.** Every vendored pack is CC0 in the manifest except five CC-BY
women that were never downloaded.

**The `rts` scale trap is already handled, and better than expected.** Nine adopted models carry a
compensating `nodes/root_scale` between 2.98 and 8.20 — `prp_jetty`, `prp_warden_post`,
`prp_ore_seam`, `prp_timber_stack`, `prp_mine_head`, `prp_watch_tower`, `prp_dock_complex`,
`prp_fishing_hut`, `prp_gate_palisade`. Every one measures to a sane real-world size in-engine, and
**every declared collider matches its model's measured bounds to the centimetre**. There is no
megakit equivalent for a dock, a mine head or a palisade, so these stay. ⚠️ The spread of scales
means the correction was made per model rather than once at pack level: right answers, arrived at
nine times.

⚠️ **What the sweep actually found was the opposite of an invisible wall: SOLID-LOOKING SCENERY YOU
WALK THROUGH.** An audit for "renders more than 8 m³ and has no `StaticBody3D` anywhere under it"
returned **18 props**: six dead pines, two rock clusters and a ruin pillar in the wilds, six glaciers
up to 17.5 m across in Frostfang, and one banner whose two siblings were already collided.

Three things that fix taught, none of which a log would say:

1. **A collider child inherits its node's scale.** `Rocks1` carries 1.4×, the glaciers 1.2 and 0.9,
   so the shape must be authored in the model's LOCAL units or it comes out 40% too big.
2. **A tree takes a TRUNK collider, not a bounding box.** A 3 m box around a dead pine is an
   invisible wall three metres from anything you can see.
3. ⚠️ **A collider outside the `NavigationRegion3D` blocks the player while the navmesh stays
   ignorant of it** — an enemy then paths straight through a tree it cannot walk through. The wilds
   pines were parented to the cell root and are now under `Nav`, which moves nothing because `Nav`
   is at the origin with an identity basis.

⚠️ **`frostfang_reach/glacier.tscn` has no `NavigationRegion3D` at all** — a bare two-prop stub. Its
ice is now collided, but there is no navmesh there to carve. Recorded rather than papered over.

**Cost of composing**: 61 module instances, 60 meshes, 11.7k tris — against 1 mesh and 5.8k tris for
the monolith it replaces. The collider stays **one box on the whole shell**: fifty little static
bodies would carve fifty little holes in the navmesh.

---

---

- **The library is vendored** at `assets/library/` — 746 CC0 models across 14 bundles, behind a
  **`.gdignore`** so Godot never imports or exports any of it. It costs the build nothing.
  ⚠️ A pack dropped anywhere *else* — the repository root, say — **is in the game**, importing and
  exported, whether or not anyone adapted it. This has now happened twice.
- **`assets/library/manifest.json`** is the index: title, pack, licence and where each model lives.
  It stays even though crediting does not, because it is what makes "check the library first" cost
  one `grep` instead of one session. It has been searched from memory twice and been wrong twice.
- **A model enters the game by being adapted into `assets/models/`** (scaled, origin fixed, mesh
  renamed `Mesh`). The library is source, not content. That is now the whole checklist.
- **CC0 only.** The filter is the manifest's `licence` field, not eye. Five CC-BY models in the
  Ultimate Modular Women bundle were deliberately not downloaded. As of this standardisation
  **every model in `assets/models/` is CC0 and the project owes no attribution** — the
  `prp_tome_stand` release blocker is gone. Keep it that way.

**Two failure modes this migration actually hit — check both, every time:**

1. **Judge a model from behind, not just the front.** `House_4` is the obvious cottage head-on and
   its **back is open** — walls, no roof, hollow interior. Four RTS huts/shacks failed the same way.
   This is the second time open-sided models have shipped or nearly shipped here (see the "roofs on
   stilts" incident in `CREDITS.md`).
2. **The glTF importer plants an `Icosphere`** bone-shape placeholder in a `glTF_not_exported`
   collection on **every rig load**. The exporter skips it, so files are clean — but it spans
   z −1…+1, so any bounding box that includes it reads 1 m too tall and silently produces a wrong
   scale. Exclude that collection when measuring, and **verify a written file by reading the file**
   (parse the GLB), not by re-importing into the same polluted scene.

**Sources, in order:** the vendored library → Poly Pizza's Quaternius catalogue (no login, direct
`static.poly.pizza/<uuid>.glb`, and the bundle page JSON carries every id) → quaternius.com/itch
(**click-through only, cannot be scripted** — the maintainer must download those zips) → §2's wider
list, only for what Quaternius does not make.

---

## 1. The order of operations — never reversed

1. **Search the web** for an appropriate open-source model.
2. **Evaluate** whether it can be used (licence first, then fit).
3. **Adapt it with the Blender MCP** if it is close but not perfect.
4. **Create from scratch only** when a thorough search shows nothing suitable exists.

Creating from scratch is the **rare exception**, and reaching for it requires that *all four*
of these hold:

- an extensive web search was completed,
- no acceptable open-source asset exists,
- modifying an existing asset is impractical,
- combining multiple assets cannot solve the problem.

> **Do not assume a model does not exist.** "I couldn't think of one" is not a search.

---

## 2. Search requirement

Web search is permitted and **required** for every model request. Search **multiple**
reputable repositories before concluding one must be built — not one, and not the first hit.

Starting set (non-exhaustive):

| Source | Notes |
| ------ | ----- |
| **Poly Pizza** | CC0 / CC-BY low-poly; closest match to this project's §1.1 style |
| **Kenney Assets** | CC0, game-ready, consistent kits |
| **Quaternius** | CC0 low-poly fantasy/creature packs |
| **OpenGameArt** | Mixed licences — **check each asset individually** |
| **Sketchfab** | Downloadable only, and only with a compatible licence |
| **Blend Swap** | Check per-asset licence tier |
| **CGTrader Free** | Free tier only; verify the specific licence |
| **GitHub asset repos** | Often the cleanest provenance |
| **Itch.io asset packs** | Many CC0/CC-BY fantasy kits |
| **Khronos glTF Sample Assets** | Reference-grade glTF, permissive |

---

## 3. Licence requirements — the hard gate

Every asset **must** have a licence compatible with this project. Verify and record:

- commercial use
- modification rights
- redistribution rights
- attribution requirements
- overall compatibility

**Never use** an asset with unclear licensing, proprietary/copyrighted content, unknown
ownership, or an incompatible licence. **If licensing cannot be verified, discard the asset** —
"probably fine" is a discard.

> **Note on commercial use.** This build is private/personal and is not sold or published, so
> commercial rights are not strictly required today. Verify them anyway: it costs nothing at
> download time and keeps every option open later. Prefer **CC0 > CC-BY > other permissive**.
> Avoid paid or closed assets outright.

---

## 4. Selection — when several qualify

Do **not** simply take the first result. Prefer the asset that best matches:

- visual style (`ART_STYLE.md` §1 — low-poly build, grounded proportions)
- topology quality and triangle budget (`ART_STYLE.md` §3)
- game-readiness
- optimization
- ease of modification
- consistency with what is already in `assets/models/`

**Consistency across the game beats individual asset quality.** A slightly worse model that
matches the set is the better choice.

---

## 5. The Blender MCP is an adaptation tool

`mcp__blender__*` is **not** the primary source of assets. Its intended uses:

adapting downloads · changing proportions · simplifying meshes · combining assets · repairing
geometry · improving UVs · adjusting materials · creating LODs · optimizing for gameplay ·
minor stylistic adjustments.

**If a downloaded asset is close but not perfect, adapt it — do not abandon it and model
clean.** This is the specific reversal of the old default.

> **Scene hygiene still applies** (`CLAUDE.md` §2): never leave multiple models stacked at the
> world origin. Lay assets out side by side with clear spacing so the maintainer can see what
> is being worked on; zero an object's transform only transiently at export time.

---

## 6. Pre-integration checklist

Before any model is committed:

- [ ] scale verified (1 unit = 1 m)
- [ ] orientation verified
- [ ] transforms cleaned/applied
- [ ] unused materials removed
- [ ] topology sane for the class — `ART_STYLE.md` §3's bands are **lifted** for sourced assets;
      decimate only when a model is visibly heavy for what it is
- [ ] **origin at the base centre** in all three axes, and the bounding-box *size* matched to the
      model it replaces (props: so scene transforms stay valid; actors: so the mesh matches its
      `Capsule*` reach). Verify the **height**, not just where the top lands — a model floating
      above its origin passes a top-only check while being far too short
- [ ] rigged: the root node carries **scale only**, no translation; every gameplay slot resolves
      against the **imported** scene's `AnimationPlayer.get_animation_list()`
- [ ] inspected in Blender **at eye level, straight on**, not in three-quarter view
- [ ] unnecessary geometry removed
- [ ] normals verified
- [ ] object hierarchy cleaned
- [ ] naming consistent with `assets/models/<class>/<prefix>_<name>.glb`
- [ ] collision compatible (static collider added in scene/factory — **never** runtime-parsed
      visual-mesh collision, per the navmesh rule in `CLAUDE.md` §8)
- [ ] performance sane at gameplay distance

---

## 7. Documentation — required for every asset

Every new asset gets an entry in **`assets/CREDITS.md`** recording:

- asset name
- download source
- direct URL
- licence type
- why it was selected
- Blender MCP modifications performed (if any)
- optimization steps performed

A CC0 asset gets an entry too — attribution is not the only reason to record provenance. An
asset with no entry is not finished.

---

## 8. Style consistency

`ART_STYLE.md` remains the visual source of truth, but the maintainer relaxed two of its clauses
for the Phase 35 migration and this section follows them: **§3's triangle bands are lifted** and
**a sourced asset keeps its own materials** (see `ART_STYLE.md` §3 and §4). A pack whose textures
already read as stylised low-poly is accepted as it ships; a photo texture must still stop reading
as a photo. Prefer sourcing further assets from the packs already in use over re-tinting new ones.

The one clause of `ART_STYLE.md` §6.3 that this policy overrides is *"if adapting costs more
than modeling clean — model clean."* Adaptation is now preferred; modelling clean requires
the §1 four-part test.

### §0.7 Normalising a rig's root node (2026-08-09)

**`tools/normalize_rig_root.py` is how a model with a crooked root node is repaired**, and it is the
reason the player can be retargeted at all. Compared against a body that retargets cleanly:

| | `RootNode` | `CharacterArmature` | root bone |
| --- | --- | --- | --- |
| `npc_townsman` | T 0, R identity, S 1 | R −90° X, S 100 | t = (0, −0.00072, 0) |
| `chr_player_base` (before) | **T (0, 4.8237, 0), S 0.9161** | R −90° X, S 100 | **t = (0, 0.00725, −0.05264)** |

⚠️ **Those are two errors that cancel.** The root bone's −0.05264 on Z, carried through the
armature's −90° X rotation and ×91.61 of scale, lands at −4.822 in world Y — which the root node's
+4.8237 cancels almost exactly. The model rendered correctly, and **all four** rest-fixer flag
combinations broke it, because every one redistributes exactly those two numbers.

The tool collapses the cancellation rather than moving it: the scene root becomes exact identity
(its uniform scale folded into the armature, so the total reaching the bones is unchanged —
0.9161 × 100 = 1 × 91.61), and its translation is folded into the root bone in the bone's own space.
Vertices and inverse bind matrices are never touched, and all **17 bone-parented children survive**,
which is the whole reason this is not a Blender job.

⚠️ **The animation keyframes have to move with the rest pose.** All 24 of this model's animations
drive the root bone's translation, so fixing only the rest pose is undone the instant anything
plays. ⚠️ **And several animations SHARE one output accessor**, so each must be rewritten exactly
once — an earlier draft applied the factor once per animation and overflowed a float32 on the
nineteenth pass. That failure was at least loud; a smaller factor would have silently produced a
subtly wrong rig.

**Result: 12 of 12 skinned bodies retarget.** The player now has block, cast and channel.
