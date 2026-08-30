## Phase 42 — Guild & Faction Questlines `[C]`

> **Dependencies and authority.** Phase 41 is the only quest runtime. Membership, refusal,
> leaving, rank and consequences use persistent `StoryFlagsComponent` flags; public attitude uses
> `ReputationComponent` and `FactionResource`. Do not build a guild quest log, guild reputation bar
> or second save ledger. Every hub actor needs a stable `PersistentId`, schedule, localized dialogue
> and canonical map location. Unique rewards need an inventory-full/duplicate answer.

- [ ] **42A — Shared membership/rank contract + small guild UI** `[F/C]`
  - **Goal:** one inspectable shape for all five guilds.
  - **Build / Author:** audit the live faction/flag/quest/journal seams; register five guild ids and a
    stable flag vocabulary (`offered`, `joined`, `left`, `refused`, named rank flags, `finale`);
    document join/leave/refuse/rejoin policy per guild and forbidden combinations. Add a compact
    `UiTheme` guild section that *derives* state from flags plus `FactionResource`; add a `guild`
    debug report/mutator through the normal flag choke point. Validate skipped/contradictory ranks.
  - **Do not:** add `GuildComponent`, display names in flags, or make the UI authoritative.
  - **Verify:** pure flag→state tests, validator negative cases, all five default/terminal states,
    save→mutate→load replacement, and panel captures at 854×534 through ultrawide.
  - **Done when:** one resolver/UI/report names every guild state, invalid chains fail, and reload
    restores exact membership and rank.

- [ ] **42B — Guild hubs, rosters and Phase 44 placement handoff** `[C]`
  - **Goal:** give each organization a credible home before quest content depends on it.
  - **Build / Author:** assign a primary hub/territory, leader, quartermaster, quest contact and rank
    peer to each guild. Place only what current regions support; give Phase 44 exact cell/location ids
    for future hubs. Author membership-aware initial greetings, schedules, map/service/shop links and
    stable ids. Distinguish fortress/watch, hunting lodge, archive, contract house and concealed order.
  - **Do not:** put hubs in transitional country for even distribution or clone one hall five times.
  - **Verify:** map generator/probe, schedule destinations, dialogue/flag reachability, travel-node
    approach and eye-level front/back captures.
  - **Done when:** every hub/actor has one owner and all currently reachable hubs are mapped/playable.

- [ ] **42C — Dawnwardens recruitment and probation** `[C]`
  - **Goal:** establish protection of civilians and the tension between duty and coercive order.
  - **Build / Author:** join/refuse dialogue plus a Defend/Reach probation pair: answer a civilian
    threat, then choose rescue versus punitive expediency. Use villagers/Dawnwardens standing and
    flags, not morality points; introduce a named field partner and first-rank reward.
  - **Verify:** join/refuse/leave, failed-defense retry, pre-resolved threat, branch save/load and NPC
    reactions without consuming Iron King story flags.
  - **Done when:** both decisions have honest terminal/continuation states and rank one is earned.

- [ ] **42D — Dawnwardens command arc and payoff** `[C]`
  - **Goal:** resolve service versus authoritarian survival and make final rank world-visible.
  - **Build / Author:** mid-rank escort/defense, Iron King-linked command dispute and two-resolution
    finale; protection/resilience rewards distinct from divine relics; post-finale patrol, service and
    hub dialogue variants. Expose stable Act II/epilogue flags without gating the main story.
  - **Verify:** both resolutions, Iron King defeated early, companion absent/present, full pack,
    journal/map cleanup and reload immediately before/after choice.
  - **Done when:** finale outcome changes Dawnwarden presence and is independently inspectable.

- [ ] **42E — Ash Hunters field induction** `[C]`
  - **Goal:** make knowledge/preparation—not a kill counter—the hunter identity.
  - **Build / Author:** tracked-beast investigation using placed clues, Bestiary, existing Reach/
    Interact/Kill and encounter/lair data; briefing, trophy hand-in and spare/kill choice for a
    territorial creature; Phase 44 hooks into Ashen Wilds.
  - **Do not:** add tracking vision, harvesting, traps or a monster-part subsystem.
  - **Verify:** target killed early, companion final hit, spare/kill, target reload, bestiary/quest id
    agreement and regional encounter filters.
  - **Done when:** investigation→hunt→judgment works in every target state and grants rank one.

