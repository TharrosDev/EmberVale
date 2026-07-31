# Asset credits & provenance

Required by [`docs/ASSET_POLICY.md`](../docs/ASSET_POLICY.md) §7 and
[`docs/ART_STYLE.md`](../docs/ART_STYLE.md) §6.3.

**Every sourced asset gets an entry here — including CC0 ones.** Attribution is not the only
reason to record provenance: six months on, "where did this come from and may we keep using
it?" must be answerable without archaeology. An asset with no entry is **not finished**.

---

## Current state

**No third-party 3D assets are in use.** Every model under `assets/models/` was authored
in-house in Blender via the Blender MCP during Phase 30 (30B characters, 30D creatures, 30H
props/architecture), before the search-first policy was adopted. They carry no third-party
licence obligations.

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

_(none yet — first entry goes here)_

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
