# Session 6 → Session 7: environment, rocks, vegetation and props

Session 6 is closed. It did **not** rebuild the vegetation system — that system is good and the
reasons are below. What it found instead was that the environment's real problems were a texture
pipeline paying for the same image six times, a metallic factor on 121 prop materials that
`ART_STYLE.md` forbids outright, and a rock/ice library with exactly one silhouette above 3.5 m.

---

## The three findings that mattered, in order of how much they cost

### 1. The nature props' file size was texture, and the same texture was in the build six times

`prp_clover.glb` was 2.5 MB. **2.47 MB of that was a 2048×2048 leaf atlas and 379 triangles was
the rest.** `prp_fern`, `prp_flowers_a` and `prp_flowers_b` each embedded a byte-identical copy of
that same atlas, so Godot imported four separate `ImageTexture`s of one image and the duplication
was paid twice — once on disk and once in VRAM.

⚠️ **And every prop `.import` carries `gltf/embedded_image_handling=1`, which means the importer
EXTRACTS each embedded image to a sidecar `.png` beside the GLB on every import.** An embedded
texture was therefore stored *twice on disk*, and deleting the sidecar did nothing at all, because
the next import wrote it back. This is why the 24 MB of `prp_*_<TexName>.png` files sitting in
`assets/models/props` looked like adoption residue and were not: they were importer output, and
the only way to end them was to externalise the image so the importer had nothing left to extract.

`tools/share_nature_textures.py` externalises every embedded image onto one canonical
`T_Nature_*.png`. **`assets/models/props` went from 84 MiB to 49 MiB and zero prop GLBs embed an
image any more.**

It rewrites glTF JSON and compacts the binary chunk, and refuses to write if any accessor's
**resolved bytes** change. ⚠️ The obvious version of that guard — compare the JSON before and
after — is wrong and fails a *correct* rewrite, because dropping a bufferView legitimately
renumbers every accessor that pointed past it.

### 2. 121 prop materials set 0.4 metallic with no metallic texture

Wood, hay, sacking, bark, leaves, cooking soup and fire. `docs/ART_STYLE.md` requires nonmetallic
plaster, wood and stone; the entire dressed world carried a faint sheen, which is the "excessive
gloss" the visual-QA checklist names.

⚠️ **0.4 survived this long because it is wrong in BOTH directions.** It is a compromise value:
too high for wood and too low for iron, so nothing looked obviously broken and everything looked
slightly off. Correcting only the wood would have left the brazier's ironwork and the relic's gold
reading as painted plastic.

`tools/repair_architecture_materials.py` (widened from the five buildings to the whole props
folder) now drives real metal **up** as well as everything else **down** — 138 materials across 49
GLBs. It skips any material with an authored `metallicRoughnessTexture`, because the twenty
shared-texture `.gltf` props carry real ORM maps and a factor would override them.

**All 31 `metallic-wood` / `metallic-stone` HIGH findings on props are gone from the audit.**

### 3. Nothing in fourteen vendored bundles is a rock above 3.5 m, and Frostfang's ice is one mesh

`manifest.json` searched for rock/boulder/cliff/ice/glacier returns `Rock_Medium_1..3`,
`RockPath_*`, and the 1/6-scale `rts` mountains (different kit, different style). The pack's
largest rock is `Rock_Medium_3` at 3.42 × 2.32 × 3.48 m.

⚠️ **`prp_rock_cluster.glb` is not a cluster.** It is a single 244-triangle `Rock_Medium_2` under a
plural name, and *both* of Frostfang's stone scatter layers scattered it. The name is why nobody
looked: a layer called "cluster" reads as though it already carries variety.

⚠️ **`prp_glacier.glb` was the whole of Frostfang's ice** — a 768-triangle block instanced fifteen
times across `glacier.tscn`, `ancient_aerie.tscn` and `dragon_roost.tscn`, told apart only by yaw.
Every placement was anisotropically scaled; `ancient_aerie`'s `GlacierNW` had basis column lengths
1.220 / 0.908 / 1.248 with the two horizontal columns 2.4° off perpendicular, so the transform
carried a small shear as well as a squash. The shear is mild — this was not a visibly skewed mesh
— but it existed only because one prop was being stretched to stand in for five different shapes.

