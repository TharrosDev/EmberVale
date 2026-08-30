## Phase 62 — Release Candidate & Gold Master [P]

- [ ] **62A — Code/content lock and change-control board** [P]
  - **Build / Author:** lock commit/branches/content versions; allowed fixes only with severity, risk, tests, affected
    evidence, rollback and approver. Any content/mechanic exception reopens G3/G2 rows.
  - **Done when:** candidate scope is immutable except approved fixes.

- [ ] **62B — RC build series and candidate ledger** [P]
  - **Build / Author:** produce numbered, hashed, signed artifacts with source commit, toolchain, migrations, known issues,
    symbols, store/platform state and delta from prior RC.
  - **Verify:** clean install/update/launch/save/load/quit on every approved target.
  - **Done when:** each RC is reproducible and one is nominated.

- [ ] **62C — Final cross-discipline validation** [P]
  - **Verify:** G3 smoke/both endings, QA exit query, performance cert, save fault/migration, localization LQA, accessibility,
    platform compliance, legal/store assets and base-game isolation from optional/postlaunch content.
  - **Done when:** all evidence references the exact nominated build and no stale report is accepted.

- [ ] **62D — Day-one patch, rollback and launch-ops plan** [P]
  - **Build / Author:** patch eligibility/cutoff, candidate branch/artifact, migration compatibility, validation delta,
    deployment timing, rollback triggers/process, telemetry dashboards/on-call/support messaging and store propagation.
  - **Done when:** patch can ship or be withheld independently and rollback preserves saves.

- [ ] **62E — Gold Master risk review and sign-off** [P]
  - **Threshold:** zero known Blocker/Critical/crash/data-loss/cert issues; High issues individually approved only if no
    core/content/accessibility impact and documented publicly/support-ready; crash/perf/save/platform targets pass.
  - **Done when:** production, engineering, QA and platform owners sign exact build/hash and rollback recommendation.

- [ ] **62F — Release rehearsal and sealed artifact handoff** [P]
  - **Verify:** time-boxed dry run from artifact retrieval through submission, store scheduling, monitoring, support,
    day-one patch/rollback decision and announcement; permissions/contacts tested.
  - **Done when:** rehearsal meets timeline, sealed artifacts are recoverable and G4 is signed.

> **🚩 G4 — Release Candidate.** Exact gold-master artifacts meet every approved ship requirement,
> zero Blocker/Critical issues remain, and launch/rollback operations are rehearsed.

---
# Stage F — Launch (→ G5)
