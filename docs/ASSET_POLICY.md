# Asset Acquisition Policy — 3D models

> **Authority.** This document governs **every 3D asset** that enters the project. It is
> mandatory, and it **supersedes** the prior build-from-scratch defaults in `CLAUDE.md` §1
> and `ART_STYLE.md` §6.3 wherever they disagree. Set as a standing maintainer policy after
> Phase 35; recorded here because a policy that lives only in a chat session governs nothing.
>
> **What changed.** Through Phase 30 every model in `assets/models/` was authored from
> scratch in Blender via the MCP. That is no longer the default. **Search first, adapt
> second, create last.**

> ⚠️ **This document covers SOURCING, LICENCE and ART DIRECTION only.** Everything about the
> pipeline itself — rig families, retargeting, BoneMaps, `.import` configuration, adoption,
> materials, collision, validation, weapon and viewmodel conventions — lives in
> **[`docs/3D_ASSETS.md`](3D_ASSETS.md)**, which is the operational contract. Read that one to do
> the work; read this one to decide where a model should come from.

---

## 0. Standing direction — the art set is Quaternius (2026-08-05)

> **Maintainer instruction, and it overrides §1–§4 for anything a vendored pack already covers.**
> Embervale's art set **standardises on Quaternius CC0 packs**. The point is coherence: one artist,
> one style, one skeleton. A slightly better model from elsewhere is the *wrong* answer.

### §0.1 The four packs, and the order to reach for them (2026-08-08)

⚠️ **THERE ARE TWO LANES, AND THIS SECTION IS ONE OF THEM** (maintainer direction, 2026-09-03).

**Characters and creatures are generated with Meshy**, semi-realistic, and do not come from the
packs at all — the cast is custom generations and the prompt stem is in
[`docs/3D_ASSETS.md`](3D_ASSETS.md). The order below governs **props, architecture and nature**.

**For those, four Quaternius MegaKits are the art set.** The near-entirety of the world is built
from them, and the search order is fixed: **the four packs → the other vendored bundles → the open
web → Blender MCP.** Stop at the first that works.

| Bundle | Covers | Models |
| --- | --- | --- |
| `medieval_megakit/` | modular architecture: walls, roofs, doors, windows, shutters, floors, stairs, balconies, overhangs, chimneys | 176 |
| `medieval_interiors/` | interiors and props: beds, cabinets, bookcases, shelves, tables, chairs, chests, anvil, workbench, market stalls | 94 |
| `nature_megakit/` | trees (common/pine/dead/twisted), bushes, ferns, grass, clover, flowers, mushrooms, pebbles, rocks, rock paths | 68 |
| `animations/` | `AnimationLibrary_Godot_Standard.glb` — 46 clips on one shared skeleton | 1 |

⚠️ **What the four packs do NOT cover: characters, creatures and weapons.** Characters and
creatures are now generated (the other lane, above). Weapons and the remaining odds come from the
older vendored bundles (`men/`, `women/`, `monsters/`, `animals/`, `rpg_items/`), which is why
step 2 exists and is not optional.

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

⚠️ **It is not connected by default** (maintainer direction, 2026-08-10): the `uvx blender-mcp` entry
was removed from the user-level `~/.claude.json`, so `mcp__blender__*` does not appear in a session's
tool list at all and re-adding it needs a command **and** a Claude Code restart **and** a Blender with
the add-on connected. `CLAUDE.md` §2 carries all three. **An absent tool list here is the intended
state, not a fault** — reach for the vendored library instead, and treat needing this section as a
conversation with the maintainer.

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

## 7. Documentation — the manifest, not CREDITS

⚠️ **`assets/CREDITS.md` is FROZEN as history** (maintainer direction, 2026-08-08). Do not add
entries to it and do not treat a missing entry as unfinished work. This build is personal, never
published and never sold, and every asset in it is CC0, so no attribution was ever legally owed.
Read it for the traps it records; that is all it is for now.

What replaced it is two machine-readable files, neither of them hand-maintained:

- **`assets/models/manifest.json`** — the production manifest. Derived from the files on disk by
  `python tools/assets.py status --write`. Runtime truth only: id, path, rig family, animation
  profile, bone map, root scale, references.
- **`assets/library/manifest.json`** — the source-library index. What is vendored, and its licence.
  It stays because it is an *index* rather than a credit: it is what makes searching the library
  cost one `grep`.

Provenance for the generated cast — prompts, task ids, per-model history — lives in
`reports/3d/archive/meshy-migration/manifest.csv`, deliberately out of the runtime manifest.

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

