# Embervale — Art Style Guide (Phase 30A)

> **What this is.** The visual source of truth every model, texture, material, VFX
> and lighting decision answers to — the "dying world" identity made concrete
> enough that assets authored months apart still look like one game. Written ahead
> of the Phase 30 model/animation work; the Phase 53 art-complete pass deepens it,
> never contradicts it. Companion to `docs/UI_STYLE.md` (30.5A, forthcoming) and
> the LORE/DESIGN bibles.

---

## 1. The one-line direction

**Low-poly but detailed — Skyrim's grounded, weathered fantasy realism rendered in
clean, faceted geometry.** (Maintainer-pinned direction, 2026-07-01.)

Two references triangulate it:

- **Skyrim** for *what things are and how they feel*: muted Nordic-medieval
  material culture, weathered wood and cold iron, fur and leather, mist-wrapped
  peaks, believable proportions, nothing toy-like or chibi. Grounded, lived-in,
  slightly melancholy.
- **Low-poly stylization** for *how they're built*: economical faceted meshes,
  detail carried by **silhouette, material layering and lighting** — not by dense
  geometry or photo-real texture noise.

The synthesis: **"carved, not sculpted."** An Embervale asset should read like a
careful woodcut of a Skyrim asset — the same object, the same weight and wear,
expressed in fewer, more deliberate planes.

### 1.1 What "low-poly but detailed" means in practice

- **Realistic-ish, never blocky.** (Maintainer-pinned, 2026-07-01.) Organic
  subjects — bodies, faces, creatures, foliage — are **connected, smooth-shaded
  low-poly meshes with continuous silhouettes** (think PS2-era realism), *never*
  assemblies of separate boxes/capsules per limb. If a character reads as
  Roblox/Minecraft, it fails the style. Build organic forms as one mesh (skin
  modifier / sculpt-and-decimate), then decimate to budget.
- **Silhouette first.** Spend triangles where the outline reads (a sword's
  fuller, a tower's broken crenellation, a goblin's hunched spine); starve flat
  interiors. A good asset reads at 50 m from its silhouette alone.
- **Facets are a feature — on hard surfaces only.** Hard edges and visible
  planes are welcome on rock, armor plates, blades, architecture. Organic
  surfaces shade smooth with angle-based sharp edges (~60°) so the low-poly
  structure shows in the silhouette, not as flat-shaded tiling.
- **Detail lives in the material pass**, not the mesh: layered albedo wear
  (edge-worn metal, ash-dusted shoulders, damp hem), simple roughness variation,
  sparse emissive accents. Normal maps are allowed but subtle — bake the big
  forms, skip pore-level noise.
- **No photo textures.** Hand-painted or flat-shaded gradients with painted wear.
  PolyHaven/PBR sources are fine as a *base* only if downscaled and repainted/
  posterized until they read as authored, not photographed (see §6.3).
- **Believable proportions.** Human ≈ 7–7.5 heads, weapons at plausible scale,
  architecture human-scaled. Stylization lives in facets and paint, never in
  bobble-head or oversized-weapon tropes.

---

## 2. The dying-world language

Embervale is *beautiful but dying* (LORE). Every scene carries three layers:

1. **The faded world (base).** Desaturated, ash-greyed mids — colors remember
   what they were. Nothing is fully saturated except embers and magic.
2. **Ash (the sickness).** Grey-violet dust settled on horizontal surfaces,
   dead grass bands, bone-pale deadwood, char. The further from civilization/ley
   sites, the heavier the layer.
3. **Embers (the hope/threat accent).** Warm orange-gold emissive sparks — forge
   fires, lantern light, Flamebearer power, corruption's eye-glow. The *only*
   thing allowed to glow warm. Use sparingly; an ember accent should be findable
   in most vistas but never dominant.

**Master palette (hex, authoring anchors):**

