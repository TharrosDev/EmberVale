## Phase 32 — Companion System `[F]`

- [x] **32A — `CompanionComponent` + follower AI core** `[F]` ✅
  - **Done when:** a companion follows/holds on the player's team, reusing
    `EnemyAIComponent`/`Locomotion`/`Combat`; recruit/dismiss API; `ISaveable`
    roster.
  - **Landed:** `CompanionAIComponent` (anchor/leash follower FSM on the shared
    `LocomotionComponent` + `PathSteering` + `MeleeWeaponComponent`), the pure
    `CompanionDecision`/`CompanionFormation` cores (15 unit tests), `CompanionFactory`
    (team 0) + `CompanionRegistry`, and an `ISaveable` `CompanionRoster` with
    recruit/dismiss/stance + a save-reconciling party. Kael is recruitable via the
    `companion` dev command; toasts on join/leave/down.
- [x] **32B — Command states (follow / hold / engage)** `[F]` ✅
  - **Done when:** the player can command stance via a quick command; combat assist
    works.
  - **Landed:** an `Engage` order alongside follow/hold; `C` (D-pad right) cycles the
    party's standing order with a toast; the pure `CompanionOrders` sets each order's
    leash/scan envelope (6 tests); assist focus makes companions prioritise the
    player's lock-on target; an engage order stands itself down once the fighting
    stops; a self-hiding `PartyWidget` shows each companion's health + current order.
- [x] **32C — `CompanionResource` + loyalty standing** `[F]` ✅
  - **Done when:** companions are data (`CompanionResource`) with a per-companion
    loyalty standing (reuse `ReputationComponent` patterns), persistent.
  - **Landed:** `CompanionResource` + `CompanionDatabase` (`data/companions/Kael.tres`);
    the registry and factory now build entirely from the resource (stats, weapon,
    model, faction, spells, follower envelope). Loyalty is a 0–100 standing with
    Wary/Steady/Trusted/Sworn tiers held and persisted by the roster (kept even for
    dismissed companions), projected onto stats by `CompanionLoyaltyComponent`.
    Dialogue gained `RecruitCompanion`/`DismissCompanion`/`AddCompanionLoyalty`
    effects and `CompanionRecruited`/`CompanionNotRecruited`/`CompanionLoyaltyAtLeast`
    conditions, so 32E is authorable content. Validator + 24 new tests.
- [x] **32D — Party persistence + save round-trip** `[F]` ✅
  - **Done when:** roster, positions, and loyalty survive save/load and region
    streaming.
  - **Landed:** the party save now carries each companion's transform, and loading
    is a *reconcile* (pure `CompanionPartyReconcile` + 7 tests) — survivors keep
    their actor and move, only genuine newcomers are built. `CompanionAIComponent`
    became `ISaveable` for the state the roster can't see (hold anchor, downed +
    recovery countdown). Region hard-loads call `RegroupNow()` so the band cuts to
    formation the moment the player lands, while held companions stay put. A
    `party` repro scenario pins a deterministic party-in-the-field run.
- [x] **32E — Kael authored fully (recruit + loyalty quest + dialogue)** `[C]` ✅
  - **Done when:** one complete companion (Kael) is recruitable with a dialogue
    graph + recruit quest + loyalty quest; the rest deferred to Beta.
  - **Landed:** Kael Aldemar, last shield of the Emberguard, stands in the Ember
    Crown hub. A 14-node conversation carries the whole arc: the recruit quest
    *The Oathkeeper's Debt*, the recruit itself, the loyalty quest *What the Ash
    Took* (his sword-brother Toren's plunder), a one-time loyalty payoff sealed
    behind a story flag, a trust-gated personal line, and an amicable parting.
    `CompanionRecruiterComponent` swaps the town NPC out while he travels with you
    and back when dismissed; the recruited actor carries his own dialogue so
    personal content is reachable from the party member. `KaelContentTests` checks
    the authored graph (reachability, string keys, ordinals, prerequisites) without
    needing Godot.

**Phase 32 complete** — 32A–32E all landed.

---