---

## Assets deliberately retained, and why

- **The whole vegetation system.** `WorldBiomeScatter` already does MultiMesh instancing, a real
  HLOD tier that reuses the source mesh rather than a primitive, terrain gating by slope and
  altitude band, clumping, per-instance tint variation, slope leaning at 0.55, per-layer
  visibility ranges and shadow control. The species (`prp_grass_tall/short/wispy`, `prp_clover`,
  `prp_fern`, `prp_bush_flowering`, `prp_tree_broadleaf`, `prp_pine_dead`) are correct pack models
  at measured scale. **Nothing here needed replacing and none of it was replaced.** What it got
  was the texture consolidation and the material repair above, which are visible on every instance.
- **`prp_relic.glb`.** A gold chalice, 0.30 × 0.46 × 0.30 m. Its gold now reads as worked metal
  rather than yellow plastic (1.0 metallic / 0.30 roughness). Geometry retained — see the open
  question about its pedestal below.
- **`prp_boulder`, `prp_rock_cluster`, `prp_pebble_a/b`, `prp_rockpath_small/wide`.** All are pack
  models in the right style; they were kept and re-pointed at the shared atlas.
- **All background clutter and gameplay props.** Barrels, crates, sacks, tents, carts and the
  furniture set were left alone apart from the material repair.

---

## Assets adopted (container change only, no vertex touched)

`tools/adopt_kit_model.py`, from `nature_megakit`:

| new | source | measured |
| --- | --- | --- |
| `prp_rock_medium.glb` | `Rock_Medium_1` | 3.23 × 2.26 × 2.99 m |
| `prp_pebble_c.glb` | `Pebble_Round_3` | 0.45 × 0.10 × 0.48 m |
| `prp_pebble_d.glb` | `Pebble_Square_5` | 0.35 × 0.15 × 0.45 m |

The pack ships eleven pebbles and three medium rocks; two pebbles and two rocks were already
adopted (`prp_pebble_a` = `Pebble_Round_1`, `prp_pebble_b` = `Pebble_Square_2`, `prp_boulder` =
`Rock_Medium_3`, `prp_rock_cluster` = `Rock_Medium_2`). These three fill the spread without
padding a number.

## Assets created — `tools/build_environment_assets.py`

All flat-shaded, all seeded so a rebuild is byte-reproducible, all a **single material**, all with
their lowest vertex at exactly y = 0.

**Rock family — composed from pack meshes, not sculpted.** The UVs come along, so every piece
lands on the same `Rocks_Diffuse` atlas as the incumbents and the material family is shared for
free. Sculpting a cliff would have needed a new UV layout and a new texture, which is the "a stray
model from a fifth source reads as a mistake" trap arriving by the back door.

| asset | measured | tris |
| --- | --- | --- |
| `prp_boulder_large.glb` | 5.60 × 3.93 × 5.07 m | 1 108 |
| `prp_rock_cluster_a.glb` | 5.25 × 2.10 × 4.23 m | 1 172 |
| `prp_rock_scree.glb` | 6.22 × 1.44 × 5.12 m | 1 416 |
| `prp_rock_edging.glb` | 8.21 × 0.82 × 1.68 m | 1 758 |
| `prp_cliff_face.glb` | 10.89 × 6.01 × 5.43 m | 4 286 |
| `prp_cliff_face_tall.glb` | 10.01 × 6.99 × 5.51 m | 5 784 |

**Ice family — authored, because there is nothing to compose from.** One `Ice` material, no
texture at all, which is the choice the existing `bld_ice` made and what `ART_STYLE.md` §4 wants.

| asset | measured | tris |
| --- | --- | --- |
| `prp_ice_chunk.glb` | 1.30 × 0.95 × 1.10 m | 30 |
| `prp_ice_shard.glb` | 1.04 × 2.50 × 1.11 m | 24 |
| `prp_ice_slab.glb` | 4.40 × 0.55 × 4.00 m | 38 |
| `prp_glacier_wall.glb` | 8.00 × 6.40 × 3.20 m | 46 |
| `prp_glacier_face.glb` | 12.24 × 9.49 × 7.41 m | 114 |

