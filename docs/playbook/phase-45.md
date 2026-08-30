## Phase 45 — Alpha Hardening, TRUE Feature-Complete Audit & Freeze `[F/P]`

> **G2 is audited, not declared.** Compare DESIGN, LORE, live code/data and Phases 42–44.5.
> Every shipped player verb/mechanic exists before freeze. Rough content is allowed; hidden
> engineering debt is not.

- [ ] **45A — Feature-completeness matrix and evidence baseline** `[P]`
  - **Goal:** create the authoritative G2 inventory.
  - **Build / Author:** rows for player verbs; melee/magic/**physical ranged**/stealth; combat/AI/
    bosses/dragons; traversal/mounts/non-swimming water; quests/dialogue/cinematics; factions/guilds/
    cult; companions/housing/economy/crafting/items; corruption/world state; realms/map; UI/input/
    accessibility (including remapping, difficulty and aim assists), audio, Codex/compendium; save/load/debug.
    Record promise, live evidence, persistence owner, play route,
    test/probe, content owner and status `present/content gap/feature hole/cut/decision`.
  - **Verify:** every DESIGN/LORE heading and roadmap promise maps once; all future “later/TODO/no phase/
    deferred/stub/placeholder/TBD” classified; struck 40/40.5 and swimming only map to explicit cuts.
  - **Done when:** no promise is unclassified and every feature hole has a pre-freeze owner.

- [ ] **45B — Physical ranged-combat contract and resource seam** `[F]`
  - **Goal:** close the confirmed hole: the repo has no bow item, firing path or ranged component,
    while DESIGN promises complete combat breadth and an old note assumed a bow.
  - **Build / Author:** bow as first physical-ranged family; draw/release/stamina; explicit finite-ammo
    go/no-go; damage/crit/poise via `DamagePacket`; aim via `AimNode`; equipment dispatch that does
    not route OffHand to `MeleeWeaponComponent`; append-safe data and one authoring example/recipe.
  - **Do not:** call spell mana/projectiles the weapon abstraction or hide mechanics in Phase 51.
  - **Verify:** timing/damage/selection/range tests, validator range ends, melee/bow/spell input,
    cameras/controller, equipped/ammo save.
  - **Done when:** one bow equips and attack selects the right verb without breaking melee/block/cast.

- [ ] **45C — Bow firing, collision and lifecycle** `[F/P]`
  - **Goal:** complete a readable ranged player verb.
  - **Build / Author:** draw/cancel/release, pooled physical projectile or justified hitscan, world/
    hurtbox/body-zone collision, feedback, animation/VFX/SFX, crosshair, lock-on/assist seam and kill
    attribution. Define menu/dodge/stagger/mount/cutscene/load behavior.
  - **Verify:** miss/world/body-zone/crit/stagger, flying target, pause/menu/cutscene, cancel paths,
    camera obstruction/convergence and projectile cleanup.
  - **Done when:** a bow-only player defeats representative ground/flying enemies with correct credit.

- [ ] **45D — Bow Alpha content slice and QA tool** `[F/C]`
  - **Goal:** prove viable but not dominant ranged combat before G2.
  - **Build / Author:** one bow plus approved quiver/ammo, loot/shop/quest placement, UI details, one
    enemy pressure answer and one cover/range encounter; `ranged` repro/report and validation.
    Hand catalogue breadth to Phase 51 and numbers to Phase 56.
  - **Verify:** clean acquisition, full pack, save/reload, all combat alternatives, economy and aim assist seam.
  - **Done when:** Phase 51 owes content only—never firing code.

- [ ] **45E — Remaining feature-hole burn-down** `[F/C]`
  - **Goal:** resolve every other matrix hole/decision.
  - **Build / Author:** add bounded 45 lettered addenda if required; implement, explicitly cut by
    authority, or document evidence with persistence/UI/Loc/validation/probes in the same unit.
  - **Do not:** relabel mechanics Beta content or invent a top-level phase to move G2.
  - **Verify:** rerun source/doc searches; zero unowned holes.
  - **Done when:** every row is present, content-only, explicit cut or approved post-launch option.