- [ ] **42F — Ash Hunters dragon/corruption finale** `[C]`
  - **Goal:** culminate in a prepared hunt that distinguishes Wild, Ancient and Ash dragons.
  - **Build / Author:** a corrupted-beast/dragon contract and final operation allowing slay,
    withdrawal or protection of a speaking Ancient where valid; reuse lairs, boss telegraphs,
    disposition and dialogue. Reward resistance gear, bounty access and a named trophy.
  - **Verify:** Ancient neutral/provoked, dragon already dead, corruption-tier dialogue, reward
    overflow and every Phase 47 realm-arc hook flag.
  - **Done when:** final rank records judgment rather than raw kills and feeds Frostfang/Ashen arcs.

- [ ] **42G — Veiled Archive admission and recovered knowledge** `[C]`
  - **Goal:** turn the fading-Weave/recovered-spell systems into the scholar guild's play loop.
  - **Build / Author:** lost-tome admission, ley-site survey and Ancient-knowledge lead using tomes,
    `LearnSpell`, Weave potency and existing objectives; establish Sunspire library placement handoff.
  - **Do not:** add research currency or a second spell progression track.
  - **Verify:** tome already learned, Ancient spared/killed, low/high-potency regions, duplicate spell
    reward, save after learning and canonical map targets.
  - **Done when:** rank one plus a recovered spell are completable for every Ancient outcome.

- [ ] **42H — Veiled Archive truth-custody finale** `[C]`
  - **Goal:** decide whether dangerous knowledge is preserved, shared or sealed, feeding Act III.
  - **Build / Author:** cross-realm recovery plus three-result finale; update archive access/dialogue;
    mastery/tome rewards without granting Phase 48's highest spells; explicit Phase 48 fallback for
    every outcome.
  - **Verify:** missing prior tome, low standing, high corruption, every branch and reload; confirm no
    Act III reveal is authored early.
  - **Done when:** every result leaves an explicit playable Act III handoff.

- [ ] **42I — Iron Syndicate contract rank** `[C]`
  - **Goal:** establish pragmatic mercenary/bounty work, not an assassin reskin.
  - **Build / Author:** recruit through existing contract-board/economy surfaces; author a bounty,
    paid escort and spare/kill target resolution; integrate contraband/public standing and record how
    the work ended.
  - **Do not:** add stealth takedowns, hostage AI, bounty currency or a new board runtime.
  - **Verify:** target dead early/spared, rotation overlap, failed escort/retry, poor player/bribe and
    full-pack payout.
  - **Done when:** varied paid work grants rank one and its consequences persist.

- [ ] **42J — Iron Syndicate loyalty-for-sale finale** `[C]`
  - **Goal:** choose between contract fidelity, a better offer and protecting a relationship.
  - **Build / Author:** trade-route operation plus employer/target/self-interest finale; integrate
    shops/services/reputation/contraband. Reward economy access, bounty privileges and one protected
    named item with duplicate/overflow handling.
  - **Verify:** each allegiance, hostile employer, unavailable target, hired companion, toll/travel,
    handoff save/load and reward protection.
  - **Done when:** final rank or expulsion is economically/world visibly distinct and main-story safe.

- [ ] **42K — Emberbound secret initiation** `[C]`
  - **Goal:** introduce a hidden order studying Flamebearers and relic ethics.
  - **Build / Author:** gate contact on legitimate Phase 23/28 state, not level; discreet investigation
    and relic-handling choice using corruption/boss/relic flags; concealed location undiscovered until
    initiation and canonical afterward.
  - **Verify:** before/after Iron King, every corruption tier, relic accepted/refused, hidden/revealed
    map, refusal policy and save across initiation.
  - **Done when:** initiation is distinct from public guilds and reveals no future twist prematurely.

- [ ] **42L — Emberbound reckoning and payoff** `[C]`
  - **Goal:** resolve whether divine power is safeguarded, destroyed or instrumentalized.
  - **Build / Author:** cross-guild/relic investigation, flag-driven internal schism and three-doctrine
    finale; corruption/rank/reward consequences plus only the hooks needed by visions, Act III and
    epilogues.
  - **Verify:** pure/corrupt, relic accepted/refused, rival-guild combinations, leader unavailable,
    every branch and decision-point reload.
  - **Done when:** doctrine, order state and player rank are separately inspectable.

- [ ] **42M — Five-guild integration and sequence-break campaign** `[C/P]`
  - **Goal:** close a coherent faction layer, not five happy paths.
  - **Build / Author:** matrix membership combinations, leave/refuse/rejoin rules, standing, cross-guild
    reactions, rewards, hubs, map ids and Phase 46–49 flags; repair uncovered seams and add bounded repros.
  - **Verify:** clean/all-joined saves, arcs out of order, unavailable actors/targets, companion kills,
    full pack, region transitions, every terminal save/load, `validate-all`, map and panel/hub captures.
  - **Done when:** every matrix cell has an authored result, no guild or reward can orphan another arc,
    and Phase 45 can enumerate the whole layer without exceptions.

---