**`prp_brazier.glb` was rebuilt in place.** The incumbent was 386 triangles of wire: 0.29 × 1.18 ×
0.45 m, and beside a 1.8 m human reference it read as a thin black scribble with a cone on top —
no bowl, no coals, no mass. It is placed in seven cells and is a `PlaceableTemplates` decor item.
The replacement is an eight-sided iron bowl with a rim, three splayed tapered legs, a tie ring, a
coal bed and a flame: **0.62 × 1.30 × 0.62 m, 126 triangles** — fewer than it replaced.
⚠️ It was overwritten **at its own path and near its own height on purpose**: seven cells each
have a hand-placed `OmniLight3D` at y = 1.3 just above the old flame, and the arena has a
`Shape_brazier` collider. A new file at a new path would have needed seven scene edits and moved
every one of those lights.

### Three defects that only a render found

Each of these had perfectly reasonable bounds, triangle counts and material counts.

1. **The first cliff was a 4×3 grid and rendered as a stack of loaves on a bakery shelf.** Every
   rock the same size, every course level, and the small position jitter left a visible horizontal
   gap between rows. Fixed by a much wider scale spread (0.55×–1.5× within one module), courses
   that **overlap** rather than stack, and tilt on two axes as well as yaw — with yaw only, the
   diagonal seam the shared atlas draws across each rock stayed parallel on all twelve.
2. **The first ice was a subdivided, jittered cube and produced a giant marshmallow.** Displacing
   a dense grid gives fine rounded noise; ice fractures, so it wants a few large flat planes
   meeting at sharp angles. Replaced with a convex hull, which guarantees planar faces and hard
   edges. ⚠️ The hull's first version sampled *directions* and scaled them by the half-extents,
   which samples an **ellipsoid** — the 8 × 3.2 × 6.4 m glacier wall came out as a giant white
   dice with nothing box-like left in it. Sampling the box **surface** fixed it.
3. **A convex hull is always smaller than the box it was sampled from**, by an amount that depends
   on the point count — the 8 m wall came out 5.65 m. The mesh is now normalised to the requested
   extent, so `size` means what it says.

---

## Materials and textures

- Five multi-user families collapsed to one file each: `T_Nature_Leaves.png` (4 users),
  `T_Nature_PathRocks.png` (4), `T_Nature_Grass.png` (3), `T_Nature_Flowers.png` (2),
  `T_Nature_LeafBroadleaf.png` (2). Every single-user image was externalised too, for the
  importer-extraction reason above.
- ⚠️ **The stone family shipped the same image at two resolutions and neither file said so.**
  `prp_boulder` embedded the megakit's 2048² `Rocks_Diffuse`; `prp_rock_cluster` embedded a 1024²
  downsample of it (verified: max channel delta 8/255 against a Lanczos reduction, so it is the
  same art). Two files meant two imported textures for one material family and 4× the VRAM on the
  boulder. **The whole stone family — eight assets — now shares one 1024² `T_Nature_Rocks.png`.**
- `tools/nature_texture_probe.gd` proves the sharing reached the *game* rather than just the file.
  ⚠️ A GLB whose image URI resolves to nothing still imports; the surface comes back with a null
  albedo, which in a scatter layer reads as "the grass went pale" rather than as an error.

---

## Instancing, LOD and scatter

⚠️ **The realm's entire stone cover was one pebble.** `Layer_stone` scattered `prp_pebble_a` at 210
instances per 100 × 100 m across **five of the six** Ember Crown biome profiles, and a MultiMesh
varies only yaw, uniform scale and tint — so every stone in the realm was the same 136-triangle
silhouette from a different angle, at 3.4 m spacing.

⚠️ **The fix splits the existing density; it does not add to it.** `MaxScatterInstancesPerCell` is
2400 and the layers already sat near it.

| region | before | after |
| --- | --- | --- |
| Ember Crown | `Layer_stone` 210 | `Layer_stone` 90 + `Layer_stone_b` 70 + `Layer_rock_medium` 26 + `Layer_boulder` 5 = **191** |
| Frostfang | `Layer_frost_rock` 120, `Layer_high_rock` 95 | 70 + 55, plus `Layer_frost_rock_b` 40 and the new ice tier |

