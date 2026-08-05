# Asset credits & provenance

Required by [`docs/ASSET_POLICY.md`](../docs/ASSET_POLICY.md) §7 and
[`docs/ART_STYLE.md`](../docs/ART_STYLE.md) §6.3.

**Every sourced asset gets an entry here — including CC0 ones.** Attribution is not the only
reason to record provenance: six months on, "where did this come from and may we keep using
it?" must be answerable without archaeology. An asset with no entry is **not finished**.

---

## Current state

**29 of the project's 33 models are sourced; the other 4 are still in-house.** The asset
migration replaces the in-house set category by category. Props, characters, creatures, buildings,
the sword and the first-person arm are all done. **Still in-house (4):** the Ashen Acolyte and
three props with no suitable match — see *Searched for, not replaced* for why each stayed.

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
  the node), uniformly scaled 0.41702×, centred in X/Z, and positioned so the **blade starts at
  y = 0.175 — exactly where the in-house sword's did**. 0.96 m long, spanning −0.126 → 0.834, blade
  along +Y. Footprint 0.223 × 0.051 (was 0.16 × 0.05) — a slightly wider crossguard.
- ⚠️ **Aligning the overall bounding box was the wrong call and shipped visibly wrong.** The first
  pass matched the in-house sword's *total span* (−0.035 → 0.925). But the Quaternius sword has a
  much longer grip, so the guard landed 0.09 m higher and the hand ended up holding the pommel with
  a hand's width of bare handle above the fist. **What the hand mount actually pins is the grip, so
  that is what has to line up** — for a held object, align the feature the hand sits on, not the
  bounding box.

> **This was the one asset never searched for**, which is a `docs/ASSET_POLICY.md` §1 miss rather
> than a considered outcome. It took one search.

### First-person arm — extracted from the Adventurer already in the project (CC0)

- **Source:** derived asset — the right forearm and hand of `chr_player_base.glb`
  (Adventurer, Quaternius) · **URL:** https://poly.pizza/m/5EGWBMpuXq
- **Licence:** CC0 1.0 Universal · **Author:** Quaternius
- **Why this route:** every FPS-arms pack found was rejected — the two Poly Pizza *Rigged Fps Arms*
  are CC BY, and the CC0 WRAD ARMS is Half-Life-1 styled and ships only as an itch.io zip. All of
  them are also a *bonded left+right pair on one armature*, where `FirstPersonArmsComponent`
  instantiates a **single** arm twice. Lifting the arm off the body the player already has solves
  all three problems at once: same licence, same style, and the viewmodel now matches the body seen
  in cutscenes.
- **Blender MCP modifications:** posed the Adventurer's rig with its own **`Idle_Sword`** clip so
  the hand came out **closed around a hilt** rather than open in a T-pose; applied the armature
  modifier to bake that pose; kept only vertices weighted ≥0.5 to `LowerArm.R`, `Wrist.R` and the
  nineteen finger/thumb bones; rotated the hanging arm to point down Godot's −Z; scaled 1.8331× to
  the old stub's 0.609 m long axis and translated to its exact bounding box, so `RightRest` /
  `LeftRest` and the sword mount needed no change.
- **Optimization:** 448 → 1,016 tris. That is a deliberate increase on the model that is on screen
  more than any other in a first-person game, and it is what buys actual fingers.
- ⚠️ **The left arm is now mirrored** (`Scale.X = -1` in `FirstPersonArmsComponent`). The old stub
  was reused unmirrored because it had no thumb to get wrong; a real hand does, and two right hands
  would read immediately.
- **Lands at:** `assets/models/characters/fp_arm.glb`

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

⚠️ **Both shipped mis-scaled twice before landing** — the intended heights (goblin 1.12 m,
Iron King 2.42 m) were not reached on either of the first two passes. The root node carried the scale factor in its **Y translation as well as its scale**, so
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
| `npc_innkeeper` | Adventurer (women pack), Quaternius | https://poly.pizza/m/ZwF0K7WBmu | 8,932 |
| `npc_guild_rep` | King, Quaternius | https://poly.pizza/m/I1gTjmuK2m | 11,594 |

