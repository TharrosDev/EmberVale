## Phase 40.5 — Dungeon & Puzzle Framework `[F]`

> Ruins/temples/dragon-nests imply puzzles and traps; no phase before this builds the
> tooling. Lands before Phase 50 authors dungeons against it.

- [ ] **40.5A — `PuzzleComponent` + lever/pressure-plate primitive** `[F]`
  - **Done when:** a lever/plate puzzle gates a door/reward and is solvable + reset
    -safe.
- [ ] **40.5B — Sequence + light/shadow puzzle primitives** `[F]`
  - **Done when:** two more puzzle types exist on the same component family.
- [ ] **40.5C — Trap primitives (spikes/darts/collapsing floor)** `[F]`
  - **Done when:** trap hazards deal damage through the existing `DamagePacket`
    pipeline and are placeable as data.
- [ ] **40.5D — Relic-trial vault convention + one authored example** `[F/C]`
  - **Done when:** one vault (puzzle + guardian encounter) is authored end-to-end
    as the template Phase 51E's relics reuse.
- [ ] **40.5E — docs/RECIPES.md recipe + `ContentValidator` checks** `[F/P]`
  - **Done when:** "a new puzzle/trap" is documented and content is checked for
    solvability/dangling triggers.

---