`Layer_boulder` is an HLOD layer (reduction 2, 145 m → 340 m) for the reason a tree layer is, and
one more: a 5.6 m mass that pops out of existence at its visibility range is the most obvious cull
in the realm. `Layer_ice_shard` carries `MaxSlope = 0.32` — ice shoved upright belongs on the flat
of a snowfield, not canted out of a 40° corrie wall.

Frostfang gained a genuinely new ice tier (`Layer_ice_chunk` 55, `Layer_ice_shard` 12): it is a
glacial realm whose only ice was fifteen hand-placed copies of one prop, so the ground between
them had nothing frozen on it at all.

## Collision

`tools/replace_glacier_props.py` retired `prp_glacier.glb` from all three cells.

⚠️ **The routes through those cells are authored against these footprints.** `glacier.tscn` says
so explicitly: *"the route is the gaps BETWEEN these, not a corridor with these beside it."* So
each node keeps its **position and yaw** and gets a **uniform** scale chosen so the new asset's
horizontal footprint matches the area the old one covered. The anisotropy and the shear are gone.

Each node also got **its own collider**, replacing the single shared `Shape_glacier` box — a model
swap does not authorize reusing its predecessor's collision (`CLAUDE.md` §12; the `Shape_station`
and `Shape_boulder` incidents in `ASSET_POLICY.md` §0.5).

⚠️ **The collider's Y offset belongs to the model, not to the slot.** Every one of those shapes sat
at local y = 2.2, which is half `prp_glacier`'s 4.96 m height less its 0.28 m ground offset.
Carried onto a 6.4 m wall it centres the box a metre low, so the collision stands proud of the ice
at the bottom and stops short of it at the top — and the player walks into nothing, or through
something, depending which end they meet. Every new asset has its base at exactly y = 0, so the
centre is simply half the height.

**`traversal`, `layout`, `seams`, `scenes`, `stepup`, `meshes` and `building-collision` all pass.**

---

## Performance

`quality-performance/performance.json` — machine-sensitive report, not a hard gate. Intel Iris Xe
reference machine, against Session 5's numbers:

| | Session 5 | Session 6 |
| --- | --- | --- |
| Ember Crown mean draws | 876 | **903** (+3%) |
| Ember Crown mean ms | 18.97 | **16.49** |
| Ember Crown worst | 22.73 ms (Hollowreach) | **23.26 ms** (Wilds North) |
| Frostfang mean draws | 178 | **203** (+14%) |
| Frostfang mean ms | 12.76 | **14.34** |
| Frostfang worst | 18.87 ms (Clan Hold) | **19.61 ms** (Clan Hold) |
| Video memory | not recorded | 486 MB / 412 MB |

The draw increase is the expected, deliberate cost: splitting one stone layer into four is three
extra `MultiMeshInstance3D`s per cell that scatters stone, and Frostfang gained two ice layers on
top of that. Instance *counts* went down in both realms. Frame time is unchanged to better on
Ember Crown and about a millisecond worse on Frostfang.

⚠️ **Two cells exceed the authored `MaxDrawCalls = 1800`: Embermarket at 2204 and Hollowreach at
1975.** This is **not** caused by this pass — both are Session 5 settlement cells dense with
modular architecture, where the composed shells are 60 meshes each against a monolith's one (the
tradeoff `ASSET_POLICY.md` §0.6 records), and this session adds three draws to them. It is worth
Session 7's attention as a real budget overrun that no gate is currently failing on, because
`MaxDrawCalls` is checked by the authored-budget validator against node counts rather than against
a live frame.

⚠️ **The texture consolidation is a disk and import-time win, and should not be claimed as a VRAM
win without measuring it.** Godot decompresses to its own format on import, so four `ImageTexture`s
of one 2048² atlas becoming one is a real saving, but the recorded video-memory figures cannot be
compared to the 2026-08-30 historical baseline — that baseline predates Session 5's 43 MB of
shared architecture PBR maps, which dominate the difference.

## ⚠️ Unresolved: the world visual gate is nondeterministic, and fixing one bug exposed another

**This is the most important thing to carry forward and it is not caused by this session's content.**

