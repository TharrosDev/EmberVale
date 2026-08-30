## Phase 44.5 — World State: Realm Decay & Restoration `[F]`

> Realm-scoped story presentation, not a second corruption system. Story flags remain the cause;
> the realm-state service derives/caches only what consumers need. Atmosphere, weather, encounters
> and scene variants remain their domain authorities.

- [ ] **44.5A — Five-realm state vocabulary and persistence decision** `[F]`
  - **Goal:** define initial, post-Flamebearer and ending states.
  - **Build / Author:** per-realm table of driving flags, legal transitions and Pale Concord-specific
    meanings; choose derived-from-flags versus saved state, including migration/replace rules if saved.
  - **Verify:** transition tests, contradictory/missing flags, old-save defaults, five-realm coverage.
  - **Done when:** every outcome resolves deterministically and no two ledgers own it.

- [ ] **44.5B — Query/event seam and debug report** `[F]`
  - **Goal:** let content react without ad hoc flag polling.
  - **Build / Author:** one API/event, region-entry re-evaluation and dev-only report/override through
    the story-flag mutation choke point.
  - **Verify:** set/clear in active/inactive realm, load replacement, event once, invalid ids, override unsaved.
  - **Done when:** consumers use one traceable authority.

- [ ] **44.5C — Ember Crown proof state** `[F/C/P]`
  - **Goal:** prove a state is visible/playable, not merely logged.
  - **Build / Author:** post-Iron-King environment/weather, one bounded prop/actor variant and encounter
    weighting, using generated/spec-safe anchors and stable flags.
  - **Verify:** before/after day/dusk/weather captures, encounters, leave/return, save/load, perf delta.
  - **Done when:** a blind review identifies the change without HUD text and all hooks restore.

- [ ] **44.5D — Five post-boss realm states** `[C/P]`
  - **Goal:** give every defeat a fiction-specific regional consequence.
  - **Build / Author:** bounded atmosphere/weather/NPC/encounter/location variants per realm, including
    Pale Concord release/preservation; Phase 53 final-art handoff.
  - **Do not:** rebuild cells, swap geography/ids or erase empty wilderness.
  - **Verify:** five route captures, map stability, encounters/schedules, order permutations and budgets.
  - **Done when:** each realm changes distinctly and safely.

- [ ] **44.5E — Ending-wide atomic state and postgame decision** `[F/C]`
  - **Goal:** establish one complete world vector for each Phase 49 ending.
  - **Build / Author:** Dawnfire/Lord mappings, idempotent finalization, load-after-ending and Phase 53/65
    hooks. Phase 49 must explicitly decide post-ending free roam before implementation.
  - **Verify:** both endings, repeated apply, reload before/after, mixed prior states, old save/failed transition.
  - **Done when:** no ending can leave a half-applied realm vector.

---
