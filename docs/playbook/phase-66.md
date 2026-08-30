## Phase 66 — Expansion / DLC Framework [F/C]

> Build seams and acceptance criteria, not hypothetical expansion fiction. An expansion must not fork the
> base game or make launch saves/content unavailable. Base-only installs remain first-class.

- [ ] **66A — Content-pack identity, dependency and entitlement contract** [F/P]
  - **Build / Author:** pack id/version, required base version, optional dependencies/conflicts, entitlement provider
    interface with offline/cache/error policy, manifest signatures and dev entitlement override that cannot ship enabled.
  - **Verify:** entitled/unentitled/offline/expired/provider unavailable/wrong version/tampered manifest.
  - **Done when:** content availability is deterministic and a service outage cannot corrupt a save.

- [ ] **66B — Isolated discovery/loading/validation pipeline** [F]
  - **Build / Author:** namespaced databases/ids/Loc/assets/regions/quests/items, deterministic load order, duplicate-id
    refusal, pack-aware ContentValidator and actionable quarantine; base databases behave identically with no pack.
  - **Do not:** allow packs to overwrite base ids/resources in place.
  - **Verify:** base only, one/multiple packs, missing dependency, malformed pack, update/downgrade and negative cases.
  - **Done when:** bad optional content is refused without preventing base-game boot.

- [ ] **66C — DLC save ownership and missing-content compatibility** [F]
  - **Build / Author:** namespaced save records, pack/version manifest in envelope, migration per pack and policy when an
    entitled pack is unavailable: warn/restore later or refuse only the affected continuation, never silently delete state.
    Define player location fallback from a missing DLC region.
  - **Verify:** save in DLC region then uninstall/offline/reinstall/update; base inventory with DLC item; both endings/NG+.
  - **Done when:** base progress survives every availability transition and DLC state returns intact when restored.

- [ ] **66D — New-realm expansion production/shipping seam** [F/C]
  - **Build / Author:** template checklist using modern region spec/quality, map/travel unlock, world state, quests/
    cinematics, items/economy, art/audio/Loc/accessibility/performance/platform/store entitlement and separate build/package.
  - **Verify:** base-only and entitled campaigns, cross-boundary travel/save, rollback and platform packaging.
  - **Done when:** a small non-fictional test pack proves the seam without committing expansion content.

- [ ] **66E — Base-game isolation, rollback and expansion acceptance** [P]
  - **Verify:** clean base install byte/behavior expectations, base saves before/after pack, entitlement changes, corrupted
    pack, pack rollback, cloud conflict, achievements, localization, support diagnostics and uninstall.
  - **Done when:** no base-game fork exists, pack absence cannot break base play, compatibility policy is documented and
    the test pack passes release-grade evidence.

> **🚩 G6 — Live.** The live game has a sustainable, save-safe optional-content pipeline; no DLC content
> itself is promised by this gate.

---

## Appendix — keeping this playbook honest

- Split a sub-phase when it exceeds one focused session; keep one buildable/playable vertical result.
- Gates require captured evidence and maintainer sign-off, not checked boxes.
- Every stateful addition owns persistence immediately; every reachable place owns map integration.
- Phase 40/40.5 cuts, no swimming, continuous terrain and intentional empty country remain permanent.
