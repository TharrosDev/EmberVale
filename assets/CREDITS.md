# Asset credits & provenance

Required by [`docs/ASSET_POLICY.md`](../docs/ASSET_POLICY.md) §7 and
[`docs/ART_STYLE.md`](../docs/ART_STYLE.md) §6.3.

**Every sourced asset gets an entry here — including CC0 ones.** Attribution is not the only
reason to record provenance: six months on, "where did this come from and may we keep using
it?" must be answerable without archaeology. An asset with no entry is **not finished**.

---

## Current state

**28 of the project's 33 models are sourced; the other 5 are still in-house.** The asset
migration replaces the in-house set category by category. Props, characters, creatures, buildings
and the sword are all done. **Still in-house (5):** the first-person arm, the Ashen Acolyte, and
three props with no suitable match — see *Searched for, not replaced* for why each stayed. Every
one of the five now has a recorded search; `wpn_sword_iron` did not until the post-migration audit,
and searching for it took one round.

*(An earlier revision of this file, and PR #202's description, said 15 props. The prop count was
16 — corrected here because this file is the provenance record. The model total dropped 34 → 33
when the orphaned `enm_goblin_brute.glb` was deleted.)*

⚠️ **One asset requires attribution.** `prp_tome_stand` is CC BY 3.0, not CC0 — see its entry.
That obligation ships with the game and must survive any future asset cull. **It is currently
unmet in-game**: there is no credits screen anywhere in `src/UI`, and recording the attribution
in this file is not compliance. That is a release blocker, not a nice-to-have.

⚠️ **All five rigged replacements shipped mis-scaled and floating, and are now fixed** — see
*Defects found and fixed in the post-migration audit* at the bottom for the root cause, which is
worth reading before converting another model. The bounding-box claims this file used to make
about them were wrong: they were verified by comparing the **top** of the model against the
predecessor's height, which a model floating above its origin passes while being 25–40% too short.

Audio under `assets/audio/` is either CC0/open `.ogg`/`.wav` or `ProceduralAudio`
placeholders generated at runtime (see `CLAUDE.md` §8, "a new sound cue"); any CC0 audio file
added from here on gets an entry below too.

---

## Entry template — copy this

```markdown
### <asset name>

- **Source:** <repository — e.g. Poly Pizza, Kenney, Quaternius>
- **URL:** <direct link to the asset page, not the site root>
- **Licence:** <CC0 / CC-BY 4.0 / MIT / …>  — attribution string if required
- **Author:** <creator, if the licence needs it>
- **Why selected:** <fit against ART_STYLE §1 style, topology, budget, consistency with the set>
- **Blender MCP modifications:** <retopo, decimate, re-proportion, merged with X, UVs, materials, LODs — or "none">
- **Optimization:** <scale/orientation verified, transforms applied, unused materials removed,
  normals checked, tri count before → after, collider approach>
- **Lands at:** `assets/models/<class>/<prefix>_<name>.glb`
```

---

## Sourced assets

### Kenney — Survival Kit (CC0)

- **Source:** Kenney · **URL:** https://kenney.nl/assets/survival-kit
- **Licence:** CC0 1.0 Universal (public domain) — verified in the pack's own `License.txt`.
  No attribution required; recorded here for provenance.
- **Author:** Kenney (www.kenney.nl)
- **Why selected:** genuinely low-poly (98–320 tris per model), ships glTF/GLB directly, and the
  whole pack shares **one `colormap` texture atlas**, so a camp full of these props costs one
  material. Closest available match to the project's silhouette-first look.
- **Used for:** `prp_crate` (box), `prp_campfire` (campfire-pit), `prp_tent` (tent),
  `prp_rock_cluster` (rock-a), `prp_cache_chest` (box-large), `prp_cache_chest_open`
  (box-large-open), `prp_station_forge` (workbench-anvil), `prp_station_workbench` (workbench),
  `prp_station_alchemy` (workbench-grind), `prp_pine_dead` (tree-trunk), `prp_waystone` (signpost)

### Kenney — Fantasy Town Kit 2.0 (CC0)

- **Source:** Kenney · **URL:** https://kenney.nl/assets/fantasy-town-kit
- **Licence:** CC0 1.0 Universal — verified in the pack's own `License.txt`. 160 assets.
- **Author:** Kenney (www.kenney.nl)
- **Why selected:** same atlas + tri-count profile as the Survival Kit, so the two packs mix
  without reading as two packs. Supplies the town/ruin pieces the survival kit lacks.
