# Session 04 → Session 05 handoff — enemies, creatures and bosses

## Outcome

Session 4 reviewed all 31 authored enemy resources and all 33 production creature GLBs. The live
roster no longer uses the bull-as-boar, duplicate ghost/wizard bodies, ninja soldier, modern bandit,
punk enforcer, cartoon slimes, cactus Ash Maw, comic imp Crawler, invalid young-dragon imports or
generic golem silhouettes found at baseline. Useful human, animal, wolf, dragon and Iron King rigs
remain intact; inappropriate bodies were replaced or removed from live data.

The canonical KEEP / IMPROVE / KITBASH / REPLACE table is
`reports/3d/session-04-enemies-handoff/ENEMY_VISUAL_DECISIONS.md`.

## Models and production data changed

- Seven repository-authored Blender replacements: Cinder Wisp, Storm Mote, Rime Shard, Ash Maw,
  Ruin Crawler, Ward Golem and Stone Sentinel. Each has a small functional rig and idle, locomotion,
  attack, hit and death clips.
- `enemy_identity_kit.glb` supplies deterministic burial armour, spectral veils, ritual hides/bones,
  grounded faction equipment, wolf anatomy, dragon crowns/dorsals/chains and Iron King hero pieces.
- Sixteen enemy resources now use semantically appropriate proven foundations instead of their old
  stock body: Thornback Boar; Barrow Wight; Bone Knight; Hollow Husk; Clan Raider; Clan Shaman;
  Clan Beast Tamer; Hollow Necromancer; Soldier; Bandit; Syndicate Enforcer; Cultist; Cinder Thrall;
  Wild Dragon; Ash Dragon; Frost Drake.
- Wolf remains the good base animal. Dire Wolf and Frost Stalker are structurally differentiated.
- Wild, Ash, Frost and Ancient dragons retain the functioning 46-bone dragon foundation and receive
  distinct head, dorsal, chain, scale and material languages.
- The Iron King retains the useful 62-bone/24-clip humanoid rig but now has layered dark plate,
  crown, enlarged pauldrons/back plate, chains, damage/runes and a custom axe.
- All retained production creature GLBs received byte-safe glTF material correction. Mesh, skin,
  inverse-bind and animation binary payloads were not round-tripped for material-only repairs.
- `assets/library/manifest.json` records the new repository-authored assets and repairs.

## Duplicate enemies differentiated

- Barrow Wight is a physical armoured corpse; Grave Shade is a floating spectral veil and halo.
- Clan Shaman uses hides, bone mask, antlers and totem; Hollow Necromancer uses decayed robes, ribs,
  cowl and occult focus.
- Soldier, Bandit and Syndicate Enforcer now have different base bodies, head treatments, torso
  silhouettes and equipment rather than palette-only faction identity.
- Wolf, Dire Wolf and Frost Stalker now separate through shoulder mass, mane/fangs, dorsal ridge and
  skull treatment.

The permanent byte audit still lists two historical duplicate GLB pairs because those approved
source files remain in the library. No live enemy resource uses them; this is an archival duplicate,
not a production presentation duplicate.

## Blender work and rigs preserved

`tools/build_enemy_identity_assets.py` is the reproducible Blender 5.1/headless build. It generates
the identity kit and seven replacements, then performs byte-safe material patches. Do not re-export
the retained rigs through Blender merely to change a material.

`EnemyVisualKit` attaches rigid pieces through animated bone deltas while retaining authored axes.
This avoids the pack-specific bone-axis twisting previously seen with direct bone attachments. The
pieces are cosmetic and collision-free. Human, wolf, animal, dragon and Iron King skeletons remain
the source of motion. The replacement constructs use deliberately simple authored skeletons.

## Materials

Skin, fur, hide, cloth, bone, stone and dragon surfaces are non-metallic with restrained roughness.
Metallic response is reserved for iron, chains, armour and cores. Elemental glow is confined to
small cores/runes instead of full-body neon. The final audit's `metallic-stone` warning on Stone
Sentinel is caused by its mixed iron/core/stone asset-level maximum; the stone material itself is
not metallic.

## Animation, collision and hitbox warnings

- `AnimationClips` recognizes `HitReact` and `Idle_HitReact`; this is required for wolf/animal hit
  states and is covered by tests.
- Rigid accessories do not deform. Recheck clipping if future clips introduce extreme spine, wrist
  or jaw motion.
- The seven custom replacements provide the five gameplay-required clips, but their motion is
  intentionally compact. A future animation-polish pass can add secondary motion without changing
  their collision contract.
- Gameplay capsules, combat hitboxes, weapons and AI profiles were not resized or rearchitected.
  The live shot harness verifies every priority enemy has a valid capsule and required animation.
- Large dragons intentionally fill the close combat framing. Their full form is available in the
  Blender audit views; do not reduce gameplay scale merely to make the first-person QA image tidy.
- Godot's existing shot harness reports ObjectDB leak warnings at shutdown, and the world capture
  reports the existing two-edge navigation raster warning. Both complete successfully; neither is
  introduced by enemy assets.

## Unresolved visual issues

- Thornback uses the good wolf quadruped rig beneath a substantially changed boar silhouette. It now
  reads as a tusked plated boar, but it does not have a bespoke porcine joint layout.
- Source-pack topology remains under the deterministic kit for retained humanoids/dragons. Session 4
  removed the visible semantic errors without discarding proven animation foundations.
- Identity pieces are rigid bone followers, not cloth simulation or skinned secondary armour.
- Iron King phase gameplay already controls boss state; Session 4 did not start new phase architecture.
  His visual core/runes support future phase emphasis if that work is explicitly scheduled.

## Visual QA and screenshots

