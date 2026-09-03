# Session 03 → Session 04 handoff — modular human NPC population

## Responsibility and outcome

Session 3 owned civilian, worker, merchant/vendor, guild, guard/traveller, and named non-creature
human presentation. It did not begin the creature/enemy overhaul.

The live population now uses one reusable modular kit and 53 deterministic visual profiles instead
of relying on repeated stock bodies and conspicuous recolours. All 53 placed human identities have
a profile; no two retain the same base-model/profile combination. Profession, faction, location,
wealth, and story importance control combinations. Missing cosmetic data degrades to the original
body and cannot affect gameplay.

## Modular pieces created

`assets/models/equipment/npc_kit_embervale.glb` contains 22 rigid reusable meshes:

- outer vest and tapered work apron;
- Dawnwarden, Syndicate, Ash Hunter, Archive, Emberbound, and ochre civic tabards;
- merchant mantle and asymmetric shoulder cape;
- belt pouches, satchel, coin pouch, keys, ledger, mug, rope coil, and scroll case;
- knife, hammer, quiver, and pauldron.

The complete library is 567,788 bytes, 8,276 triangles, 12 shared materials, zero textures, zero
skins, and zero animation clips. Each controlled profile adds two to four meshes. Slim/standard/
broad presentation changes cosmetic width only; no skeleton is scaled or deformed.

## NPCs altered

The canonical identity-by-identity table is
`reports/3d/session-03-npc-handoff/npc-population-audit/placed-humans.csv`. It covers all 53 placed
humans across town hub, Embermarket, Crossway Post, Hollowreach, Wilds North, Emberdeep Mine, Tarn
Landing, and Clan Hold, using the 10 live production bases.

Distinctive important profiles include:

- Kael — asymmetric rust shoulder cape, pauldron, belt pouches, and knife;
- village elder — mantle, ochre civic tabard, and scroll case;
- innkeeper Holt — practical apron, keys, mug, and waist pouches;
- Aldreth/Bryn/Mirela and all Embermarket vendors — profession-specific merchant/work layers,
  storage, tools, ledgers, keys, rope, or coin props;
- Dawnwarden captain/armourer/serjeant, Archive keeper/reader/steward, Syndicate broker/fixer, Ash
  Hunter master/skinner/tracker, and Emberbound hierarch/warder/seeker — controlled faction tabards
  plus authority or profession markers;
- clan chief/quartermaster/beast tamer/hearthkeeper/exile, mine staff, landing workers, wardens,
  clerk, stablemaster, mercenary Wren, and traveller Hesk.

Existing good head silhouettes remain authoritative. A generated cap and cowl were built, reviewed,
and removed because they obscured faces or weakened the proven production heads. Do not restore
them merely to increase variation count.

## Rigs, animations, materials, and collision

- The 11 human production GLBs were not imported/re-exported through Blender. Their material JSON
  alone was repaired. `tools/verify_npc_rig_preservation.py --base f9d6a4a` proves their scene,
  node, mesh, accessor, skin, inverse-bind, animation, and binary payloads match pre-Session-3 Git.
- The 62-bone/24-clip families and Kael/dress 31-bone families remain unchanged. Existing BoneMaps,
  animation libraries, import scale—including `npc_woman_dress` at 0.384—and equipment/gameplay
  systems remain unchanged.
- `NpcKitFollower` follows the animated Chest/Hips delta while preserving model axes. A direct
  `BoneAttachment3D` pass twisted garments because the retargeted sources have different bone-local
  axes; do not simplify this without repeating the full motion review.
- Skin, cloth, leather, hair, and eyes are non-metallic with restrained roughness/color. Only actual
  metal hardware is metallic. The townsman's stock high-visibility yellow/vest was desaturated.
- Human capsules and all gameplay collision remain authoritative and unchanged. Cosmetic pieces
  intentionally have no collision.
- `npc_merchant_f.glb` is currently unused/retired but received the same material correction so a
  future reintroduction cannot restore metallic skin.

## Reproducible pipeline and compatibility rules

- `tools/build_npc_kit.py` — Blender/headless kit build and byte-safe material patch.
- `src/Npc/NpcVisualKit.cs` — exact TemplateId-to-profile compatibility table and attachment code.
- `tools/audit_npc_population.py` — parses actual region scenes and detects missing or identical
  base/profile combinations.