- **Used for:** `prp_lamp_post` (lantern), `prp_banner_guild` (banner-green),
  `prp_ruin_pillar` (pillar-stone), `prp_ruin_wall` (wall-broken), `prp_arena_wall` (wall-block)

**Blender MCP modifications (all 16):** imported glTF → joined multi-part kit pieces into a single
mesh (parented children otherwise compound their parent's scale — this silently produced a 10 m
anvil on the first pass) → scaled to the in-house model's footprint → applied transforms → origin
dropped to the base → mesh renamed `Mesh` → exported GLB over the original filename.

Kenney kits are authored at roughly **1/4 scale** (their crate is 0.25 m; ours is 1.24 m), so every
asset needed a 1.9×–6.5× scale-up. Five needed **per-axis** rather than uniform scaling because the
source aspect ratio differed from the model the scenes were built around — `prp_arena_wall` is a
long low wall where Kenney's `wall-block` is a cube, and `prp_ruin_wall` also needed a 90° yaw.

⚠️ Every replacement matches its predecessor's bounding-box **size**, so no scene transform
changed — but two shipped with the wrong **position**: `prp_ruin_wall` sat 13.6–17.6 m along +X of
its own origin, and `prp_banner_guild` 1.5–1.7 m along +X. Both re-centred; see *Defects found and
fixed*.

**Optimization:** single joined mesh per prop, transforms applied, origin at base, 44–305 tris
(the in-house set was 28–1048). Godot extracts the embedded atlas to a sidecar PNG per model —
16 copies of the same 11 KB image, 148 KB total, accepted as standard importer behaviour.

### Practice Dummy — Quaternius (CC0)

- **Source:** Poly Pizza · **URL:** https://poly.pizza/m/1pYOHhwjXP
- **Licence:** CC0 1.0 Universal — no attribution required; recorded for provenance.
- **Author:** Quaternius
- **Why selected:** a mannequin silhouette that reads as a practice target at a glance, and 104
  tris — lighter than the 648-tri in-house dummy it replaces. Chosen over a "dumbbell" hit from
  the same search, which was visually inspected and rejected.
- **Used for:** `prp_training_dummy`
- **Blender MCP modifications:** joined, per-axis scaled to the original's 1.10 × 1.80 × 0.62 box,
  transforms applied, origin dropped to base, mesh renamed `Mesh`.

### Pulpit — 4444ESOUSA (CC BY 3.0) ⚠️ attribution required

- **Source:** Poly Pizza · **URL:** https://poly.pizza/m/3nHkaEsTGL
- **Licence:** **Creative Commons Attribution 3.0** — https://creativecommons.org/licenses/by/3.0/
- **Required attribution:** *"Pulpit" by 4444ESOUSA, licensed CC BY 3.0.* This is the only asset
  in the project carrying an attribution obligation. It must appear in the game's credits screen
  before release; recording it here alone is not sufficient compliance.
- **Why selected:** the only true lectern silhouette found across two search rounds — the CC0
  alternatives were a potion bottle and a skull candle. 68 tris, the lightest model in the game.
  Accepted a non-CC0 licence deliberately because the CC0 field had nothing that read as a lectern.
- **Used for:** `prp_tome_stand`
- **Blender MCP modifications:** joined, per-axis scaled to the original's 0.55 × 1.22 × 0.44 box,
  transforms applied, origin dropped to base, mesh renamed `Mesh`.

### Sword — Quaternius (CC0)

- **Source:** Poly Pizza · **URL:** https://poly.pizza/m/9lLmH8Et4K
- **Licence:** CC0 1.0 Universal (Public Domain), stated on the model page — no attribution
  required; recorded for provenance. **Author:** Quaternius
- **Why selected:** the in-house sword was a flat plank with a bar crossguard, 176 tris and no
  grip detail. This is a proper arming sword — fullered blade, notched crossguard, wrapped grip,
  pommel — which reads as "iron sword" at viewmodel distance, where it is the single most-looked-at
  object in a first-person game. Compared at eye level, straight on, against the in-house model and
  against `Sword_big` (`/m/ajOJ2NLz5m`, also Quaternius CC0, 830 tris); `Sword_big`'s broad leaf
  blade is more stylised and less grounded than ART_STYLE §1 asks for.