| Role | Hex | Use |
| ---- | --- | --- |
| Ash grey | `#8a8578` | universal mid neutral, stone, dust |
| Faded earth | `#6b5d4a` | wood, leather, soil |
| Cold steel | `#7d8792` | iron, weapons, armor |
| Dead green | `#5c6b4f` | surviving vegetation (never lush) |
| Bone pale | `#c9c0ad` | deadwood, bone, plaster |
| Ember orange | `#e8722c` | THE accent — fire, forge, Flamebearer |
| Ember gold | `#d9a441` | candlelight, sun shafts, reward glint |
| Corruption violet | `#7a4d8c` | corruption, Necrotic, the Ash King's mark |
| Night blue | `#2b3547` | shadow mass, night ambient |

Saturation discipline: environment albedo stays under ~40% saturation; only
emissives (embers, spell VFX, corruption glow) may exceed it.

### 2.1 Per-realm grading (same language, four dialects)

| Realm | Base key | Light | Signature |
| ----- | -------- | ----- | --------- |
| **Ember Crown** | warm earth + ash grey | golden-hour amber, soft fog | candlelit hope: hearths, banners faded to rose |
| **Frostfang Reach** | cold steel + bone pale | hard white-blue, long shadows | wind-scoured ice facets, dark pine silhouettes |
| **Ashen Wilds** | ash grey + corruption violet | sickly diffuse, low contrast | drifting ash motes, ember fissures in char |
| **Sunspire Dominion** | bone pale + ember gold | high bleached sun, deep cool shade | sandstone monoliths, turquoise oasis accents |

### 2.2 Corruption's visual arc (the 23F/30I hook)

Player corruption tiers restate the world palette on the body: Untainted = none →
Touched = faint violet veining at wrists → Marked = ash-grey skin patches, dim
eye glow → Ashbound = charred vein network, ember-orange eye glow → Embers =
skin like banked coals, violet-orange rim light. Materials, not new meshes.

### 2.3 Magic schools (VFX tint law)

Spell/status VFX take their hue from `SpellSchools.Color` (already canonical in
code): Fire `#ff731f` · Frost `#73c7ff` · Lightning `#d9cc4d` · Arcane `#b366f2` ·
Nature `#66d973` · Necrotic `#8c4d8c`. VFX shapes follow the school identity
(Fire licks upward, Frost crystallizes in facets, Lightning arcs in hard polylines,
Arcane geometric glyphs, Nature growth curls, Necrotic sinks/drips).

---

## 3. Geometry budgets (triangles, per asset, LOD0)

Budgets serve the Steam-Deck/min-spec target (Phase 19/57). Facet-styled meshes
make these comfortable.

| Class | LOD0 budget | Notes |
| ----- | ----------- | ----- |
| Player character | 10–16k | + up to 4k per visible gear piece |
| Boss (Iron King class) | 15–25k | one per arena, earns the headroom |
| Humanoid enemy / NPC | 4–9k | goblin nearer 4k, key NPC nearer 9k |
| Creature (wolf → dragon) | 5–20k | scale with screen size |
| Weapon / handheld | 0.5–2k | silhouette detail (fuller, guard, wrap) |
| Hero prop (forge, waystone, portal) | 1–4k | landmark silhouettes |
| Common prop (crate, fence, rock) | 60–800 | instanced heavily |
| Building exterior (kit piece) | 1–6k | modular kit, §5.2 |
| Foliage card/cluster | 20–200 | alpha-cut cards, faceted trunks |

**LOD rule of thumb:** LOD1 ≈ 45% of LOD0 at ~25 m; LOD2 ≈ 15% at ~60 m;
imposter/cull by 120 m (props) / 300 m (buildings). Author LOD0 clean and let
Godot's importer generate LODs (it does this automatically from the glTF) unless
the silhouette collapses — only then hand-author an LOD.

---

## 4. Materials & texturing

- **Workflow:** Godot `StandardMaterial3D` (Forward+), albedo + roughness (+
  metallic where true metal) (+ emission for embers/magic). Normal maps optional
  and subtle. No height/parallax; no detail maps.
