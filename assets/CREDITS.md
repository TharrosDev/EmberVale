# Asset credits & provenance

Required by [`docs/ASSET_POLICY.md`](../docs/ASSET_POLICY.md) §7 and
[`docs/ART_STYLE.md`](../docs/ART_STYLE.md) §6.3.

**Every sourced asset gets an entry here — including CC0 ones.** Attribution is not the only
reason to record provenance: six months on, "where did this come from and may we keep using
it?" must be answerable without archaeology. An asset with no entry is **not finished**.

---

## Current state

**16 of the project's 34 models are Kenney CC0; the other 18 are still in-house.** The asset
migration replaces the in-house set category by category (props first). Characters, creatures,
buildings, the weapon and five props with no suitable match are unchanged so far.

*(An earlier revision of this file, and PR #202's description, said 15. The count is 16 —
corrected here because this file is the provenance record.)*

Audio under `assets/audio/` is either CC0/open `.ogg`/`.wav` or `ProceduralAudio`
placeholders generated at runtime (see `CLAUDE.md` §8, "a new sound cue"); any CC0 audio file
added from here on gets an entry below too.

This section stops being true the moment the first sourced model lands. Replace it with the
entry.

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
