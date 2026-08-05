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

- **The library is vendored** at `assets/library/` — 401 CC0 models across 10 bundles, behind a
  **`.gdignore`** so Godot never imports or exports any of it. It costs the build nothing.
- **`assets/library/manifest.json`** records every model's title, Poly Pizza id, licence and
  download URL. Re-pull without searching; check a licence without guessing.
- **A model enters the game only by being adapted into `assets/models/`** (scaled, origin fixed,
  mesh renamed `Mesh`) and credited in `assets/CREDITS.md`. The library is source, not content.
- **Search is now scoped, not skipped.** §1's order still holds *inside* the library: look there
  first, and only go to the open web for something no bundle covers (today: an anvil/forge and an
  ice/glacier prop). Record any such exception in `CREDITS.md`.
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