- `tools/verify_npc_rig_preservation.py` — Git-base structural/BIN comparison for production humans.
- `tools/npc_kit_shots.gd` — actual production model/profile/animation studio QA.
- `docs/NPC_VISUAL_KIT.md` — canonical extension, material, attachment, budget, and validation rules.

Do not use unconstrained random outfit selection. Tabards belong to their factions; tools belong to
their professions; armed-looking accessories belong only on roles whose current data supports them.
Keep the four-piece ceiling unless measured evidence and visual review justify increasing it.

## Visual QA performed

Final retained evidence is indexed at
`reports/3d/session-03-npc-handoff/visual-qa/README.md`:

- 128 studio frames across 18 representative combinations;
- front/rear/left/right/front-3q/rear-3q, dialogue, walk, and run for important identities;
- Kael armed pose;
- 12 final live-game frames at all five guild hubs and stranger/member dialogue framing;
- labeled front, rear, motion, and settlement contact sheets;
- guarded 260-frame full-world visual regression PASS.

The final images were inspected for clipping, attachment drift, broken weights, floating props,
T-pose, missing animation, scale/head mismatch, duplicate silhouette, material response, dialogue,
armed framing, and actual settlement readability. Rejected direct-bone-axis and generated-headwear
iterations were removed rather than shipped.

## Validation status

- Baseline 3D audit: 155 assets / 172 findings; retained under `baseline-audit/`.
- Final 3D audit: 156 assets / 163 findings; Blender PASS and Godot imported-scene probe PASS.
- Placed-human audit: 53 placed, 53 controlled profiles, 0 missing, 0 identical combinations.
- Rig/geometry preservation: PASS for all 11 production human GLBs against `f9d6a4a`.
- `dotnet build`: PASS, 0 warnings, 0 errors.
- `dotnet test --no-build`: PASS, 1,695/1,695.
- Godot `--validate`: PASS.
- Godot `--state`: PASS (2 regions, 26 cells, 48 dialogues, 31 schedules, 75 map locations).
- `tools/audit_3d.py --self-test`: PASS.
- `tools/world_quality_check.py --mode engine`: all 17 gates PASS.
- `tools/world_quality_check.py --mode visual`: PASS against the approved world baseline.
- `tools/world_quality_check.py --mode performance`: completed without failures. On the reference
  Intel Iris Xe run, Ember Crown averaged 17.29 ms and Frostfang 13.15 ms; this is machine-sensitive
  whole-world evidence, not an isolated NPC before/after benchmark.
- `--guild-shots`: exit 0, all 12 frames written, every officer exercised as stranger/member and
  leader state restored across load. It retained the existing shot-harness ObjectDB leak warning;
  no kit/import/animation error was emitted.
- The final guild run reached the live `Playing` state with the production kit imported.

## Known limitations and warnings

- Rigid layers follow chest/hips; they are deliberately not cloth simulation or skinned garments.
  Recheck extreme future clips. Current idle/walk/run and armed/dialogue poses pass.
- The final audit flags the attachment library's local Z extent as `ground-offset`; the kit is never
  floor-placed, so this is an origin heuristic, not a placement regression.
- The kit's 12 materials trigger the audit's medium `many-materials` heuristic. Six faction colors
  plus physical cloth/leather/metal/parchment/wood roles are intentionally shared in one GLB; the
  high `excessive-materials` finding was eliminated by consolidating the leather palette.
- Historical metallic-skin findings remain on `fp_arm.glb` and creature/enemy models. They are not
  live Session 3 NPC bodies; creature/enemy repair belongs to the later creature scope.
- Existing human source models still have pack-era topology/material splits. Session 3 removed
  physically wrong response and duplicate identity presentation without destabilizing proven rigs.
- Full-world performance differs from the 2026-08-30 historical machine report and includes all
  intervening project work. Use it as current telemetry, not causal proof about this kit alone.

## Canonical reports Session 04 must read