`reports/3d/session-04-enemies-handoff/visual-qa/README.md` indexes the final evidence:

- 230 live-world frames: 23 priority identities × six orthographic/three-quarter views plus idle,
  locomotion, attack, hit and death coverage;
- every frame uses the real enemy factory, production model, identity kit, gameplay scale/capsule and
  animation resolver in encounter context;
- labeled front-three-quarter and attack/death contact sheets;
- 275 baseline Blender views/poses under `baseline-visual-audit/renders/`.

The final `--enemy-shots` run passed **230/230**. Ground contact, player-relative scale, material
response, capsule presence, required clips and identity attachments were checked. Representative
front/rear/side/three-quarter and motion frames were manually inspected.

## Validation results

- Baseline static audit: 156 assets / 163 findings.
- Baseline Blender visual audit: 156 assets / 181 findings, 275 renders.
- Final permanent audit: 157 assets / 162 findings; Blender PASS; Godot imported-scene probe PASS.
- `tools/audit_3d.py --self-test`: PASS.
- `dotnet build Embervale.sln --no-restore`: PASS, 0 warnings, 0 errors.
- `dotnet test tests/Embervale.Tests --no-build`: PASS, 1,713/1,713.
- Godot `--validate`: PASS, all references resolve and graphs are reachable.
- `tools/world_quality_check.py --mode engine`: all 17 gates PASS, including melee and real-capsule traversal.
- `tools/world_quality_check.py --mode visual`: PASS, 260/260 frames after inspecting the two
  changed `fen_edge` shoreline frames and using the guarded baseline update command.
- `--enemy-shots`: PASS, 230 verified images. The final run reached live `Playing` state with every
  kit piece—including the Iron King weapon—present in the imported scene.

## Exact reports Session 05 must read

1. `reports/3d/session-04-enemies-handoff/README_NEXT_SESSION.md`
2. `reports/3d/session-04-enemies-handoff/ENEMY_VISUAL_DECISIONS.md`
3. `reports/3d/session-04-enemies-handoff/visual-qa/README.md`
4. `reports/3d/session-04-enemies-handoff/visual-qa/front-three-quarter-contact.png`
5. `reports/3d/session-04-enemies-handoff/visual-qa/attack-death-contact.png`
6. `reports/3d/session-04-enemies-handoff/final-audit/README.md`
7. `reports/3d/session-04-enemies-handoff/final-audit/prioritized-findings.md`
8. `reports/3d/session-04-enemies-handoff/final-audit/production-inventory.md`
9. `reports/3d/session-04-enemies-handoff/final-audit/materials-analysis.md`
10. `reports/3d/session-04-enemies-handoff/final-audit/scale-origin-analysis.md`
11. `reports/3d/session-04-enemies-handoff/final-audit/rig-animation-analysis.md`
12. `reports/3d/session-04-enemies-handoff/final-audit/collision-analysis.md`
13. `reports/3d/session-04-enemies-handoff/final-audit/duplicate-analysis.md`
14. `reports/3d/session-04-enemies-handoff/final-audit/texture-performance-analysis.md`
15. `reports/3d/session-04-enemies-handoff/final-audit/inventory.csv`
16. `reports/3d/session-04-enemies-handoff/final-audit/inventory.json`
17. `reports/3d/session-04-enemies-handoff/final-audit/findings.json`
18. `reports/3d/session-04-enemies-handoff/quality-engine/summary.json`
19. `reports/3d/session-04-enemies-handoff/quality-visual/summary.json`
20. `reports/3d/session-03-npc-handoff/README_NEXT_SESSION.md` for inherited modular-human constraints.

## Exact initial commands for Session 05

From `C:\Users\magnu\Embervale` in PowerShell:

```powershell
Get-Content reports/3d/session-04-enemies-handoff/README_NEXT_SESSION.md
Get-Content reports/3d/session-04-enemies-handoff/ENEMY_VISUAL_DECISIONS.md
Get-Content reports/3d/session-04-enemies-handoff/visual-qa/README.md
git status --short
git log -3 --oneline --decorate
dotnet build Embervale.sln --no-restore
dotnet test tests/Embervale.Tests --no-build
& 'C:\Users\magnu\Downloads\Godot_v4.7.1-stable_mono_win64\Godot_v4.7.1-stable_mono_win64\Godot_v4.7.1-stable_mono_win64_console.exe' --headless --path . -- --validate
& 'C:\Users\magnu\.cache\codex-runtimes\codex-primary-runtime\dependencies\python\python.exe' tools/audit_3d.py --output reports/3d/session-05-baseline --render none
```

To rebuild or revalidate the enemy work only:

```powershell
& 'C:\Program Files\Blender Foundation\Blender 5.1\blender.exe' --background --factory-startup --python tools/build_enemy_identity_assets.py -- 'C:\Users\magnu\Embervale'
& 'C:\Users\magnu\Downloads\Godot_v4.7.1-stable_mono_win64\Godot_v4.7.1-stable_mono_win64\Godot_v4.7.1-stable_mono_win64_console.exe' --headless --editor --path . --quit
& 'C:\Users\magnu\Downloads\Godot_v4.7.1-stable_mono_win64\Godot_v4.7.1-stable_mono_win64\Godot_v4.7.1-stable_mono_win64_console.exe' --path . -- --enemy-shots
```

## Git commits

- `99312a4` — `Overhaul enemy creature and boss identities` (implementation, assets, tests,
  baseline/final audits, 230-frame live QA, contact sheets and quality reports).
- Handoff commit — the commit containing this file. Resolve it with
  `git log -1 --oneline -- reports/3d/session-04-enemies-handoff/README_NEXT_SESSION.md`.

Both commits are on `main`. Session 4 did not begin architecture work.