> ⚠️ **The last two rows are the second pass.** The first pass shipped models nobody had looked
> at: `npc_guild_rep` was a **watch tower** — wooden legs, red roof, pennant, almost certainly from
> Quaternius's Ultimate Fantasy RTS pack, which is full of them — standing in for the **Village
> Elder** and the **Clan Chief**. `npc_innkeeper` was a bright green cartoon wizard from a
> "100 Avatars" pack (its extracted texture was still named `100Avatars_019_Wizzir`), rotated 90°
> so it faced sideways, standing in for **Innkeeper Holt**, the **Hearthkeeper** and the **Exile**.
> Both were picked on search-result metadata, never inspected, and both were **static meshes frozen
> in a T-pose** — so those five NPCs could never animate at all.
>
> Replaced with rigged CC0 characters from the same Quaternius families already in use, so they
> carry the standard `CharacterArmature|*` clip set and animate through the existing component.
> Rejected on sight, at eye level: `Worker` (hi-vis vest and hard hat), `Man` / `Man in Long
> Sleeves` (jeans, t-shirts, trainers), and `Witch` (CC BY, and the project takes no second
> attribution obligation).

- **Licence:** all CC0 1.0 Universal, each verified on its own model page. A CC-BY blacksmith was
  found and **deliberately passed over** — the project already carries one attribution obligation
  and a second buys nothing here.
- **Why selected:** the Adventurer and Rogue carry the richest combat clip sets found (Sword_Slash /
  Dagger_Attack, Run, HitRecieve, Death) and read as distinct silhouettes, so the companion does not
  look like a recolour of the player. The King reads as authority for the Elder and the Clan Chief;
  the women-pack Adventurer is muted leather and cloth, and gives the innkeeper roles a silhouette
  that is not a fourth copy of the Farmer. All five are rigged and all five now animate.

⚠️ **The earlier claim that "the two static townsfolk have no rig at all, which is correct — the
NPCs they replace are scene-placed props with no animation component" was the wrong conclusion
drawn from a real observation.** The scene NPCs genuinely had no animation component — but three of
them (`npc_vendor` ×3, `npc_kael`) had just been given **rigged** models by this same migration, so
they stood in bind pose. The fix was to add the driver, not to pick static models to match the
missing one. All eleven scene NPC mounts now carry a `CharacterAnimationComponent`.
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

### The Quaternius standardisation (2026-08-05) — CC0

The maintainer set a standing direction: **the art set standardises on Quaternius CC0 packs.** The
whole library is vendored at `assets/library/` (401 models, 10 bundles) behind a **`.gdignore`**, so
Godot never imports or exports any of it — a model enters the game only by being adapted and
exported into `assets/models/`. `assets/library/manifest.json` records every model's title, Poly
Pizza id, licence and download URL, so any of them can be re-pulled without a search.

| Bundle | Models | Source |
| --- | --- | --- |
| Medieval Village Pack | 39 | https://poly.pizza/bundle/Medieval-Village-Pack-NsHhjhlrfY |
| Ultimate Fantasy RTS | 107 | https://poly.pizza/bundle/Ultimate-Fantasy-RTS-nSDjmACoSU |
| Stylized Nature MegaKit | 68 | https://poly.pizza/bundle/Stylized-Nature-MegaKit-T34GZFA0fm |
| Ultimate RPG Items | 55 | https://poly.pizza/bundle/Ultimate-RPG-Items-Bundle-h8mhlZ0dG8 |
| Ultimate Monsters | 45 | https://poly.pizza/bundle/Ultimate-Monsters-Bundle-5oyGWAmOB6 |
| Survival Pack | 32 | https://poly.pizza/bundle/Survival-Pack-XzvQPP0yWB |
| Modular Dungeons Pack | 27 | https://poly.pizza/bundle/Modular-Dungeons-Pack-HaFPqhAp3w |
| Animated Animal Pack | 12 | https://poly.pizza/bundle/Animated-Animal-Pack-ILAPXeUYiS |
| Ultimate Modular Men | 11 | https://poly.pizza/bundle/Ultimate-Modular-Men-Pack-ZiH8muWqwQ |
| Ultimate Modular Women | 10 (5 taken) | https://poly.pizza/bundle/Ultimate-Modular-Women-Pack-aCBDXDdTNN |

- **Licence:** every vendored model is **CC0 1.0**, author **Quaternius**. No attribution is owed.
- ⚠️ **Five CC-BY 3.0 models in the Women bundle were deliberately not downloaded** (Sci Fi
  Character, Witch, Worker, Suit, Soldier). This project already carries one unmet CC-BY obligation
  (`prp_tome_stand`) that is a release blocker; adding more attribution debt to fix a *looks*
  problem is a bad trade, and the filter is by `licence` field in the manifest, not by eye.
