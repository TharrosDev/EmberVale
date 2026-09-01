# Session 3 NPC visual QA

## Retained final evidence

- `studio/` — 128 production-model frames across 18 representative identities. Important named,
  service, and guild NPCs have front, rear, left, right, front/rear three-quarter, dialogue, walk,
  and run views; Kael also has an armed pose. Five additional worker/merchant combinations retain
  front and rear three-quarter views.
- `front-three-quarter-contact.png`, `rear-three-quarter-contact.png`, and `motion-contact.png` —
  labeled review sheets assembled from those final frames.
- `settlement-guilds/` — 12 live game frames at the five actual guild hubs plus the Dawnwarden
  captain's stranger/member dialogue framing.
- `settlement-guilds-contact.png` — labeled live-world review sheet.
- `../quality-visual/summary.json` — guarded 260-frame world visual regression: PASS.

## Review result

PASS for the retained scope. Accessories remain attached through idle, walk, and run; silhouettes
read from front and rear; dialogue and armed framing remain usable; no T-pose, missing animation,
floating piece, gross clipping, or head/body mismatch was found in the final images. All five guild
colors remain restrained and distinct in context.

The rejected iterations are not retained as production assets: plain bone-local attachment twisted
rigid garments on retargeted skeletons, and generated cap/cowl pieces weakened or obscured existing
heads. `NpcKitFollower` fixed the axis problem; the poor headwear was removed. Existing proven hood,
hair, helmet, and head silhouettes remain authoritative.

The live guild harness also exercised every placed officer as stranger and member and restored the
pre-membership state across a wholesale load. It exited 0 and wrote all 12 frames. Its shutdown
reported Godot ObjectDB instances from the existing shot harness; build, content, scene, traversal,
visual-regression, and gameplay gates all pass and no attachment/import error was emitted.

## Performance interpretation

The complete kit is one 568 KB shared GLB with 22 meshes, 8,276 triangles, 12 shared materials, no
textures, no skin, and no animations. A controlled NPC profile instantiates two to four of those
meshes. The full world performance probe completed without failures; its measurements are retained
under `../quality-performance/`. They are machine-sensitive and must not be represented as an
isolated before/after NPC benchmark.