- **Used for:** `wpn_sword_iron` (872 tris, up from 176 — well inside the lifted §3 bands)
- **Blender MCP modifications:** vertex data baked (transforms written into the mesh, not left on
  the node), uniformly scaled 0.41702×, centred in X/Z and translated so the model reproduces the
  in-house sword's exact vertical layout — 0.96 m tall spanning **−0.035 → 0.925**, blade along
  +Y with the grip at the origin. That is what `FirstPersonArmsComponent` and `PlayerFactory`'s
  hand-bone mount were authored against, so neither needed changing. Footprint 0.223 × 0.051
  (was 0.16 × 0.05) — a slightly wider crossguard, nothing else moved.

> **This was the one asset never searched for**, which is a `docs/ASSET_POLICY.md` §1 miss rather
> than a considered outcome. It took one search.

### Orc — Quaternius (CC0)

- **Source:** Poly Pizza · **URL:** https://poly.pizza/m/5vO2YJsPEf
- **Licence:** CC0 1.0 Universal · **Author:** Quaternius
- **Why selected:** a rigged green humanoid with a weapon that reads as a goblin at 1.12 m, and it
  ships every combat clip the project needs — Idle, Run, Punch, HitReact, Death. Chosen over a
  lighter "Orc_Blob" (2,296 tris) because the blob has no Run and reads as a creature rather than a
  humanoid raider.
- **Used for:** `enm_goblin` (7,344 tris)

### Knight — Quaternius (CC0)

- **Source:** Poly Pizza · **URL:** https://poly.pizza/m/66kQ4dBBC7
- **Licence:** CC0 1.0 Universal · **Author:** Quaternius
- **Why selected:** an armoured blade-carrying figure with the richest clip set found — it adds
  Slash and Stab on top of the standard set, so the boss's attack reads as a weapon swing rather
  than a punch.
- **Used for:** `boss_iron_king` (7,070 tris)

**Blender MCP modifications (both):** scaled the **root node only**, exported with skins and
animations.

⚠️ **Both shipped mis-scaled** — the intended heights (goblin 1.12 m, Iron King 2.42 m) were not
reached. The root node carried the scale factor in its **Y translation as well as its scale**, so
the goblin was 0.83 m tall floating 0.29 m off the ground and the Iron King 1.46 m tall floating
1.03 m. The check that passed them measured the model's top, which lands on target either way.
Both re-exported at the intended height; see *Defects found and fixed*.

⚠️ **Rigged models are converted differently from props.** The prop pipeline joins meshes and
applies transforms; doing that to a skinned model destroys the rig. Applying scale to an armature
is also wrong — it leaves the animation's keyed bone locations at the old scale and silently wrecks
every clip. The scale therefore stays on the node and the glTF exporter writes it as a node
transform, which Godot honours.

**A trap this hit:** Blender keeps `bpy.data.actions` alive across imports, and the glTF exporter
writes every action it can see. The first export baked *every previously imported model's*
animations into both files — a goblin carrying `Fast_Flying` and `Headbutt`, an Iron King carrying
`Bite_Front`. The purge between conversions must clear actions, armatures, meshes and materials,
not just objects.

**Verified post-import, not just on the source files:** Godot preserves the `CharacterArmature|`
prefix (which is exactly why `AnimationClips` has to strip it) and strips the in-house `-loop`
suffix. Both projects' naming resolves; the unit tests use the post-import names.

### Adventurer / Farmer / Rogue / static townsfolk — Quaternius & others (CC0)

| Model | Replacement | Source | Tris |
| --- | --- | --- | --- |
| `chr_player_base` | Adventurer, Quaternius | https://poly.pizza/m/5EGWBMpuXq | 10,198 |
| `npc_kael` | Rogue | https://poly.pizza/m/DgOCW9ZCRJ | 6,050 |
| `npc_vendor` | Farmer, Quaternius | https://poly.pizza/m/7pn3R6hPvE | 5,476 |
| `npc_innkeeper` | townsperson (static) | https://poly.pizza/m/BCMT02FrVE | 3,893 |
| `npc_guild_rep` | guard (static) | https://poly.pizza/m/sbaM8I229r | 416 |

- **Licence:** all CC0 1.0 Universal. A CC-BY blacksmith was found and **deliberately passed over**
  — the project already carries one attribution obligation and a second buys nothing here.
