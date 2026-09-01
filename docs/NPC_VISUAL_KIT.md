# Embervale modular NPC visual kit

Session 3 replaces identity-by-recolour with a controlled cosmetic layer over the production
human rigs. The kit is intentionally small: one shared GLB, a deterministic profile table, and
no deformation of the source bodies.

## Canonical files

- `assets/models/equipment/npc_kit_embervale.glb` — 22 reusable rigid pieces.
- `src/Npc/NpcVisualKit.cs` — profession/faction/location profiles keyed by `Entity.TemplateId`.
- `tools/build_npc_kit.py` — reproducible Blender build plus material-only repair of the 11
  production NPC GLBs.
- `tools/audit_npc_population.py` — live scene/profile coverage and duplicate-combination audit.
- `tools/npc_kit_shots.gd` — production animation and dialogue-framing visual-QA harness.

## Attachment contract

The source body, Skeleton3D, BoneMap, skin, animation library and collision remain authoritative.
Kit pieces are rigid followers of `Chest` or `Hips`; they use the animated bone delta while
preserving the model's world axes. This is required because Embervale's retargeted human sources
do not share identical bone-local axes. Do not replace `NpcKitFollower` with a plain
`BoneAttachment3D` without repeating the complete motion review.

`Build.Slim`, `Standard`, and `Broad` alter cosmetic width only. They never scale a skeleton or
move vertices in a skinned body. A missing kit, profile, or bone degrades to the original body and
must not affect gameplay.

Each placed human uses a deterministic profile. Profiles are authored from profession, wealth,
faction, location, and story importance; there is no unconstrained random combination. Keep the
current four-piece ceiling unless a measured budget and visual review justify more. Head
silhouettes come from the proven production bodies—especially `npc_hooded`—because generated caps
and cowls failed Session 3 review and were removed rather than retained for numerical variety.

## Library and compatibility

The shared library contains outer vest, work apron, six faction tabards, merchant mantle,
asymmetric shoulder cape, belt pouches, satchel, coin pouch, keys, ledger, mug, rope coil, scroll
case, knife, hammer, quiver, and pauldron. Tabards encode guild membership; work tools stay with
their professions; merchant storage and account props stay with commercial roles. Armed-looking
pieces are limited to guards, hunters, travellers, clan roles, and other identities whose current
gameplay supports them.

The kit GLB owns a restrained common material palette. Cloth and skin use zero metallic response;
leather is non-metallic with medium-high roughness; only actual metal hardware has metallic
response. Existing human GLBs receive JSON material corrections only. Their binary geometry,
skins, inverse binds, animations, nodes, and accessors are not re-exported.

## Rebuild and validation

Run from the repository root:

```powershell
& 'C:\Program Files\Blender Foundation\Blender 5.1\blender.exe' --background --python tools/build_npc_kit.py
dotnet build
dotnet test
godot --path . --headless -- --validate
godot --path . --script res://tools/npc_kit_shots.gd
python tools/audit_npc_population.py
python tools/audit_3d.py --output reports/3d/session-03-npc-handoff/final-audit --render none
```

Review every important identity at front, rear, both sides, three-quarter, dialogue, walk, and run;
review Kael's armed pose too. Then run `--guild-shots` and inspect the actual settlement frames.
Never approve a rebuild from audit text alone.
