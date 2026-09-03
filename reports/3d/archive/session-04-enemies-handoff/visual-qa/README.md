# Session 04 enemy visual QA

`live/` contains 230 production-game screenshots: 23 priority identities multiplied by front,
front 3/4, left, rear, rear 3/4, right, locomotion, attack, hit and death. The harness creates each
subject through `EnemyArchetypeFactory`, uses its real model path, deterministic identity kit,
animation resolver and gameplay capsule, and frames it beside a 1.8 m player-height reference in
the live town world.

- `front-three-quarter-contact.png` is the labeled 23-identity silhouette overview.
- `attack-death-contact.png` pairs the 46 attack/death checks.
- `live/*.png` is the complete view/state evidence.
- The run completed **230/230**, with every production model, capsule, animation state and required
  identity attachment present.

The world HUD and first-person weapon are intentionally visible: these are encounter-context checks,
not isolated beauty renders. Large dragons fill or exceed the close combat frame by design; their
full silhouettes are also covered by the permanent Blender audit views.

