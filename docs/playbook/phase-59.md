## Phase 59 — Bug Triage, QA & Soak [P]

- [ ] **59A — QA taxonomy, database and release thresholds** [P]
  - **Build / Author:** Blocker progress/data/cert impossible; Critical crash/corruption/core broadly broken; High major
    path with workaround; Medium/local; Low/cosmetic. Require build, environment, repro rate, save/seed, logs/media,
    owner/fix version/regression. Set G4 zero Blocker/Critical and approved High threshold; reopen policy.
  - **Done when:** one triage authority and exit query exist.

- [ ] **59B — Region/platform/input functional matrix** [P]
  - **Verify:** five realms/Celestial; day/weather/world states; foot/mount/travel/water; supported hardware/platform/
    quality/input and accessibility personas; world-quality plus human routes.
  - **Done when:** every cell has pass/build/evidence and defects.

- [ ] **59C — Quest/story/faction/companion matrix** [P]
  - **Verify:** all main/side/guild/cult/companion quests, branches/failure/retry/order, both endings, flags/dialogue/
    cinematics/map/save transitions and unavailable-content explanations.
  - **Done when:** every content manifest row has at least one pass and branch-risk rows have all outcomes.

- [ ] **59D — System/regression/edge-case matrix** [F/P]
  - **Verify:** combat builds/AI/bosses/items/economy/crafting/housing/codex/UI/audio/settings/save; grow pure/in-engine
    tests and repro harnesses where deterministic; no test count vanity without risk mapping.
  - **Done when:** every Critical/High fix gains a regression at the lowest practical layer.

- [ ] **59E — Soak/longevity and crash reproduction** [P]
  - **Scenarios:** unattended hub/wilderness/combat where safe; repeated region loops, save/load/title cycles, inventory/
    UI churn, boss/VFX/projectile pools and multi-hour active play. Record duration, actions, memory and crash artifacts.
  - **Done when:** approved soak durations complete without leak/crash/state drift and every crash has symbols/log/repro status.

- [ ] **59F — Full regression cadence and blocker burn-down** [P]
  - **Build / Author:** smoke per change, risk suite daily/RC cadence, full matrix at candidate cuts; flaky tests are defects,
    quarantined only with owner/deadline. Burn Blocker/Critical to zero and review High individually.
  - **Done when:** exit query meets thresholds and no waived issue invalidates G3/G4 evidence.

- [ ] **59G — QA sign-off packet** [P]
  - **Build / Author:** build id/commit, matrix coverage, open defects/waivers, crash/soak, performance/save/localization/
    platform results, known limitations and rollback recommendation.
  - **Done when:** named owners sign and packet is reproducible from retained artifacts.

---
