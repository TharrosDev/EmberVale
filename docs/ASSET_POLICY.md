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

## 0. Standing direction — three sourcing lanes (2026-10-06)

> **Maintainer instruction, and it overrides §1–§4 wherever it covers a model.** It supersedes the
> 2026-08-05 "the art set is Quaternius" rule and the 2026-09-03 "two lanes" rule for the outdoor
> world. Characters and the outdoor world are generated; the housing kit stays Quaternius on purpose.

### §0.1 The three lanes, and the order to reach for them

| Lane | Covers | Source |
| --- | --- | --- |
| **Cast** | characters, creatures, dragons, weapons | Meshy (rigged bodies, image or text route; the prompt stem is in [`docs/3D_ASSETS.md`](3D_ASSETS.md)) |
| **Outdoor world** | trees, rocks, ice, landmarks, ruins, towers, arches, tents, lamps, fences, carts, wells, banners, yard clutter | **Meshy text-to-3D** (`meshy-5`, preview 5 + texture 10 credits, short prompts, `target_polycount` set at preview, **never the image route**) in `assets/models/world/`, plus **two in-house procedural ground-cover meshes** (`tools/gen_ground_cover.py`: grass clump, fern rosette) |
| **Kept kit** | the modular housing and architecture kit, the five enterable buildings and 19 composed `bld_*` buildings, interiors, and a listed set of small interactive props | Quaternius CC0 MegaKits under `assets/library/`, **on purpose**: Meshy cannot make hollow, enterable buildings |

**The kept props** keep their measured colliders and are never mapped to a landmark: `prp_tome_stand`,
`prp_relic` (the town hub Relic), the crafting stations, `prp_bench`, `prp_cauldron`, `prp_timber_stack`,
the cache chest, the training dummy, the dock, jetty, fishing hut, gazebo and mine head, and the housing
decor named by bare file name in `src/Housing/PlaceableTemplates.cs` (display pillar, brazier, banner,
crate). `docs/3D_ASSETS.md` has the list with reasons.

**Search order, stop at the first that works:** (1) **inventory what already exists** —
`python tools/assets.py status`, `ls assets/models/<folder>/`, `assets/library/<pack>/manifest.json`,
and the ledger below — because the library has been "searched" from memory wrongly twice; (2) the lane
above: generate for the cast and the outdoor world, take from the pack for the kept kit; (3) the other
vendored bundles (`men/`, `women/`, `monsters/`, `animals/`, `rpg_items/`, `dungeons/`, `survival/`,
`nature/`, `rts/`, `medieval_village/`); (4) the open web (CC0/MIT only); (5) the Blender MCP, which is a
conversation with the maintainer (`CLAUDE.md` §2).

**Variety rule (owner):** two or three models per type, never a mesh per instance. Variety comes from
the scatter transform, scale and tint (`albedo_tint` on a wrapper, `Tint` on a scatter layer). A fourth
rock or tree needs a reason a different scale and tint cannot give.

**Landmarks are solid, walk-around set pieces.** Nothing generated is roofed or enterable; hollow
buildings stay the kept kit.

⚠️ **THE LEDGER IS THE LIBRARY, AND TASKS EXPIRE.** Meshy task ids and their download URLs lapse, so
a model that is not in `assets/models/` and not in the ledger is gone and costs credits again. Every new
generation is appended to `reports/3d/archive/meshy-migration/manifest.csv` (id, prompt, preview, refine
and rig task ids, date, credits) in the commit that adopts it; a rejected attempt is recorded in a
short note, never as an asset. `meshy_batch.py` keeps `state.json` with every task id the moment it is
created, so a re-run never pays twice; copy it into the ledger before the working folder goes.

## 1. The order of operations — never reversed

1. **Inventory first.** What is already in `assets/models/`, in the vendored library and in the ledger.
2. **Take the lane** (§0.1): generate cast and outdoor-world models, adapt a pack model for the kept kit.
3. **Search the web** for an open-source model only when the lane has nothing and the model is not
   generatable.
4. **Evaluate** (licence first, then fit) and **adapt it with the Blender MCP** if it is close.
5. **Create from scratch only** when a thorough search shows nothing suitable exists.

Creating from scratch is the **rare exception**, and reaching for it requires that *all four*
of these hold:

- an extensive web search was completed,
- no acceptable open-source asset exists,
- modifying an existing asset is impractical,
- combining multiple assets cannot solve the problem.

> **Do not assume a model does not exist.** "I couldn't think of one" is not a search.

Generated models are not "created from scratch" in this sense: generating is a lane, with its own
cost ledger and recipe (`docs/RECIPES.md` → *a new generated world model*), and it spends credits that
are capped (`meshy_batch.py` stops at a balance floor of 140).

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

- visual style (`ART_STYLE.md` §1 — grounded proportions; faceted low-poly for the kept kit, stylised-realistic otherwise)
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

Provenance for every generated model (cast and outdoor world) — prompts, task ids, per-model history — lives in
`reports/3d/archive/meshy-migration/manifest.csv`, deliberately out of the runtime manifest.

---

## 8. Style consistency

`ART_STYLE.md` remains the visual source of truth, with the 2026-10-06 split: the **generated cast and
outdoor world are stylised-realistic** (solid sculpted forms, muted painted textures, the §2 palette
written into every texture prompt), and the **kept housing kit stays faceted low-poly**. The maintainer
relaxed two of its clauses for the Phase 35 migration and this section follows them: **§3's triangle
bands are lifted** and **a sourced asset keeps its own materials** (see `ART_STYLE.md` §3 and §4). A
photo texture must still stop reading as a photo. **"No mixed kits" now applies only inside the kept
kit:** a model from a fifth source next to the housing kit still reads as a mistake, while the generated
outdoor world is a deliberate change of register that the §0.1 variety rule keeps coherent. The terrain
is unchanged: no ground textures.

The one clause of `ART_STYLE.md` §6.3 that this policy overrides is *"if adapting costs more
than modeling clean — model clean."* Adaptation is now preferred; modelling clean requires
the §1 four-part test.