`tools/world_shots.gd` never awaited `RenderingServer.frame_post_draw` before reading the viewport.
Twelve `process_frame`s look like ample patience, but `process_frame` is a *tree step*, not a drawn
frame — so `get_texture().get_image()` returned whatever the GPU had last resolved, frequently the
**previous shot**. On the reference machine that produced **92 duplicate frames out of 260**,
including whole cells captured as ten copies of one camera position. It poisoned both outputs at
once: the signature is computed from the same image, so a stale capture wrote a stale PNG *and*
recorded a stale signature into the baseline. Fixed here (one `await`, matching the three other
shot harnesses); distinct frames went 168/260 → 252/260 and the reported diffs fell from mean 66 to
mean 8.

⚠️ **But the stale frames had been accidentally stabilising the gate.** With captures genuinely
fresh, a second and deeper problem is visible: **the capture is not time-pinned**, so shader-driven
content (the fen's animated water specular above all) is sampled at a different phase every run.
The signature of a failing frame is diagnostic: **mean 0.4–8 against a threshold of 8, but `static
peak` 73–145 against a threshold of 70.** The images are all but identical and one small bright
highlight moves.

**The failing set therefore changes from run to run**, and re-merging chases it rather than fixing
it — `ash_approach` and `aerie_ascent` failed, then passed, then failed again across three
consecutive runs with no repository change in between. **The baseline was merged once, from
reviewed evidence, for the twelve cells whose content genuinely changed, and then deliberately left
alone.** A world visual-baseline change is reviewed evidence, not a way to silence a failing gate.

**Session 7 should pin the clock before capture** (freeze the world time-of-day and set
`Engine.time_scale = 0`, or capture at a fixed simulated timestamp) rather than merge again. It is
a small change in `world_shots.gd` and it is the difference between a gate that means something and
a gate that is ignored.

## Other unresolved issues

- **`prp_relic` has no pedestal.** It is a 0.46 m chalice standing on bare ground in the town hub
  square — ankle height on a passing player. The material is now right; the *context* is not, and
  a plinth plus a raised placement is a scene-dressing decision worth making deliberately rather
  than as a side effect of an asset pass.
- **`prp_cliff_face` is a boulder-choked rock face, not a sheer wall.** It reads correctly as a
  route-blocking landform at eye level and it tiles, but rounded pack boulders cannot make a
  vertical fractured cliff. A sheer wall needs the convex-hull treatment the ice family uses, which
  needs a UV layout for the stone atlas that does not exist yet.
- **`prp_rock_medium` sits 0.271 m below y = 0** (the pack authors it partly buried) while
  `prp_rock_cluster`, adapted from the same family, sits at exactly 0. The audit flags it HIGH as
  `ground-offset`. It is arguably correct for a rock and it is *inconsistent* within the family —
  decide which, then make both match. `prp_boulder` (−0.316) and `prp_glacier` (−0.282) are the
  same question.
- **Scrub scatters onto open water in `fen_edge`.** Bushes float on the fen surface. Pre-existing,
  unrelated to this pass, and it needs a water mask in the scatter planner rather than a tint.
- **Four `unresolved-provenance` findings** (was one): the composed assets have no
  `manifest.json` entry, which is correct — they are derived, not vendored — but the audit has no
  way to say so.
- **Blender's exporter re-embeds the atlas and resets material factors on every export.**
  `assets/models/props` went 49 MiB → 79 MiB mid-session that way and nothing failed.
  `tools/build_environment_assets.py` must always be followed by `share_nature_textures.py` and
  `repair_architecture_materials.py`; `share_nature_textures.py --check` exits non-zero if it was
  not, and is the cheap guard.

---

## Screenshots

`visual-qa/` — **133 frames, PASS.** Nineteen subjects × seven views (front, back, left, right,
front 3/4, rear 3/4 and **eye level**).

⚠️ **Every frame stands a 1.8 m human capsule beside the subject.** A rock has no intrinsic scale
and a render of one on a grey plane proves nothing — a boulder, a pebble and a cliff are the same
picture at different camera distances. This is exactly how the nature megakit came to ship 1.33 m
"short grass" and a 2.49 m "flower" without anyone noticing.

The three incumbents (`prp_glacier`, `prp_boulder`, `prp_rock_cluster`) are rendered in the same
frames and the same light, so "is the new one better" is a comparison rather than an assertion.
`visual-qa/summary.json` records measured bounds, base-Y grounding and surface count per subject.

World frames for the changed cells are under `tools/shots/world/` (disposable output) and the
localized diff evidence under `tools/shots/world_diffs/`.

## Validation

| Check | Result |
| --- | --- |
| Build | `dotnet build Embervale.sln` — **0 warnings, 0 errors** |
| Tests | `dotnet test tests/Embervale.Tests` — **1713 passing** |
| `--validate` | **PASS** |
| `gen_regions.py --check` | **PASS** — committed `.tres` match their specs |
| `world_quality_check.py --mode engine` | **all 19 gates PASS** |
| `world_quality_check.py --mode visual` | **FAIL, 1–4 frames of 260**, nondeterministic — see above |
| Environment shots | **PASS, 133/133 frames**, 19 subjects × 7 views |
| `nature_texture_probe.gd` | **PASS** — every family resolves to one shared texture in-engine |
| `share_nature_textures.py --check` | **clean** — no prop GLB embeds an image |
| Permanent 3D audit | **192 assets, 147 findings** (was 178 / 174); all 31 prop metallic HIGHs gone |
| `assets/models/props` | **84 MiB → 49 MiB** |

| Negative battery | **111/111** rules broken and restored, each caught by its own refusal |
| `--mode performance` | report generated; see the Performance section |

`negative_tests.py` and the performance report were run **after** the implementation commit, as
`AGENTS.md` requires — the negative battery refuses a dirty `data/` or `scenes/`.

## Git commits

- `dad4d09` — `Share the environment texture set and unshine the props`
- The implementation commit containing the rock/ice/brazier families, the scatter layers and the
  glacier replacement. Resolve it with
  `git log --oneline -- tools/build_environment_assets.py`.
- The handoff commit — the one containing this file.

---

## Session 7: read exactly these first

1. `AGENTS.md`
2. `docs/NOW.md`
3. `docs/ART_STYLE.md`
4. `docs/ASSET_POLICY.md` §0.1–§0.6
5. `docs/WORLD_AUTHORING.md`
6. `reports/3d/session-06-environment-handoff/README_NEXT_SESSION.md` (this file)
7. `reports/3d/session-06-environment-handoff/visual-qa/summary.json`
8. `reports/3d/session-06-environment-handoff/final-audit/prioritized-findings.md`
9. `tools/build_environment_assets.py` — its module docstring is the record of what was and was
   not authored, and why
10. `tools/share_nature_textures.py` — the texture contract every future prop adoption must meet

## Session 7 startup commands

Run from `C:\Users\magnu\Embervale` in PowerShell:

```powershell
git status --short
git log -4 --oneline --decorate
Get-Content reports/3d/session-06-environment-handoff/README_NEXT_SESSION.md

dotnet build Embervale.sln --no-restore
dotnet test tests/Embervale.Tests --no-build
& 'C:\Users\magnu\.cache\codex-runtimes\codex-primary-runtime\dependencies\python\python.exe' tools/negative_tests.py
& 'C:\Users\magnu\.cache\codex-runtimes\codex-primary-runtime\dependencies\python\python.exe' tools/world_quality_check.py --mode full
& 'C:\Users\magnu\.cache\codex-runtimes\codex-primary-runtime\dependencies\python\python.exe' tools/share_nature_textures.py --check
& 'C:\Users\magnu\Downloads\Godot_v4.7.1-stable_mono_win64\Godot_v4.7.1-stable_mono_win64\Godot_v4.7.1-stable_mono_win64_console.exe' --headless --path . --script res://tools/nature_texture_probe.gd
& 'C:\Users\magnu\.cache\codex-runtimes\codex-primary-runtime\dependencies\python\python.exe' tools/audit_3d.py --output reports/3d/session-07-baseline --render none
```

The global technical-art cleanup is Session 7's, and `docs/NOW.md` remains authoritative: the main
roadmap resumes at 42C. **Pin the visual gate's clock before doing anything else that changes a
cell** — every world-visual result until then is advisory.
