## Phase 61 — Platform Compliance, Builds & Storefront [P]

> Steam/Windows/Linux presets exist from the slice, but launch platforms are not automatically committed.
> Console, Steam Deck verification, achievements/trophies and cloud features are conditional on signed targets.

- [ ] **61A — Target-platform/service decision and requirement matrix** [P]
  - **Build / Author:** approve OS/store/hardware targets, architecture, online/cloud/achievement/controller/Deck/
    console scope, account/devkit/cert lead times and owners. Import current authoritative platform requirements.
  - **Done when:** required/conditional/out-of-scope rows are signed and no generic TRC promise remains.

- [ ] **61B — Reproducible signed release-build pipeline** [P]
  - **Build / Author:** pinned Godot/.NET/import environment, clean checkout build, version/commit/channel metadata,
    deterministic content generation/validation, secrets/signing isolation, symbols and artifact hashes/retention.
  - **Verify:** two clean builds, install/uninstall/upgrade, no dev tools/assets, licence manifest and rollback artifact.
  - **Done when:** one command/workflow produces traceable candidate artifacts for each approved target.

- [ ] **61C — Steam/approved-store integration** [F/P]
  - **Build / Author:** app/build/depot branches, overlay/lifecycle, controller glyph policy, launch options, save path/
    cloud if approved and offline behavior; platform APIs behind isolation/fallback.
  - **Verify:** clean account/install, offline, overlay/controller, update/rollback and cloud conflict via Phase 58.
  - **Done when:** every required store feature passes its current checklist.

- [ ] **61D — Achievements/trophies decision and implementation** [F/C]
  - **Build / Author:** if approved, bounded list mapped to authoritative story/system events, no inaccessible/missable
    surprise, offline queue/idempotence, save/platform reconciliation and localized names/descriptions/icons.
  - **Verify:** fresh/existing saves, both endings, repeat trigger, offline→online and platform reset test account.
  - **Done when:** list is complete/cert compliant; otherwise explicit launch no-go recorded.

- [ ] **61E — Controller, cloud and platform compliance matrix** [P]
  - **Verify:** required input/glyph/suspend/resume/focus/storage/error/network/user-change behaviors per approved target;
    cloud conflicts use 58G; platform-specific failures have player-facing recovery.
  - **Done when:** every mandatory requirement has build evidence and zero unresolved cert blocker.

- [ ] **61F — Storefront assets, ratings, legal and credits** [P]
  - **Build / Author:** capsules/screenshots/trailer/copy/feature claims, ratings questionnaire, privacy/telemetry disclosure,
    EULA as chosen, third-party notices and full asset/VO/localization credits. Claims must match approved platforms/content.
  - **Verify:** current store specs, safe areas/locales, rights provenance and no cut-feature screenshot/copy.
  - **Done when:** all required assets/legal metadata are approved and submission-ready.

- [ ] **61G — Packaging/submission rehearsal and compliance sign-off** [P]
  - **Verify:** fresh-machine install, upgrade from prior candidate, DLC-free base behavior, save migration, antivirus/
    permissions, crash symbols, uninstall leftovers, store sandbox submission and rollback.
  - **Done when:** matrix passes, submission blockers are zero and evidence enters Phase 62.

---