- **The three packs the maintainer named** (Medieval Village MegaKit, Fantasy Props MegaKit,
  Modular Character Outfits Fantasy) are downloadable **only** through itch.io's click-through,
  which cannot be scripted. Poly Pizza mirrors the same artist's CC0 catalogue with no login, so the
  library was pulled from there instead — same author, same licence, adjacent packs.

### Ember Crown buildings — the Quaternius village set (CC0)

| Slot | Model | Source bundle | Faces | Size |
| --- | --- | --- | --- | --- |
| `bld_cottage` | Houses_SecondAge_1_Level1 | Ultimate Fantasy RTS | 2,336 | 4.84 × 4.20 × 4.70 m |
| `bld_inn` | Inn | Medieval Village Pack | 7,756 | 8.66 × 7.50 × 8.64 m |
| `bld_blacksmith` | Blacksmith | Medieval Village Pack | 7,659 | 8.42 × 6.50 × 7.11 m |

- **Licence:** CC0 1.0 Universal, Quaternius.
- **Blender MCP modifications:** joined, uniformly scaled to a target ridge height (cottage 4.2 m,
  inn 7.5 m, smithy 6.5 m), origin dropped to the **base centre**, mesh renamed `Mesh`.
- **Why a cottage was needed at all:** `bld_house_a` is 4.74 m wide and 7.50 m tall — a narrow
  two-storey towerhouse. LORE calls the holding a *cottage*, and the square was already using that
  one model at three mounts. `bld_cottage` is low, wide and single-storey, and the square now shows
  four distinct buildings instead of two used twice.
- ⚠️ **Rejected on the rear view: `House_4` from the Medieval Village Pack.** It is the obvious
  cottage from the front — low, wide, arched door — and its **back is open**: walls with no roof
  over the rear and a hollow interior. This is the same failure mode as the "roofs on stilts"
  incident below, caught this time because every candidate was rendered from **behind** as well as
  the front before selection. Four RTS huts/shacks (`hut`, `hut_2`, `shack`, `storage_hut`) were
  rejected the same way.

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
**visually compared side by side** rather than judged on its filename. **All four** remaining
in-house models are listed here; the last three were missing from this table until the
post-migration audit, and one of them had never been searched for at all.

| Model | What the searches actually returned | Why it stayed in-house |
| --- | --- | --- |
| `prp_brazier` | cooking spit, bonfire, two rock fire-pits, potion bottle, skull candle | Nothing was a brazier — a raised fire bowl on legs. The near-misses are all ground-level fires, which is a different silhouette and a different read in a lit town square. |
| `prp_glacier` | grey rocks, a cliff, a drinks can, an orange crystal | No ice. The closest geometric fit (`Environment_Cliff3`) is **19,960 faces** — more than the entire rest of the model set combined — and still reads as rock, not ice, which is wrong for Frostfang. |
| `prp_relic` | two swords, generic cubes | A divine relic is too project-specific to source; its silhouette is authored narrative, not a generic prop. |
| `enm_ashen_acolyte` | one promising "mage" | The mage candidate had **no skin and no animation clips at all**, so it could not drive `CharacterAnimationComponent`. No other rigged CC0 robed figure was found. The in-house model is the only remaining rig authored to this project's own clip vocabulary (`idle-loop`, `run-loop`, `cast`) and **the only actor in the game whose `cast` slot resolves** — replacing it with a sourced rig would cost the casting animation, not gain one. |

Re-open these if a winter/ice, dungeon-dressing, FPS-arms or robed-character pack lands that
covers them; do not force a bad match to raise the sourced-asset count.

---

## In-house assets

Authored from scratch via the Blender MCP; listed for provenance completeness, not because a
licence requires it. See `docs/SESSION_PLAYBOOK.md` Phase 30 for the authoring notes.

**This is the post-migration list — four models.** (An earlier revision of this table still listed
the whole Phase 30 output, including everything the migration had already replaced and the deleted
`enm_goblin_brute`. Corrected in the post-migration audit.)

| Class | Path | Phase |
| ----- | ---- | ----- |
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

## Second round — what a play-through caught that the audit did not

The audit above measured everything and read every scene, and still missed three things that were
obvious the moment the game was actually run. All three are now fixed.

