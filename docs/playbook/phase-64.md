## Phase 64 — Launch Response & Stabilization [P]

- [ ] **64A — Live intake, telemetry and severity command center** [P]
  - **Build / Author:** unify crash reports, tickets, store/community reports and privacy-approved telemetry; deduplicate,
    correlate build/hardware/save and apply 59A severity. Publish internal cadence/owners.
  - **Done when:** every urgent signal has owner/repro/status and no channel is unmonitored.

- [ ] **64B — Hotfix criteria, validation and rollback** [P]
  - **Hotfix:** Blocker/Critical crash/data-loss/progression/cert/security or narrowly severe issue; otherwise batch.
    Require minimal diff, regression, save/migration compatibility, target smoke and rollback artifact.
  - **Done when:** each hotfix/hold/rollback decision is evidence-backed and communicated.

- [ ] **64C — Support workflow and save recovery** [P]
  - **Build / Author:** ticket templates/log-save collection with consent, known issues/workarounds, damaged-save escalation,
    response SLAs/cadence and platform-specific routing.
  - **Done when:** support can reproduce/escalate without asking players to destroy their only save.

- [ ] **64D — First balance patch measurement and delivery** [C/P]
  - **Build / Author:** aggregate real combat/economy/XP/loot/corruption/difficulty signals against Phase 56 bands;
    separate bugs from tuning; publish rationale and migration/rollback impact.
  - **Verify:** regression cohorts, both endings, existing saves and no new mechanics.
  - **Done when:** approved adjustments ship safely or evidence says no patch is needed.

- [ ] **64E — Stabilization exit review** [P]
  - **Threshold:** crash/save/progression rates within approved live bands, no open Blocker/Critical, support backlog stable,
    hotfix branch reconciled and known issues documented.
  - **Done when:** incident posture ends and normal post-launch cadence begins.

---