- **Texel budget:** 512² covers most props; 1024² for characters/hero props;
  2048² only for the player and bosses. Trim sheets encouraged for architecture.
- **Painted wear, not noise:** edge highlights, ash settling on up-facing
  surfaces (a top-down gradient or vertex-color mask), soot around openings.
- **Roughness bands, not maps** where possible: cloth 0.9, wood 0.8, stone 0.75,
  leather 0.6, worn iron 0.45, polished steel 0.3.
- **Vertex color** is the cheap detail channel: bake ash-dusting, moss, and char
  masks into vertex colors on environment meshes and blend two albedo tones in a
  shared shader — one material serves a whole kit.
- Flat-shaded (single-color-per-facet) is acceptable for distant filler and
  small props; painted-gradient texturing for anything the camera meets.

---

## 5. World-building conventions

### 5.1 Scale & grid

`1 Godot unit = 1 metre`. Door openings 2.2 m; storey height 3 m; the modular
architecture kit snaps to a **0.5 m grid** (navmesh voxels are 0.25 — keep
walkables on-grid, CLAUDE.md §8 "new region").

### 5.2 Modular kits

Buildings and dungeons are kits (wall, corner, door, window, roof, trim), not
monoliths — kit pieces at local origin with the snap point at the floor-corner,
+Y up, −Z facing "out". Set dressing = kit + prop instances + one or two bespoke
hero meshes per POI.

### 5.3 Atmosphere carries the mood

Fog is a material of the world (Skyrim's trick): every region runs distance fog
tinted to its palette key (the `WeatherResource` fields already drive this) plus
`WorldEnvironment` glow tuned so *only* emissives bloom. Godot volumetric fog is
budgeted for interiors/valleys only (min-spec).

---

## 6. Pipeline (Blender MCP → glTF → Godot)

### 6.1 Authoring & export

- Model in Blender (via the Blender MCP), apply all transforms, real-world scale.
- Export **glTF Binary (`.glb`)** into `assets/models/<class>/<name>.glb`
  (`class` = `characters` / `creatures` / `weapons` / `props` / `architecture` /
  `foliage`). Textures embedded, or beside the mesh under `assets/textures/`.
- Naming: `snake_case`, prefixed by class where ambiguous (`prop_crate_small`,
  `chr_player_base`). One asset = one file; kits may share a file with clearly
  named meshes.
- Rigs: humanoids on the standard Godot humanoid skeleton naming so retargeting
  works; keep bone counts ≤ 60 for non-player, ≤ 90 player. Animations in the
  same glb (or a shared library glb per skeleton).

### 6.2 Import (Godot side)

Default importer settings, plus: generate LODs on; static props import as
`StaticBody3D`-ready scenes with **author-time collision** (`-col`/`-convcol`
suffixes in Blender, or a simple collider added in the factory/scene — never
runtime-parsed visual-mesh collision, per the navmesh rule in CLAUDE.md §8).

### 6.3 Sourced assets (PolyHaven / Sketchfab / generated)

Open-license sources are allowed as *raw material*, never dropped in verbatim:
retopo/decimate to the §3 budget, repaint/posterize textures to §4 (a photo
texture must stop reading as a photo), re-tint into the §2 palette. Record
source + license in `assets/CREDITS.md` (CC-BY requires the entry; CC0 gets one
anyway for provenance). If adapting costs more than modeling clean — model clean.

---

## 7. Quick review checklist (every asset)

1. Silhouette reads at distance; facets deliberate, not accidental.
2. Grounded Skyrim-plausible proportions and material culture.
3. Albedo inside the palette; saturation < 40% unless emissive.
4. Ash layer present where the world would have settled it.
5. At most one warm ember accent; it glows, nothing else does.
6. Within the §3 triangle budget and §4 texel budget.
7. Exported per §6, credited if sourced.

> **Status:** written Phase 30A. Owners: 30B–30I author against it; Phase 53
> (Art Complete) may extend §2.1 per-realm and §3 budgets with playtest data,
> recording changes here.
