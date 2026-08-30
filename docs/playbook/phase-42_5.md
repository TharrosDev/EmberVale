## Phase 42.5 — The Crimson Cult `[F/C]`

> **Dependencies:** 42A's state vocabulary, Phase 41 branching/failure, Phase 34 archetypes,
> faction standing and story flags. This is a hostile/infiltrable faction, not a sixth normal
> guild. Phase 44 places the full Sunspire footprint; Phase 47D consumes its terminal flags.

- [ ] **42.5A — Cult relationship and infiltration-state contract** `[F/C]`
  - **Goal:** express hostility, cover and exposure with existing state.
  - **Build / Author:** add `faction.crimson_cult`, relationships and flags for contacted/cover/
    exposed/turned/leadership outcome; define which actors read faction standing versus cover.
    Add a derived cult-state debug report and validator for contradictory flags.
  - **Do not:** add disguise equipment, suspicion meter or second reputation system.
  - **Verify:** transition tests; hostile→cover→exposed; clean defaults; save/load replacement.
  - **Done when:** every actor answers hostile, deceived, exposed or aligned from one state graph.

- [ ] **42.5B — Sunspire foothold and world-presence specification** `[C]`
  - **Goal:** make the Prophet's following physically credible.
  - **Build / Author:** define public mission/shrine, concealed outpost, patrol territory, supply
    route and Prophet approach; place current-region content and hand exact ids to Phase 44.
    Add environmental storytelling and schedules without filling transitional wilderness.
  - **Verify:** location authority, discovery, approaches, schedules, encounter filters, captures.
  - **Done when:** recruitment, logistics and coercive presence have canonical Sunspire owners.

- [ ] **42.5C — Zealot, inquisitor and convert encounter set** `[C]`
  - **Goal:** three readable social/combat roles using the Phase 34 matrix.
  - **Build / Author:** pressure zealot, caster/controller inquisitor and lightly armed convert;
    loot, bestiary and region-filtered encounters; only use the shipped Ash-variant pipeline.
  - **Do not:** create AI for a title; prove any missing role against the matrix first.
  - **Verify:** neutral under cover/hostile exposed, mixed readability, build counters and loot.
  - **Done when:** silhouette, telegraph and behavior distinguish all roles in both states.

- [ ] **42.5D — Contact, conversion test and cover branch** `[C]`
  - **Goal:** earn or reject cover through fiction and action.
  - **Build / Author:** contact lead, conversion dialogue and refuse/fake/embrace loyalty test using
    corruption, guild outcomes and existing Stealth/Interact objectives only where legible.
  - **Verify:** high/low corruption, Emberbound states, contact killed/early, fail/retry, branch reload.
  - **Done when:** every conversion outcome has an honest continuation or warned terminal result.

- [ ] **42.5E — Infiltration operation and exposure consequences** `[C]`
  - **Goal:** turn cover into a bounded operation with collateral choices.
  - **Build / Author:** infiltrate outpost, learn a route/weakness, then sabotage, warn victims,
    deepen cover or assault; sequential gated objectives and post-exposure patrol/access/dialogue.
  - **Verify:** site cleared early, alarm failure, objective-order abuse, actor death, unload/reload.
  - **Done when:** each result changes the world and produces one explicit 47D input.

- [ ] **42.5F — Prophet handoff and sequence-break campaign** `[C/P]`
  - **Goal:** make 47D consume every cult outcome without assuming a successful spy.
  - **Build / Author:** handoff matrix for untouched/refused/covered/exposed/turned/damaged leadership;
    fallback Prophet-arc entries; cult content cannot hard-lock Sunspire, boss or relic.
  - **Verify:** every state through debug and a real route, kills before/after cover, transition saves,
    `validate-all`, map and faction reports.
  - **Done when:** every state has tested consequence and main-story access remains recoverable.

---
