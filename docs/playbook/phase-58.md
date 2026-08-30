## Phase 58 — Save/Load Hardening & Migration [F]

> Follow SAVE_FORMAT exactly: atomic save.json is authoritative; header mirror may be deleted, never stale;
> a partial restore is failure; live collections clear before restore; stable SaveIds are primary keys.

- [ ] **58A — Save ownership/schema/orphan audit** [F/P]
  - **Build / Author:** enumerate every G3 state owner/header field/SaveId, saved-versus-derived decision, deferred
    cell claim and current schema/migrations; fixtures for clean, maximal and previous-version saves.
  - **Done when:** no state lacks one owner and zero rename/version change is undocumented.

- [ ] **58B — Long-play/large-state stress fixtures** [F]
  - **Build / Author:** synthetic+played long save with large/affixed/socketed-as-scoped inventory, all quests/flags/
    guilds/companions/world states/map/shops/housing/codex and spawned actors; measure size/time.
  - **Verify:** repeated save/load and exact semantic diff, not only “load succeeded.”
  - **Done when:** maximal state restores exactly within approved time/size budgets.

- [ ] **58C — Repeated save/load, autosave ring and transition stress** [F]
  - **Verify:** hundreds of quick/manual cycles; autosave rotation/cadence; region hard loads; boss/cinematic/ending/
    shop/placement transitions; load failure returns title and never autosaves partial live state.
  - **Done when:** no drift, duplicate reward/actor, stale header or collection merge occurs.

- [ ] **58D — Interrupted-write and filesystem fault matrix** [F]
  - **Test:** interruption before temp write/during write/before rename/after authoritative rename/before mirror;
    missing/locked/disk-full/permission paths as harnessable; newest good save remains recoverable.
  - **Done when:** no fault truncates the prior authoritative save and user messaging is actionable.

- [ ] **58E — Corrupt/foreign/newer/partial slot recovery** [F]
  - **Test:** invalid JSON, no version/objects/header, bad component payload, corrupted mirror, newer version,
    orphan/missing keys and screenshot damage. Define quarantine/backup/delete/retry UI behavior.
  - **Done when:** corruption cannot enter Playing or overwrite a good save, and recoverable slots remain usable.

- [ ] **58F — Migration and backwards-compatibility campaign** [F]
  - **Build / Author:** retain v1→v2 geography migration; test every supported shipped/RC schema fixture stepwise;
    explicitly decide launch support window and downgrade refusal; migration is idempotent and backed up.
  - **Done when:** every in-scope old fixture upgrades exactly or refuses safely with reason.

- [ ] **58G — Cloud conflict behavior (conditional on platform)** [F/P]
  - **Build / Author:** if cloud approved, define conflict identity/timestamps/playtime/device, local/cloud/both options,
    upload/download interruption and no silent last-write-wins; otherwise record out of launch scope.
  - **Verify:** offline/online, concurrent divergent slots, clock skew, deleted slot and corrupted remote.
  - **Done when:** conflicts are recoverable and no choice silently destroys the other copy.

- [ ] **58H — Manual destructive-path acceptance and sign-off** [P]
  - **Verify:** F9/pause load transform, different-region load, hand-corrupt objects, power-kill simulation, backup restore,
    maximal save and cloud matrix as scoped; compare restored object/state manifests.
  - **Done when:** zero data-loss/partial-restore path, all in-scope migrations pass and maintainer signs evidence.

---