- **Why selected:** the Adventurer and Rogue carry the richest combat clip sets found (Sword_Slash /
  Dagger_Attack, Run, HitRecieve, Death) and read as distinct silhouettes, so the companion does not
  look like a recolour of the player. The two static townsfolk have no rig at all, which is correct —
  the NPCs they replace are scene-placed props with no animation component.
- **Blender MCP modifications:** rigged models scaled at the **root node only** and exported with
  skins + animations; static ones went through the prop pipeline (join → scale → apply → origin at
  base → mesh renamed `Mesh`).

⚠️ **The three rigged ones shipped mis-scaled and floating.** Only the two static townsfolk
(`npc_innkeeper` 1.62 m, `npc_guild_rep` 1.68 m, both feet-at-origin) came out correct. The player
was 1.10 m tall floating 0.60 m, Kael 1.26 m floating 0.47 m, the vendor 1.06 m floating 0.59 m —
against predecessors of 1.70 / 1.73 / 1.65 m standing on the ground. All three re-exported at the
intended height; see *Defects found and fixed*.

⚠️ **`block`, `cast` and `channel` have no replacement clip.** No CC0 character pack found ships
them. `AnimationClips` returns empty and `CharacterAnimationComponent` already guards on that, so
nothing breaks — but the *visual* for blocking and casting is gone on these rigs. Judged acceptable
because the player is **first-person** (this body is retained for cutscenes only, CLAUDE.md §1) and
**Kael is not a caster** (`KnownSpellIds` is empty). Revisit if a third-person camera or a casting
companion ever lands.

**Verified in-engine, per slot.** Loaded each imported scene and resolved all eight slots against
Godot's real animation list: player and vendor → `Idle` / `Run` / `Sword_Slash` / `HitRecieve` /
`Death`; Kael → `Dagger_Attack`, **not** its `Attacking_Idle` stance, which is the alias ordering
earning its place on real data.

`npc_kael` ships each clip twice — the Rogue source carries two armatures sharing one action set.
Harmless (resolution takes the first match) and left alone rather than risking the rig to save a
few KB.

### Houses — "House_1" and a trimmed farmhouse (CC0)

| Slot | Model | Source | Faces | Size |
| --- | --- | --- | --- | --- |
| `bld_house_a` | House_1 | https://poly.pizza/m/BH2XHWUNmF | 5,758 | 4.74 × 7.50 × 5.88 m |
| `bld_house_b` | Farm_SecondAge, field removed | https://poly.pizza/m/91wMLb9kKo | 2,288 | 4.26 × 6.80 × 5.03 m |

- **Licence:** both CC0 1.0 Universal.
- **Why selected:** both are **enclosed** half-timbered houses — real walls, windows, doors — in a
  matching medieval style but distinct roofs (teal shingle vs red tile), so a square of four reads
  as a village rather than a duplicated asset.
- **Blender MCP modifications:** joined, **uniformly** scaled to a realistic two-storey height,
  origin dropped to the base, mesh renamed `Mesh`. `bld_house_b` additionally had its attached farm
  field removed: the mesh splits into 1,086 loose parts (one per fence plank), so the field was
  separated by a **height cut** at 45% of the model's peak — 466 flat parts dropped, 620 kept.

⚠️ **`bld_house_b`'s origin was dropped to the base in Y only.** In X/Z it still sits where the
removed farm field put it: the mesh spans X −4.48…−0.23 and Z −4.85…+0.18, so the house is centred
2.35 m −X and 2.33 m −Z of its own origin. Its `Shape_bldgB` collider is centred on the origin, so
the box missed the house by ~3.3 m diagonally at all three mounts. Re-centred; see *Defects found
and fixed*.

⚠️ **The first attempt at this shipped and was wrong.** An earlier revision used a timber A-frame
model that looked right in a three-quarter silhouette but is **open-sided** — the maintainer
described the result as "roofs on stilts". It was chosen on outline and triangle count without
checking for walls. Candidates are now inspected **at eye level, straight on**, which is the view
that exposes a missing wall; that pass also caught a market stall, a modern apartment block, a
neoclassical civic building and a torus literally named `House_Open`.