1. **Every town NPC stood in a T-pose.** The scene-placed NPCs in `town_hub.tscn` and
   `clan_hold.tscn` are plain `Entity` nodes, and **none of them had a
   `CharacterAnimationComponent`** — so the rigged models the migration gave them had nothing
   driving the rig. Eleven mounts now carry one, pointed at `BodyMeshPath = "Model"`.
   `CharacterAnimationComponent.HorizontalSpeed()` also only read `CharacterBody3D.Velocity`, and a
   scene NPC is a bare `Node3D` that `ScheduleComponent` walks by writing `GlobalPosition` — so a
   townsperson would have slid to the market in an idle pose. It now differentiates position when
   there is no velocity to read.
2. **The Village Elder and the Clan Chief were a watch tower**, and **Innkeeper Holt, the
   Hearthkeeper and the Exile were a green cartoon wizard facing sideways.** Both models were picked
   on search metadata and never looked at. See the character table above.
3. **The sword was held by the pommel.** Matching the bounding box instead of the grip. See the
   sword entry above.

The common thread with the original migration's defects: **every one of them is something a
measurement passed and a look would have caught.** The bounding boxes were right, the clip lists
resolved, `--validate` was green, and the game still showed a watch tower called Village Elder.

## Third round — the Blender round-trip was corrupting bone-parented parts

A play-through reported goblins half underground, a glitched head on Kael, and a sword floating
in front of the hands. All three traced to two causes, and both invalidate measurements this file
previously reported as verified.

### Cause 1 — Blender's glTF round-trip loses a bone-parented child's placement

Two models carry a mesh that is **parented to a bone rather than skinned**: Kael's hair and eyes
(`NurbsPath.001`, on `Head`) and the Iron King's sword (`Knife`). Importing and re-exporting
either through Blender silently moves and resizes that child. Measured against the untouched
sources:

| | source (ratio of body height) | after round-trip | should have been |
| --- | --- | --- | --- |
| Kael's hair | 0.712 … 1.041 | **0.437 … 0.737** | straddling the skull |
| Iron King's sword | 0.167 … 0.721 | **0.397 … 0.451** (a 0.13 m stub) | 0.41 … 1.75 |

Kael's hair and eyeballs were rendering at chest height and the Iron King's sword had shrunk to a
nub. Both were **restored from the pristine `.glb`** and scaled by editing `RootNode` in the file
directly — see below. **Do not round-trip a rigged model through Blender unless you have to**, and
if you do, check every unskinned mesh against the source afterwards.

### Cause 2 — Blender's `Object.bound_box` reports the *undeformed* mesh

`bound_box` on an armature-modified object ignores the armature, so a model can measure correctly
in Blender and render somewhere else entirely. That is how two models passed the second round and
still shipped wrong:

| Model | shipped | should have been |
| --- | --- | --- |
| `chr_player_base` | **−4.932 … −3.193** — the whole body below the floor | 0 … 1.700 |
| `enm_goblin` | **−0.693 … 0.579** — sunk 0.69 m of a 1.12 m body | 0 … 1.117 |

The goblin is what the player saw as "half underground with just their heads visible". The player
body is first-person-invisible but is also `CompanionFactory.DefaultModelPath`, so a recruited
companion was walking around 5 m under the map.

### What replaced the measurement

`scripts` no longer measure in Blender. Every rigged model's span is now read **out of Godot**, by
skinning a sample of vertices by hand — `v_world = skeleton * bone_global_pose * bind_pose * v` —
because `MeshInstance3D.get_aabb()` on a skinned mesh returns bind space and is off by ~60×.
Corrections are then applied by editing `RootNode`'s `scale`/`translation` **in the `.glb` JSON**,
which cannot disturb skins, clips or bone-parented children the way a re-export can. All eight
rigged actors now measure exactly `0.000 … <target height>` post-import.

### The sword was floating in front of the fist

Not touching the hand at all — but only visible **side-on**. A straight-on viewmodel screenshot
shows a hilt in front of the fingers as though it were held. The offset is no longer hand-tuned:
`FirstPersonArmsComponent` now derives it from the grip point and hilt axis measured off the
Adventurer's own `Idle_Sword` pose (fist centre, and the tunnel axis the curled fingers make),
carried through the same fit the arm mesh went through.

The viewmodel also read undersized because the world FOV (75°) is far too wide for anything held
at arm's length. `ViewmodelFov` (default 55°) now scales the arms by the ratio of the half-angle
tangents at unchanged distance — the single-camera equivalent of a separate viewmodel camera.

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
