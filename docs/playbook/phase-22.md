## Phase 22 — Production Bible & Content Pipeline `[F/P]`

> Make authoring fast, safe, and consistent *before* there's a lot of content.
> Mostly tooling and docs — low engine risk, high leverage.

- [x] **22A — `docs/DESIGN.md`: combat & moment-to-moment pillars** `[P]`
  - **Goal:** pin the design the LORE leaves open, starting with combat.
  - **Tasks:** create `docs/DESIGN.md`; write the *combat pillars* (Skyrim breadth
    × Elden Ring weight, "no button mashing"), the core moment-to-moment loop
    (explore → fight → loot → grow), and the input/feel intent that Phase 29 will
    answer to. Cross-link CLAUDE.md combat sections.
  - **Done when:** `docs/DESIGN.md` exists with the Combat + Core Loop sections
    filled; no code touched.

- [x] **22B — `docs/DESIGN.md`: progression, difficulty & economy intent** `[P]`
  - **Goal:** finish the design bible's remaining pillars.
  - **Tasks:** add *Progression* (no class lock, player-authored builds, perk
    intent), *Difficulty philosophy* (easy to learn / hard to master, options not
    class locks), *Corruption fantasy* (sets up Phase 23), and *Economy intent*
    (gold sinks, scarcity in a dying world).
  - **Done when:** all five pillar sections complete; this is the document
    balancers/authors answer to.

- [x] **22C — ID & naming registry doc + audit** `[F/P]`
  - **Goal:** one documented namespace scheme for every content domain.
  - **Tasks:** locate the existing central id constants (PR #31). Write
    `docs/IDS.md` (or a section in DESIGN) documenting the scheme for `item.*`,
    `quest.*`, `npc.*`, `region.*`, `boss.*`, `faction.*`, `relic.*`, `dialogue.*`,
    `flag.*`, `spell.*`, `recipe.*`, etc. Audit current `data/**.tres` ids for
    conformance; list violators.
  - **Done when:** the scheme is documented and current ids are audited against it
    (a short conformance list in the doc).

- [x] **22D — `ContentValidator`: structural rules (no dead refs → well-formed)** `[F]`
  - **Goal:** grow validation from "references resolve" to "content is well-formed."
  - **Tasks:** in the `ContentValidator`, add checks: no duplicate ids per domain;
    loot tables non-empty; every quest objective `TargetId` resolves; every
    dialogue `Goto`/`StartNodeId` resolves. Read `src/Debugging/` for the existing
    validator shape first.
  - **Done when:** new rules implemented and surfaced; running `validate` reports
    the new classes of error.

- [x] **22E — `ContentValidator`: graph reachability (quests + dialogue)** `[F]`
  - **Goal:** catch unreachable content, the subtle content-scale bug.
  - **Tasks:** add reachability analysis — dialogue graphs have no orphan nodes
    and no dead ends that aren't intentional terminals; quest objective chains are
    completable; prerequisite quest chains don't cycle. Add a `validate-all`
    console command that runs the full battery.
  - **Done when:** `validate-all` exists and flags an intentionally-broken test
    graph; no false positives on current content.

- [x] **22F — Headless validation entry point** `[F]`
  - **Goal:** let the maintainer run validation without launching into gameplay.
  - **Tasks:** add a headless/`--validate` path (a Godot `--headless` script or a
    `GameState` boot branch) that loads the databases, runs `validate-all`, prints
    a report, and exits non-zero on failure. Document the invocation in CLAUDE.md
    §3 and the README.
  - **Done when:** a documented one-command content check exists (reviewed against
    the API; the human runs it).

- [x] **22G — `data/_templates/` canonical starting `.tres`** `[P]`
  - **Goal:** copy-paste starting points for every content type.
  - **Tasks:** create `data/_templates/` with one minimal, commented `.tres`
    exemplar per content domain already in docs/RECIPES.md (item, equippable, affix,
    loot table, perk, quest, dialogue, schedule, weather, encounter, world event,
    recipe, spell, status effect, faction). Each is the recipe's "canonical
    starting point."
  - **Done when:** every §8 domain has a template; `validate` stays green
    (templates either valid or excluded by an `_` convention).

- [x] **22H — Telemetry/analytics spine (dev-only)** `[F]`
  - **Goal:** lightweight event logging so balance/QA later have data.
  - **Tasks:** add `AnalyticsEvent : IGameEvent` and a dev-only `AnalyticsSink`
    that subscribes to the EventBus and logs to `user://analytics/` (deaths by
    location, quest start/complete, level-ups). Gated off by default in retail via
    a build/Settings flag. Implement `ISaveable` only if it must persist across
    sessions (it shouldn't — it's a log).
  - **Done when:** dev builds emit a structured analytics log; retail path is a
    no-op; documented in ARCHITECTURE.

---