⚠️ **Collision geometry was deliberately changed** — the one place in this migration where it was.
The originals were wide, low, barn-like boxes (6×5×8 and 8×6×6); every enclosed house found is
narrow and tall. Forcing one into the old box meant ~2× stretching, and matching the old footprint
meant ~10 m houses towering over a 1.7 m player. So the houses are sized realistically at **uniform
scale** and the colliders now fit them: `Shape_bldgA` → 4.74×7.50×5.88, `Shape_bldgB` →
4.26×6.80×5.03, with the parent Y and model offsets moved to match in both
`town_hub.tscn` and `clan_hold.tscn` (seven mounts). A collider should fit its building; keeping
the old boxes would have meant invisible walls.

---

## Searched for, not replaced

Two full search rounds — Kenney (Fantasy Town, Survival, Modular Dungeon, Modular Cave), Poly
Pizza across ~10 query terms, with every shortlisted candidate imported into Blender and
**visually compared side by side** rather than judged on its filename. **All six** remaining
in-house models are listed here; the last three were missing from this table until the
post-migration audit, and one of them had never been searched for at all.

| Model | What the searches actually returned | Why it stayed in-house |
| --- | --- | --- |
| `prp_brazier` | cooking spit, bonfire, two rock fire-pits, potion bottle, skull candle | Nothing was a brazier — a raised fire bowl on legs. The near-misses are all ground-level fires, which is a different silhouette and a different read in a lit town square. |
| `prp_glacier` | grey rocks, a cliff, a drinks can, an orange crystal | No ice. The closest geometric fit (`Environment_Cliff3`) is **19,960 faces** — more than the entire rest of the model set combined — and still reads as rock, not ice, which is wrong for Frostfang. |
| `prp_relic` | two swords, generic cubes | A divine relic is too project-specific to source; its silhouette is authored narrative, not a generic prop. |
| `fp_arm` | `Rigged Fps Arms` ×2 (Poly Pizza), WRAD ARMS (itch.io) | **Searched properly for the first time; still no.** Three separate blockers, any one of which is enough. **(a) Shape:** every FPS-arms asset found is a *bonded left+right pair driven by one armature*. `fp_arm.glb` is a **single** arm that `FirstPersonArmsComponent` instantiates **twice** and animates procedurally (bob, slash arc, guard blend) — dropping in a pair gives four arms. Using one properly means rewriting that component around a rig, which is a behaviour change, not an asset swap. **(b) Licence:** both Poly Pizza candidates (`/m/XdHWM8uSAO`, `/m/AMGNKfQqVc`) are **CC BY 3.0**, a second attribution obligation the maintainer already declined once for the blacksmith. **(c) Style:** WRAD ARMS clears the licence bar (CC0, 1,200 tris, rigged) but is explicitly Half-Life-1 / boomer-shooter styled with a 512² texture, against ART_STYLE §1's grounded weathered fantasy — and it ships only as an itch.io zip with no direct file URL. **Best remaining route:** extract and re-pose a forearm from the Quaternius Adventurer already in `chr_player_base.glb` — same CC0 licence, same style, and the viewmodel would then match the body it belongs to. That is a modelling job, not a download. **This is still the highest-value gap in the set:** the game is first-person, so these 448 untextured triangles are on screen essentially all the time while `chr_player_base` is only seen in cutscenes. |
| `enm_ashen_acolyte` | one promising "mage" | The mage candidate had **no skin and no animation clips at all**, so it could not drive `CharacterAnimationComponent`. No other rigged CC0 robed figure was found. The in-house model is the only remaining rig authored to this project's own clip vocabulary (`idle-loop`, `run-loop`, `cast`) and **the only actor in the game whose `cast` slot resolves** — replacing it with a sourced rig would cost the casting animation, not gain one. |

Re-open these if a winter/ice, dungeon-dressing, FPS-arms or robed-character pack lands that
covers them; do not force a bad match to raise the sourced-asset count.

---

## In-house assets

Authored from scratch via the Blender MCP; listed for provenance completeness, not because a
licence requires it. See `docs/SESSION_PLAYBOOK.md` Phase 30 for the authoring notes.

**This is the post-migration list — five models.** (An earlier revision of this table still listed
the whole Phase 30 output, including everything the migration had already replaced and the deleted
`enm_goblin_brute`. Corrected in the post-migration audit.)

| Class | Path | Phase |
| ----- | ---- | ----- |
| Characters | `assets/models/characters/fp_arm.glb` | 30B |
| Creatures | `assets/models/creatures/enm_ashen_acolyte.glb` | 30D |
| Props | `assets/models/props/` (`prp_brazier`, `prp_glacier`, `prp_relic`) | 30H |

