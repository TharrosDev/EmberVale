## Phase 57 — Performance & Memory Certification [P]

> Use existing profiler overlay, world_perf_probe, world_quality_check, mesh census and Phase 45 baselines.
> Numeric ship budgets are derived from approved target hardware, display/resolution/quality and measured captures.
> Until targets are approved, numbers are explicitly provisional—not fabricated commitments.

- [ ] **57A — Target hardware/quality matrix and measurement protocol** [P]
  - **Build / Author:** decide required platform/hardware tiers, resolution, quality, frame pacing target and cold/warm
    conditions; define capture duration/percentiles, tool versions and repeatability. Select representative/worst scenes.
  - **Done when:** a signed protocol separates observed baseline, provisional budget and ship target.

- [ ] **57B — Frame time/GPU/CPU scene certification** [P]
  - **Measure:** hub crowds, each realm wilderness, all boss/dragon types, cult battle, Celestial assault, cinematics,
    UI overlays and worst VFX using median/95th/99th frame time and stutter events.
  - **Fix:** profile-guided bottlenecks without deleting landmarks/readability.
  - **Done when:** every target tier meets approved frame/frame-pacing budget or has signed scope disposition.

- [ ] **57C — Draw calls, primitives, LOD/HLOD and shadows** [P]
  - **Measure/Fix:** per-realm/worst-cell draws/primitives, shadow casters, scatter tiers, character crowds, visibility
    transitions; compare mesh census/budgets and inspect visual regressions.
  - **Done when:** budgets pass and transitions preserve silhouette/readability.

- [ ] **57D — Memory/VRAM and longevity** [P]
  - **Measure/Fix:** boot, five region swaps, long traversal, inventory/codex, repeated bosses/VFX/projectiles,
    save/load and return-to-title; watch peaks/leaks, pooled nodes and asset residency.
  - **Done when:** RAM/VRAM ceilings on approved hardware hold and repeated cycles return to a stable baseline.

- [ ] **57E — Region build/load/traversal streaming hitches** [P]
  - **Measure/Fix:** cold/warm region build, threaded load, instantiation, save hard-load, portal/fast travel and
    mounted traversal. Region build occurs on loading screen but still needs an approved ceiling and progress UX.
  - **Done when:** all five load times/hitches meet targets and no transition risks watchdog/frozen presentation.

- [ ] **57F — Shader/audio/asset warm-up and build behavior** [P]
  - **Build / Author:** identify first-use shader compilation, audio decode and asset-load stalls; approved cache/
    prewarm strategy per platform, invalidation and build-size impact.
  - **Verify:** clean cache/install versus warm runs, every spell/VFX/realm/cinematic.
  - **Done when:** no first-use critical-path hitch violates frame/load targets.

- [ ] **57G — Performance regression gates and cert report** [P]
  - **Build / Author:** commit scenario baselines/tolerances where deterministic, reporting template and owner for
    hardware-only runs; rerun world quality/visual review after every optimization.
  - **Done when:** every scene×hardware row has evidence, zero unexplained breach and signed certification.

---