1. `reports/3d/session-03-npc-handoff/README_NEXT_SESSION.md`
2. `docs/NPC_VISUAL_KIT.md`
3. `reports/3d/session-03-npc-handoff/npc-population-audit/README.md`
4. `reports/3d/session-03-npc-handoff/npc-population-audit/placed-humans.csv`
5. `reports/3d/session-03-npc-handoff/rig-preservation/README.md`
6. `reports/3d/session-03-npc-handoff/visual-qa/README.md`
7. `reports/3d/session-03-npc-handoff/visual-qa/front-three-quarter-contact.png`
8. `reports/3d/session-03-npc-handoff/visual-qa/rear-three-quarter-contact.png`
9. `reports/3d/session-03-npc-handoff/visual-qa/motion-contact.png`
10. `reports/3d/session-03-npc-handoff/visual-qa/settlement-guilds-contact.png`
11. `reports/3d/session-03-npc-handoff/final-audit/README.md`
12. `reports/3d/session-03-npc-handoff/final-audit/prioritized-findings.md`
13. `reports/3d/session-03-npc-handoff/final-audit/production-inventory.md`
14. `reports/3d/session-03-npc-handoff/final-audit/materials-analysis.md`
15. `reports/3d/session-03-npc-handoff/final-audit/scale-origin-analysis.md`
16. `reports/3d/session-03-npc-handoff/final-audit/rig-animation-analysis.md`
17. `reports/3d/session-03-npc-handoff/final-audit/collision-analysis.md`
18. `reports/3d/session-03-npc-handoff/final-audit/duplicate-analysis.md`
19. `reports/3d/session-03-npc-handoff/final-audit/texture-performance-analysis.md`
20. `reports/3d/session-03-npc-handoff/quality-engine/summary.json`
21. `reports/3d/session-03-npc-handoff/quality-visual/summary.json`
22. `reports/3d/session-03-npc-handoff/quality-performance/performance.json`
23. `reports/3d/session-02-player-weapons-handoff/README_NEXT_SESSION.md` for inherited player rig,
    socket, and material constraints.

Machine-readable final inventory/findings are exactly:

- `reports/3d/session-03-npc-handoff/final-audit/inventory.csv`
- `reports/3d/session-03-npc-handoff/final-audit/inventory.json`
- `reports/3d/session-03-npc-handoff/final-audit/findings.json`

Provenance/licensing changes are exactly in `assets/library/manifest.json`. The frozen historical
`assets/CREDITS.md` was deliberately not edited.

## Exact commands Session 04 should run first

From `C:\Users\magnu\Embervale` in PowerShell:

```powershell
Get-Content reports/3d/session-03-npc-handoff/README_NEXT_SESSION.md
Get-Content docs/NPC_VISUAL_KIT.md
git status --short
git log -3 --oneline --decorate
dotnet build
dotnet test --no-build
& 'C:\Users\magnu\Downloads\Godot_v4.7.1-stable_mono_win64\Godot_v4.7.1-stable_mono_win64\Godot_v4.7.1-stable_mono_win64_console.exe' --path . --headless -- --validate
& 'C:\Users\magnu\.cache\codex-runtimes\codex-primary-runtime\dependencies\python\python.exe' tools/audit_npc_population.py --output reports/3d/session-04-baseline/npc-population
& 'C:\Users\magnu\.cache\codex-runtimes\codex-primary-runtime\dependencies\python\python.exe' tools/verify_npc_rig_preservation.py --base f9d6a4a --output reports/3d/session-04-baseline/npc-rig-preservation
& 'C:\Users\magnu\.cache\codex-runtimes\codex-primary-runtime\dependencies\python\python.exe' tools/audit_3d.py --output reports/3d/session-04-baseline --render none
```

To rebuild or visually revalidate this kit only:

```powershell
& 'C:\Program Files\Blender Foundation\Blender 5.1\blender.exe' --background --python tools/build_npc_kit.py
& 'C:\Users\magnu\Downloads\Godot_v4.7.1-stable_mono_win64\Godot_v4.7.1-stable_mono_win64\Godot_v4.7.1-stable_mono_win64_console.exe' --path . --script res://tools/npc_kit_shots.gd
& 'C:\Users\magnu\Downloads\Godot_v4.7.1-stable_mono_win64\Godot_v4.7.1-stable_mono_win64\Godot_v4.7.1-stable_mono_win64_console.exe' --path . -- --guild-shots
```

## Git commits

- `e65a76d` — `Build modular human NPC visual kit` (implementation, assets, tests, audits, and final
  visual evidence).
- Handoff commit — the commit containing this file. Resolve its exact hash with
  `git log -1 --oneline -- reports/3d/session-03-npc-handoff/README_NEXT_SESSION.md`.

Both commits are on `main`; no creature/enemy work is included.
