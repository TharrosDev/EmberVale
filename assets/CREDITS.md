# Asset credits & provenance

Required by [`docs/ASSET_POLICY.md`](../docs/ASSET_POLICY.md) §7 and
[`docs/ART_STYLE.md`](../docs/ART_STYLE.md) §6.3.

**Every sourced asset gets an entry here — including CC0 ones.** Attribution is not the only
reason to record provenance: six months on, "where did this come from and may we keep using
it?" must be answerable without archaeology. An asset with no entry is **not finished**.

---

## Current state

**20 of the project's 33 models are sourced; the other 13 are still in-house.** The asset
migration replaces the in-house set category by category. Every static prop that has a suitable
open-source match has now been replaced. Characters, creatures, buildings, the weapon and three
props with **no suitable match found** (see *Searched for, not replaced*) are unchanged.

*(An earlier revision of this file, and PR #202's description, said 15 props. The prop count was
16 — corrected here because this file is the provenance record. The model total dropped 34 → 33
when the orphaned `enm_goblin_brute.glb` was deleted.)*

⚠️ **One asset requires attribution.** `prp_tome_stand` is CC BY 3.0, not CC0 — see its entry.
That obligation ships with the game and must survive any future asset cull.

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

**Blender MCP modifications (all 15):** imported glTF → joined multi-part kit pieces into a single
mesh (parented children otherwise compound their parent's scale — this silently produced a 10 m
anvil on the first pass) → scaled to the in-house model's footprint → applied transforms → origin
dropped to the base → mesh renamed `Mesh` → exported GLB over the original filename.

Kenney kits are authored at roughly **1/4 scale** (their crate is 0.25 m; ours is 1.24 m), so every
asset needed a 1.9×–6.5× scale-up. Five needed **per-axis** rather than uniform scaling because the
source aspect ratio differed from the model the scenes were built around — `prp_arena_wall` is a
long low wall where Kenney's `wall-block` is a cube, and `prp_ruin_wall` also needed a 90° yaw.
Every replacement matches its predecessor's bounding box, so no scene transform changed.

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

**Blender MCP modifications (both):** scaled the **root node only** to the in-house model's height
(goblin 1.12 m, Iron King 2.42 m), origin dropped to the feet, exported with skins and animations.

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

---

## Searched for, not replaced

Two full search rounds — Kenney (Fantasy Town, Survival, Modular Dungeon, Modular Cave), Poly
Pizza across ~10 query terms, with every shortlisted candidate imported into Blender and
**visually compared side by side** rather than judged on its filename. These three keep their
in-house models:

| Model | What the searches actually returned | Why rejected |
| --- | --- | --- |
| `prp_brazier` | cooking spit, bonfire, two rock fire-pits, potion bottle, skull candle | Nothing was a brazier — a raised fire bowl on legs. The near-misses are all ground-level fires, which is a different silhouette and a different read in a lit town square. |
| `prp_glacier` | grey rocks, a cliff, a drinks can, an orange crystal | No ice. The closest geometric fit (`Environment_Cliff3`) is **19,960 faces** — more than the entire rest of the model set combined — and still reads as rock, not ice, which is wrong for Frostfang. |
| `prp_relic` | two swords, generic cubes | A divine relic is too project-specific to source; its silhouette is authored narrative, not a generic prop. |

Re-open these only if a winter/ice or dungeon-dressing pack lands that covers them; do not force
a bad match to raise the sourced-asset count.

---

## In-house assets

Authored from scratch via the Blender MCP; listed for provenance completeness, not because a
licence requires it. See `docs/SESSION_PLAYBOOK.md` Phase 30 for the authoring notes.

| Class | Path | Phase |
| ----- | ---- | ----- |
| Characters | `assets/models/characters/` (player base, first-person arm, Kael, vendor, innkeeper, guild rep) | 30B / 30D |
| Creatures | `assets/models/creatures/` (goblin, goblin brute, Ashen acolyte, Iron King) | 30D |
| Weapons | `assets/models/weapons/wpn_sword_iron.glb` | 30B |
| Props | `assets/models/props/` (crate, campfire, brazier, tent, waystone, tome stand, training dummy, cache chest ×2, crafting stations ×3, relic, banner, lamp post, rock cluster, dead pine, glacier, ruin wall, arena wall) | 30H |
| Architecture | `assets/models/architecture/` (house A, house B) | 30H |
