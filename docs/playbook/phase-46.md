## Phase 46 — Main Story, Act I: Awakening `[C]`

> **Entry:** clean New Game after G2. **Exit:** the player knows they are the Seventh Flamebearer,
> has a companion and corruption context, and receives a recoverable lead into Act II. Reuse Phase 33
> onboarding rather than authoring a second tutorial.

- [ ] **46A — Act I state graph and opening reconciliation** `[C/P]`
  - **Build / Author:** reconcile the shipped vertical-slice beats with LORE; map entry/exit flags,
    quests, locations, actors, cutscenes, companion/corruption transitions and fail/reload points.
    Mark reuse/replace/add without rewriting completed systems.
  - **Verify:** clean start and imported slice save; no duplicate tutorial/reward/flag.
  - **Done when:** every beat has one owner and one fallback edge.

- [ ] **46B — Awakening and Seventh Flamebearer reveal** `[C]`
  - **Build / Author:** opening quest/dialogue/cinematic chain, player-race variants where meaningful,
    first map objective and reveal consequence; never gate progress on optional dialogue.
  - **Verify:** all races, skip, subtitles, reload before/after reveal, actor unavailable.
  - **Done when:** reveal sets one authoritative flag and world/NPC/journal acknowledge it.

- [ ] **46C — Ancient forces hunt the player** `[C]`
  - **Build / Author:** escalating road/hub attack using existing encounters/Defend and a named pursuer
    clue; recovery if attackers spawned/killed early; civilian/faction aftermath and map cleanup.
  - **Verify:** flee/fight/fail/retry, companion absent, region transition, save during attack.
  - **Done when:** pressure is playable and cannot soft-lock the hub or next lead.

- [ ] **46D — Kael story recruitment** `[C]`
  - **Build / Author:** integrate existing Kael recruitment into the threat response, including decline,
    full party and later-recruit fallback; loyalty nudge only through `CompanionRoster`.
  - **Verify:** recruited already/declined/full party/Kael down, dialogue/cutscene with and without him.
  - **Done when:** Act I can finish with Kael recruited or a documented recoverable absence.

- [ ] **46E — Corruption seed and first power choice** `[C]`
  - **Build / Author:** teach accepted/refused Flamebearer power through Phase 23 UI/dialogue/vision,
    with attractive consequence and no false “pure path cannot win” implication.
  - **Verify:** accept/refuse, every existing tier through debug, skip/reload, duplicate reward prevention.
  - **Done when:** choice, power and corruption state agree across UI/dialogue/save.

- [ ] **46F — Act I convergence and Act II handoff** `[C/P]`
  - **Build / Author:** converge branches on leads to known realms/guilds without erasing choices; world
    reactions, quest cleanup, map/travel unlock and Act II entry flag.
  - **Verify:** branch matrix, out-of-order guild/boss state, save at unlock, no objective orphan.
  - **Done when:** every valid Act I state enters 47 with explicit carried flags and no placeholder.

---