- [ ] **45F — Cross-system integration matrix** `[F/P]`
  - **Goal:** test seams between individually complete systems.
  - **Build / Author:** combat verbs×enemy/companion; quest branch/failure×region/save; faction/guild/
    cult×dialogue; housing/economy/rewards; boss/cinematic/vision/corruption/world state; map/travel/
    mount/water; UI/input/pause. Add tests/probes at the owning choke point.
  - **Verify:** deterministic positive/negative runs; defects carry severity/repro/owner.
  - **Done when:** every row has captured evidence, never “assumed.”

- [ ] **45G — Sequence-break and reachability campaign** `[C/P]`
  - **Goal:** prove the rough whole-game shape cannot be orphaned.
  - **Build / Author:** out-of-order quests, pre-killed targets, missing actors, declined branches,
    guild/cult combinations, early bosses, hidden realm before/after reveal, repeated fail/retry;
    audit orphan/self-gating flags, map targets and fallback entries.
  - **Verify:** `validate-all`, reports, dangerous-transition saves and manual play script.
  - **Done when:** every break leaves an honest next objective or recovery.

- [ ] **45H — Alpha save/load matrix** `[F/P]`
  - **Goal:** prove every G2 owner replaces live state.
  - **Build / Author:** clean/old save; all realms; guild/cult terminals; cinematic seen; boss/reward;
    realm state; companions/housing/economy; ranged gear; large inventory; deferred claims/SaveIds/migration.
  - **Verify:** save→mutate opposite→load including false/empty, cross-region load, orphan/missing/volatile,
    previous-format fixture and manual F9/pause load.
  - **Done when:** every owner round-trips, zero volatile ids and no unexplained orphan.

- [ ] **45I — Alpha performance sanity gate** `[P]`
  - **Goal:** stop Beta scaling on a regressed baseline.
  - **Build / Author:** existing profiler/world harnesses in representative hub, wilderness, boss, cult,
    cinematic, mounted traversal and worst resident realm; compare frame/draws/primitives/memory/build to
    NOW baselines. Keep hardware targets provisional until approved.
  - **Verify:** world perf probe, cold/warm shader, transitions and repeated traversal.
  - **Done when:** five realm baselines and named Phase 57 worst cases exist with explained deltas.

- [ ] **45J — Blocker burn-down and full regression** `[F/P]`
  - **Goal:** fix findings rather than rename them polish.
  - **Build / Author:** Blocker = progress/data/gate impossible; Critical = crash/corruption/core broadly
    broken; High = major path with workaround. Fix all Blocker/Critical and any High invalidating a row;
    add regression evidence.
  - **Verify:** affected rows plus build/tests/validate/negative/world-quality battery.
  - **Done when:** zero Blocker/Critical and every accepted High has owner/phase/risk.

- [ ] **45K — Freeze exception process and G2 sign-off** `[P]`
  - **Goal:** lock a proven set and make exceptions visible.
  - **Build / Author:** exception template: player problem, why data cannot solve, systems/save/schema/UI/
    Loc/perf/tests, schedule, rollback, approver. Evidence packet: matrix, integration/sequence/save/perf,
    defect thresholds, five-realm traversal and maintainer play sign-off.
  - **Verify:** roadmap/playbook agree; every Beta item is content/fix; optional 51.5/53.5 excluded unless
    approved before freeze.
  - **Done when:** maintainer signs, matrix has zero holes and thresholds hold.

> **🚩 G2 — Alpha / Feature Complete.** Every shipped mechanic exists, integrates, persists and
> has evidence; all five realm shapes are traversable; no Beta item conceals missing engineering.

---

# Stage D — Beta / Content Complete (→ G3)

> Author against frozen systems. New-mechanic needs invoke 45K; they are not silently built.