---

## Defects found and fixed in the post-migration audit

Measured three ways — raw glTF accessors, the Godot-**imported** scene, and a Blender re-import —
which all agreed. Both defects are **fixed**; the models were re-exported, no scene, collider or
code file was touched.

### Root cause (both defects, one bug)

The conversion measured each model's bounding box across **every object in the Blender scene**, and
this Blender session **injects a stray `Icosphere` at the world origin spanning z −1…1 after every
glTF import**. So every measurement came out exactly 1 m too tall and centred on the origin rather
than the model. This is the hygiene rule in `CLAUDE.md` §2 — *never leave anything stacked at the
world origin* — failing in the one place it actually costs something.

**Measure only the objects the import just created.** The clean pattern:

```python
before = {o.name for o in bpy.data.objects}
bpy.ops.import_scene.gltf(filepath=src)
model = [o for o in bpy.data.objects if o.name not in before and not o.name.startswith("Icosphere")]
```

### 1. Every rigged replacement floated and was 25–40% too short

`RootNode.position.y` came out equal to the scale factor on all five rigged models — the arithmetic
signature of `location.z = -raw_min_z * scale` with `raw_min_z` reading −1.0 off the stray sphere.
The verification that passed them compared the model's **top** against the predecessor's height,
which a floating model satisfies while being far too short.

| Model | was: span (height) | now: span (height) | new root scale |
| --- | --- | --- | --- |
| `chr_player_base` | 0.605 → 1.700 (**1.10**) | 0.000 → 1.700 (**1.70**) | 0.937121 |
| `npc_kael` | 0.472 → 1.730 (**1.26**) | 0.000 → 1.729 (**1.73**) | 0.644687 |
| `npc_vendor` | 0.587 → 1.650 (**1.06**) | 0.000 → 1.653 (**1.65**) | 0.913183 |
| `boss_iron_king` | 1.033 → 2.492 (**1.46**) | 0.000 → 2.420 (**2.42**) | 1.792135 |
| `enm_goblin` | 0.295 → 1.120 (**0.83**) | 0.000 → 1.117 (**1.12**) | 0.396585 |

Each is now scaled to the height of the in-house model it replaced, with the root node carrying
**scale only**. Held items are excluded from the body measurement (the Iron King's `Knife` and the
goblin's `Orc_Weapon` would otherwise set the height); the goblin's weapon tip hangs 6 cm below the
feet in the bind pose, which is how it is held.

Re-verified against the imported scenes: clip counts unchanged (24/24/24/20/14, so no action
leakage between conversions) and all eight gameplay slots resolve exactly as before.

### 2. Three models sat away from their colliders

| Model | origin offset | consequence |
| --- | --- | --- |
| `prp_ruin_wall` | X +13.62 … +17.62 | the wall rendered ~15 m from its `Shape_ruin` box — an invisible wall at the ruins and a free-floating wall beyond it |
| `bld_house_b` | centre 2.35 m −X, 2.33 m −Z | `Shape_bldgB` missed the house by ~3.3 m diagonally at all three mounts |
| `prp_banner_guild` | X +1.50 … +1.70 | the guild board's interact collider was 1.6 m from the visible banner |

All three re-exported with the origin at the base centre. Bounding-box **sizes** and triangle
counts are unchanged, so no scene transform or collider needed editing.

## Known, not fixed

- **`prp_banner_guild`'s collider is a poor shape match.** `Shape_banner` is 0.5 × 3 × 0.12 (wide
  in X, thin in Z); the banner is 0.2 × 3.36 × 2.0 (thin in X, wide in Z). Now that the model is
  centred the collider sits inside it, so the interact prompt works, but it covers only a 0.12 m
  strip of a 2 m banner. Changing it is a collision change and was left alone.
- **`enm_ashen_acolyte` has no attack clip.** Its clips are `cast`/`death`/`hit`/`idle`/`run`, so
  the `attack` slot resolves empty. It is a caster, so this may be intended — but it is the only
  actor in the game whose `cast` slot resolves at all, and the only remaining rig authored to this
  project's own clip vocabulary. Replacing it costs the casting animation.
- **`block` / `cast` / `channel` resolve empty on all five sourced rigs.** See the note above.
