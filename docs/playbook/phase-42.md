## Phase 42 — Guild & Faction Questlines `[C]`

> **Dependencies and authority.** Phase 41 is the only quest runtime. Membership, refusal,
> leaving, rank and consequences use persistent `StoryFlagsComponent` flags; public attitude uses
> `ReputationComponent` and `FactionResource`. Do not build a guild quest log, guild reputation bar
> or second save ledger. Every hub actor needs a stable `PersistentId`, schedule, localized dialogue
> and canonical map location. Unique rewards need an inventory-full/duplicate answer.

- [x] **42A — Shared membership/rank contract + small guild UI** `[F/C]` ✅
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
  - **Done:** a guild IS a `FactionResource` with ranks. `GuildRules` derives the flag vocabulary
    and resolves state; the character screen's Guilds tab and the `guild` console command both
    derive from it; `ValidateGuilds` closes the five-guild set from both sides. Zero new save
    surface — see the retrospective below.

- [x] **42B — Guild hubs, rosters and Phase 44 placement handoff** `[C]` ✅
  - **Goal:** give each organization a credible home before quest content depends on it.
  - **Build / Author:** assign a primary hub/territory, leader, quartermaster, quest contact and rank
    peer to each guild. Place only what current regions support; give Phase 44 exact cell/location ids
    for future hubs. Author membership-aware initial greetings, schedules, map/service/shop links and
    stable ids. Distinguish fortress/watch, hunting lodge, archive, contract house and concealed order.
  - **Do not:** put hubs in transitional country for even distribution or clone one hall five times.
  - **Verify:** map generator/probe, schedule destinations, dialogue/flag reachability, travel-node
    approach and eye-level front/back captures.
  - **Done when:** every hub/actor has one owner and all currently reachable hubs are mapped/playable.
  - **Done:** five structures of five different kinds, fourteen new officers with
    membership-aware greetings, five leader routines and five map pins — all in the Ember
    Crown, all mapped, all walkable. `FactionResource` gained a hub id and a four-role
    roster; nothing else was invented. See the retrospective below.

- [x] **42C — Dawnwardens recruitment and probation** `[C]` ✅
  - **Goal:** establish protection of civilians and the tension between duty and coercive order.
  - **Build / Author:** join/refuse dialogue plus a Defend/Reach probation pair: answer a civilian
    threat, then choose rescue versus punitive expediency. Use villagers/Dawnwardens standing and
    flags, not morality points; introduce a named field partner and first-rank reward.
  - **Verify:** join/refuse/leave, failed-defense retry, pre-resolved threat, branch save/load and NPC
    reactions without consuming Iron King story flags.
  - **Done when:** both decisions have honest terminal/continuation states and rank one is earned.
  - **Done:** Serjeant Danhal's `stranger` exchange grew the join/refuse door
    (`SetFlag guild.dawnwardens.{joined,refused}` — the WRITE side of invariant 18, matching what
    `GuildRules` already derives). `FactionResource.RankPeerNpcId` — declared and left empty in 42B
    for exactly this — now names Bram Corrow, placed beside the Watch's existing officers in the same
    already-validated yard. His one dialogue graph owns the whole probation arc: the fork
    (`quest.hollowreach.barrels`'s two-choices-deep, gate-on-each-other shape) picks rescue or
    punitive expediency *before* `quest.dawnwardens.probation` starts, `SequentialObjectives` keeps
    the Defend objective (`location.wilds.west`, 60s, `HoldTheNorthRoad`'s shape) first regardless of
    how early the flag lands, and rank one is `SetFlag guild.dawnwardens.rank1` gated on
    `QuestCompleted` *inside* the node the branch flag already picked — an AND with no compound
    condition. Both Reach targets (`location.tarn.landing`, `location.crossway.watch`) were already
    mapped; zero new map locations, zero new terrain, zero new mechanism. See the retrospective below.

- [x] **42D — Dawnwardens command arc and payoff** `[C]` ✅
  - **Goal:** resolve service versus authoritarian survival and make final rank world-visible.
  - **Build / Author:** mid-rank escort/defense, Iron King-linked command dispute and two-resolution
    finale; protection/resilience rewards distinct from divine relics; post-finale patrol, service and
    hub dialogue variants. Expose stable Act II/epilogue flags without gating the main story.
  - **Verify:** both resolutions, Iron King defeated early, companion absent/present, full pack,
    journal/map cleanup and reload immediately before/after choice.
  - **Done when:** finale outcome changes Dawnwarden presence and is independently inspectable.
  - **Done:** `quest.dawnwardens.command` (Escort Sella Ru, `companion.dawnwarden_witness`, then
    Defend `location.wilds.north`) grants rank two; `quest.dawnwardens.finale` forks before it exists
    (`flag.dawnwardens.finale_service`/`finale_authority`, `quest.hollowreach.barrels`'s shape a third
    time in this one arc) into a Defend-the-corrie or Reach-the-Watch resolution, grants rank three and
    `guild.dawnwardens.finale`, and pays one reward either way (Dawn's Bulwark). Both rewards
    (`item.armor.warden_aegis`, `item.armor.dawn_bulwark`) are ordinary `BonusArmor`/`BonusMaxHealth`
    equippables — ItemGrant's existing full-pack/duplicate handling, no new reward mechanism. Serjeant
    Danhal and Captain Fenn each gained one more additive `GuildRankAtLeast:3` branch (the guild's
    declared ceiling) that reads which branch flag is set for a patrol/hub variant; Bram's own
    post-finale lines carry the third. `dialogue.dawnwarden_partner` reads `flag.iron_king_defeated`
    once, for flavour only, and never sets or clears it. Zero new map locations, zero new region/world
    changes, zero new `DialogueCondition`/`ObjectiveType`. See the retrospective below.

- [x] **42E — Ash Hunters field induction** `[C]` ✅
  - **Goal:** make knowledge/preparation—not a kill counter—the hunter identity.
  - **Build / Author:** tracked-beast investigation using placed clues, Bestiary, existing Reach/
    Interact/Kill and encounter/lair data; briefing, trophy hand-in and spare/kill choice for a
    territorial creature; Phase 44 hooks into Ashen Wilds.
  - **Do not:** add tracking vision, harvesting, traps or a monster-part subsystem.
  - **Verify:** target killed early, companion final hit, spare/kill, target reload, bestiary/quest id
    agreement and regional encounter filters.
  - **Done when:** investigation→hunt→judgment works in every target state and grants rank one.
  - **Done:** two placed clues in the Deadfall's already-yarded, never-furnished eastern pine thicket,
    a lair-shaped named boar (`enemy.grimtusk`, a DISTINCT template from the roaming
    `enemy.thornback_boar` so the two can never be confused by a Kill objective), and the whole arc
    living in `dialogue.hunter_tracker` — recruitment, briefing, verdict and the spare branch's own
    hand-in — plus `dialogue.hunter_skinner`'s kill-branch hand-in. Zero new objective types, zero new
    flag family beyond `flag.ash_hunters.*`; `guild.ash_hunters.rank1` is granted by whichever officer
    actually heard the outcome. See the retrospective below.

- [ ] **42F — Ash Hunters dragon/corruption finale** `[C]`
  - **Goal:** culminate in a prepared hunt that distinguishes Wild, Ancient and Ash dragons.
  - **Build / Author:** a corrupted-beast/dragon contract and final operation allowing slay,
    withdrawal or protection of a speaking Ancient where valid; reuse lairs, boss telegraphs,
    disposition and dialogue. Reward resistance gear, bounty access and a named trophy.
  - **Verify:** Ancient neutral/provoked, dragon already dead, corruption-tier dialogue, reward
    overflow and every Phase 47 realm-arc hook flag.
  - **Done when:** final rank records judgment rather than raw kills and feeds Frostfang/Ashen arcs.

- [x] **42G — Veiled Archive admission and recovered knowledge** `[C]` ✅
  - **Goal:** turn the fading-Weave/recovered-spell systems into the scholar guild's play loop.
  - **Build / Author:** lost-tome admission, ley-site survey and Ancient-knowledge lead using tomes,
    `LearnSpell`, Weave potency and existing objectives; establish Sunspire library placement handoff.
  - **Do not:** add research currency or a second spell progression track.
  - **Verify:** tome already learned, Ancient spared/killed, low/high-potency regions, duplicate spell
    reward, save after learning and canonical map targets.
  - **Done when:** rank one plus a recovered spell are completable for every Ancient outcome.
  - **Done:** three `PrerequisiteQuestId`-chained quests off the two officers 42B already placed and
    zero new placement, item or spell. `quest.veiled_archive.lost_tome` (admission) sends a candidate
    to ask Tam Quillfellow about the Treatise on Wardings his Scriptorium was never meant to sell
    (38L's own lore, finally used) and report to Keeper Ysolde Marr, whose 42B stranger line — "a
    person joins us by bringing us something we did not have" — is admission's whole brief, made
    playable; `JoinGuild` fires on the revisit after report, gated `Condition = 16` per the 42I
    checkpoint fix. `quest.veiled_archive.ley_survey` sends a member to the Ember Crown and Frostfang
    waystones — already-mapped 39.5A travel nodes sitting at opposite ends of `RegionResource.
    WeavePotency` (1.0 / 0.5) — off Ferris Vail's own 42B line about a ley line "somebody walked."
    `quest.veiled_archive.ancient_lead` sends a member to `dialogue.ancient_dragon` (35F) and reuses
    its two existing outcome flags untouched — `flag.ancient.taught` (spared) and
    `flag.ancient_dragon_defeated` (killed) — as the `RequiredFlagId` pair on two Talk objectives,
    the exact shape `quest.ash_hunters.grimtusk`'s report pair already proved (41D: only one is ever
    live, the other stays inert, no `ForbiddenFlagId` cross-gate needed since the flags are already
    mutually exclusive). Rank one and the recovered spell (`spell.elder_word`, already teachable from
    both outcomes since 35F) are granted together on Ysolde Marr's `member` branch, gated on
    `QuestCompleted` for `ancient_lead` — the two-node split (`LearnSpell` on the branch choice,
    `GuildRank` on the node it converges to) is 42I's own trick for "one choice, one effect." See the
    retrospective below.

- [ ] **42H — Veiled Archive truth-custody finale** `[C]`
  - **Goal:** decide whether dangerous knowledge is preserved, shared or sealed, feeding Act III.
  - **Build / Author:** cross-realm recovery plus three-result finale; update archive access/dialogue;
    mastery/tome rewards without granting Phase 48's highest spells; explicit Phase 48 fallback for
    every outcome.
  - **Verify:** missing prior tome, low standing, high corruption, every branch and reload; confirm no
    Act III reveal is authored early.
  - **Done when:** every result leaves an explicit playable Act III handoff.

- [x] **42I — Iron Syndicate contract rank** `[C]` ✅
  - **Goal:** establish pragmatic mercenary/bounty work, not an assassin reskin.
  - **Build / Author:** recruit through existing contract-board/economy surfaces; author a bounty,
    paid escort and spare/kill target resolution; integrate contraband/public standing and record how
    the work ended.
  - **Do not:** add stealth takedowns, hostage AI, bounty currency or a new board runtime.
  - **Verify:** target dead early/spared, rotation overlap, failed escort/retry, poor player/bribe and
    full-pack payout.
  - **Done when:** varied paid work grants rank one and its consequences persist.
  - **Done:** three contracts off Dallow Grieve's own conversation — a bandit bounty, Netta Vire's
    paid escort, and Corran Vess's spare/kill judgment — chained by `PrerequisiteQuestId` so "varied
    paid work" reads left to right, plus the join and the rank-one grant on Broker Ilder Vance's line.
    Two new `DialogueEffect` members (`JoinGuild`, `GuildRank`) are the only new code; everything else
    is `.tres`. See the retrospective below.

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

---

## 42A — a guild is a faction with ranks

The question that decided the whole sub-phase was **what a guild IS**, and the cheap answer turned
out to be the right one. Every one of the five already needed a `FactionResource` for public
standing, so a `GuildResource` beside it would have been a second registry, a second loader, a
second validator surface and a second thing to keep in step — for five rows of data. Instead
`FactionResource` gained `RankNameKeys` (ordered locale keys, lowest first) and `RejoinAllowed`,
and `IsGuild` is `RankNameKeys.Count > 0`. Four new `.tres` beside `IronSyndicate.tres`, which
already existed and only needed its ranks.

**The flag vocabulary is derived, never authored.** `GuildRules` builds
`guild.<slug>.offered/refused/joined/left/rankN/finale` from the faction id, so a flag id is not a
string two files have to agree about (invariant 12). Nothing anywhere authors one by hand.

`GuildRules.Resolve` is the whole rule as one ordered function — **finale → left → joined →
refused → offered → unknown** — the same shape as `BlessingRules.Decide`, and for the same reason:
the order is the design. Ranks are cumulative, and **a gap does not promote**: rank 3 without rank 2
resolves to rank 1 and reports `RankGap`, because the alternative is a hand-written flag silently
awarding seniority no arc granted. Contradictions are reported, never thrown — a bad save still has
to render.

Persistence cost **nothing**, which was the point. `StoryFlagsComponent` is already an `ISaveable`
whose `Load` clears before restoring, so membership replaces on load for free — and only while no
second ledger exists. `--panelshots` now proves it rather than asserting it: shot 15 saves the five
staged states, promotes the Dawnwardens to rank 3 and joins the Emberbound, loads the save back, and
photographs the tab. A merging load would show both mutations; the frame is identical to shot 14.

The UI is a fourth tab on the character screen, and adding it exposed a live bug in a panel nobody
had touched: **the character screen had no `GameLoadedEvent` subscription at all.** Every other
rebuilding panel has one. It never mattered while the screen drew only components whose own state
was restored in place; it would have mattered the moment it drew a fact that arrives as a flag.

### Retrospective + traps

`ValidateGuilds` checks the closed set **from both sides**, and that is the arm worth having: a
listed guild that stops declaring ranks silently becomes an ordinary faction and every quest gated
on membership goes dead, while an unlisted faction that starts declaring them silently becomes a
sixth guild `GameIds.Guilds`, the rank vocabulary and the UI know nothing about. Both load. Both
render. The rank count is an authored range (1..`GuildRules.MaxRanks`) with a negative case in each
direction, per invariant 8.

⚠️ **The rejoin policy is a GATE, not a state, and there is nothing to detect after the fact.** A
rejoin clears the `left` flag at the choke point, so a guild that forbids rejoining and a guild that
allows it look identical in the flags afterwards. `GuildRules.CanJoin` is therefore the only place
the policy can be enforced, and every future join path — 42C's dialogue, 42I's contract board — has
to route through it. A contradiction enum member for "rejoined against policy" was written and then
deleted, because nothing could ever set it.

⚠️ **The `guild` console command was reviewed, not driven.** The `F1` console needs keyboard input
and no CLI reaches it, which is true of every console command in this repo — but it means the
mutator's argument parsing has no automated caller. What IS driven is the path it writes through:
`--panelshots` stages all five states through `StoryFlagsComponent` exactly as the command does.

### Two things worth carrying into the next sub-phase

1. ⚠️ **THE CHEAP ANSWER TO "WHAT KIND OF THING IS THIS" IS USUALLY A KIND THAT ALREADY EXISTS.**
   A guild needed a name, a description, public standing, an enemy web and a hostile threshold —
   which is a `FactionResource`, entire. Two exported fields made it a guild, and the loader, the
   database, the reputation seeding, the character screen and four validator arms all came along for
   nothing. **42B places hubs, rosters and contacts, and the same question is waiting there**: a
   guild hub needs a name, a position, a map pin and interactable residents — ask what that already
   is before authoring a hub kind.
2. ⚠️ **A NEW FACT ON A SURFACE REVEALS WHAT THAT SURFACE NEVER SUBSCRIBED TO.** The character
   screen had listened to seven events for thirty phases and to `GameLoadedEvent` never, and only a
   fact that arrives as a *flag* could expose it — everything else it drew was restored in place by
   its own component. 42B adds actors whose greeting depends on membership: **the grep before
   shipping is not "does it update when the flag changes" but "what happens after a wholesale load,
   which replays no events at all".**

---

---

## 42B — a hub is a map location and an officer is a placed actor

42A's carry-forward said to ask what kind of thing a hub already is before authoring a hub kind, and
the answer held twice. **A guild's home needs a name, a category, a pin, a cell and discovery rules,
which is a `MapLocationResource` entire; a guild's officer needs a body, a collider, a schedule, a
faction and a conversation, which is an authored `Entity` in a cell scene.** So `FactionResource`
gained five strings — `HubLocationId`, `LeaderNpcId`, `QuartermasterNpcId`, `ContactNpcId`,
`RankPeerNpcId` — and there is no `GuildHubResource`, no `GuildHubComponent` and no second registry.
Map coverage came free: the hub IS a map location, so `ValidateMapMarkersArePlaced` already had it.

**The rank peer is declared and not placed, on purpose.** A peer only means anything once there is a
rank to be a peer of, so the field exists and the four arcs that grant rank one (42C/E/G/I) fill it.
`ValidateGuildHubs` requires the other three and accepts an empty peer, which is the difference
between a deferred role and a stub.

**The Iron Syndicate's contact is Wren Halloway**, who was already standing at the Crossway hiring
post with a service conversation of her own. A roster entry may name an actor who already exists; it
is not a licence to rewrite their conversation, and `--guild-shots` skips the greeting assertion for
an officer whose dialogue declares no guild condition rather than failing on her.

### Membership-aware dialogue without a hand-written flag

`DialogueResource` has one `StartNodeId` and nothing conditions it, so the root line of every officer
is a neutral hail and the two greetings hang off two mutually exclusive choices — the membership-aware
greeting is the first *exchange* rather than the first line. Those choices needed a condition, and
`HasFlag` with a `guild.dawnwardens.joined` argument would have put a DERIVED string into authored
data, which is exactly what invariant 18 forbids — and it would have read a rank flag raw, skipping
the two rules only `GuildRules.Resolve` knows (a player who LEFT is not a member however many rank
flags survive; a rank gap does not promote). So `DialogueCondition` gained **`GuildRankAtLeast` (14)**
and **`GuildNotMember` (15)**, both resolved through `GuildRules`.

⚠️ **`GuildRankAtLeast` always asks for rank 0 in authored data, and the generator has no rank knob.**
The pair has to be EXHAUSTIVE: gate the member branch on rank 1 and a player who has joined but not
yet been promoted matches neither branch, and the root node becomes a dead end for a state the game
can really be in. A rank-gated line is a THIRD branch for the arc that grants the rank, never a
narrowing of this one.

### Retrospective + traps

⚠️ **A LEVELLED PAD IS USUALLY A ROAD.** Every hub was first placed inside an existing `GroundArea`,
on the reasoning that the ground there is already flat and already reached — and it is flat and
reached *because the settlement's road runs through it*. `Area_crossway_compound` is 12 m deep and
`Path_crossway_compound` plus its shoulder is 7 m of that; `Area_hollowreach_street` is 8 m deep and
is 8 m of road plus 2 m of shoulder. Two hubs were built across their cells' roads. **`--validate`
passed. The layout gate passed. The scene audit passed.** The only thing that said so was
`world_traversal_probe.gd`, reporting four authored routes with no navigation path through them and a
capsule snagging on two officers' colliders. Both hubs now stand on their own `Yard` beside the road,
authored in `tools/region_spec_ember.py`. This is NOW.md invariant 21.

⚠️ **THE RENDER FOUND WHAT NOTHING ELSE COULD, THREE TIMES.** The Wardens' Watch and the Ledger House
had their **doors facing away from their own approach** — a keep whose entrance opens onto empty
ground reads as scenery, and no gate has an opinion about which way a door points. A dead pine at
Hollowreach grew **through** the Ledger House's corner while clearing the layout checker's required
distance. And the Undercroft camera, dropped at a literal 1.75 m above the hub's own Y, photographed
**the inside of the pit rim** — invariant 6 from the other side. `GuildShots.OnGround` raycasts every
camera onto the terrain now, as `world_shots.gd` already did.

⚠️ **`DialogueStartedEvent` IS IGNORED OVER A LIVE SESSION, AND THAT MADE A CONFIDENT WRONG FRAME.**
The member greeting shot photographed the stranger shot's still-open node under the member shot's
filename — the exact off-by-one `ShotHarness`'s own header was written about, wearing a different
hat. The guard is right for gameplay (two NPCs must not talk over each other); `DialoguePanel` gained
`EndConversation()` and the harness closes between the two shots.

⚠️ **Adopting a raw model from the library is not a file copy, and the Blender MCP is not connected.**
The `medieval_village` glTFs carry their scale on a **parent node** (`assets/CREDITS.md` records
this), so `fantasy_barracks.glb` imports at roughly two centimetres. Four adoptions were reverted and
the hubs are composed from the already-adapted megakit modules instead — step 1 of the asset ladder,
and cheaper than step 4 was ever going to be. `compose_building.py` gained **`--open`**: three walls,
no front run, no door and no floor, because the terrain is the floor and a laid one would put a 20 cm
lip across the very side the building exists to be walked into.

**Observed, not introduced, and left for a future pass:** `ember_crown.wilds_north`'s steep faces show
concentric chevron banding in the terrain material, and `ember_crown.fen_edge`'s animated water fails
the `visuals` gate's `static peak` metric under `--mode full` while passing standalone and under
`--mode visual`. Both cells' generated terrain is byte-identical to `origin/main` on this branch.

### The Phase 44 placement handoff

Two orders have a hub the current world can hold and a greater house it cannot. 44 owns both, with
these exact ids reserved:

| Guild | Placed now | Phase 44 owes it | Reserved id |
| --- | --- | --- | --- |
| Veiled Archive | `location.embermarket.annexe` — a reading room | the great library, in Sunspire | `location.sunspire.library` |
| Ash Hunters | `location.wilds.lodge` — a field lodge | a field station in the Ashen Wilds | `location.ashen.station` |

Neither is a move: the Annexe and the Deadfall stay where they are and stay owned by their guilds.
44 adds the second house, and `FactionResource.HubLocationId` keeps naming the PRIMARY one.

### Two things worth carrying into the next sub-phase

1. ⚠️ **THE GATE THAT FINDS A PLACEMENT DEFECT IS ALMOST NEVER THE ONE THAT NAMES THE THING YOU
   PLACED.** `--validate`, the layout checker and the scene audit all passed on two buildings sitting
   across roads; the traversal probe found it, and a render found the doors and the tree. 42C places a
   field partner and a civilian threat: **run `world_traversal_probe.gd` and render the result before
   believing a placement, and treat a green `--validate` as evidence about DATA, not about ground.**
2. ⚠️ **AN OFFICER'S GREETING IS DERIVED, SO IT SURVIVES A LOAD FOR FREE — AND ONLY WHILE THAT STAYS
   TRUE.** `--guild-shots` stages membership on all fourteen, loads a save taken before any of it, and
   proves every leader is back to the stranger line, because nothing caches the answer. 42C adds a
   join, a refusal and a probation state: **the moment any surface stores what `GuildRules` can
   derive, the wholesale-load path stops being free and starts being a bug**, and a load replays no
   events to correct it.

---

## 42C — the fork happens before the fight, not after it

The obvious shape for "defend civilians, then choose their fate" puts the choice AFTER the Defend
objective — narratively that is when it is actually decided. It was rejected for the same reason
41D's own header gives: `DialogueCondition` cannot express "objective 0 of this quest is complete",
only whole-quest `QuestActive`/`QuestCompleted`, so a fork gated on mid-quest progress would need a
new condition kind for one piece of content. `quest.hollowreach.barrels`'s answer — decide before the
errand exists — already composes with `SequentialObjectives`: the two Reach objectives that pay off
each branch are gated behind `location.wilds.west`'s Defend regardless of how early Bram Corrow's
fork sets the flag, because sequential order still requires it complete first to become live at all.
Deciding early and letting the mechanism enforce order costs nothing new; inventing a way to ask
"is objective 0 done yet" would have been a new primitive for exactly one piece of content.

**Rank one needed an AND with no compound condition, and the fix was structural, not a new
enum member.** `guild.dawnwardens.rank1` must be granted only once the Reach objective is actually
walked — `QuestCompleted`, not merely the branch flag `HasFlag` already carries from the fork — but a
`DialogueChoice` has one `Condition`. Nesting solved it for free — but the nesting order matters.
As first shipped the branch flag was OUTSIDE (routing into `after_rescue`/`after_expedient`) and
`QuestCompleted` inside; the flag is set at the fork, before the quest starts, so the past-tense node
text was reachable mid-quest (checkpoint fix). Now `QuestCompleted` is outside (`ch_after` into
`after`) and the branch-flag claim choices are inside, landing on the past-tense lines. Rule: the
condition the node TEXT presupposes goes outermost. This is the same trick
`ch_ledger`/`ch_ledger_resume` used in 41B for "available" vs "active" — sequencing through nodes
rather than through conditions.

**Both Reach destinations were already on the map, and that was a design constraint honoured rather
than a shortcut taken.** `location.tarn.landing` (rescue) and `location.crossway.watch` (punitive)
were picked specifically because escorting survivors home to an existing village and marching them
back to the guild's own hub are both places the player already knows how to reach — reusing them is
what makes the consequence legible instead of sending the player to a new pin that means nothing yet.
Zero new `MapLocationResource`s, zero region edits, zero world bake — the whole sub-phase is dialogue,
one quest and one placed actor.

### Retrospective + traps

⚠️ **A GUILD'S WRITE PATH IS THE SAME STRING ITS READ PATH DERIVES, AND THAT IS EASY TO GET
BACKWARDS.** Invariant 18 forbids reading a `guild.*` flag by hand (`HasFlag`/`MissingFlag` with a
`guild.*` argument skips `GuildRules`' cumulative-rank and left-still-a-member rules) — it does not
forbid WRITING one, because there is no other way to write it: `StoryFlagsComponent` is the only
writer and a `DialogueEffect.SetFlag` choice is the only authored path to it. 42C is the first content
to actually exercise that write side (42A/B built only the resolver and the console mutator). The
rule that matters going forward: every `guild.<slug>.<suffix>` string written by hand must match
`GuildRules`' own derivation EXACTLY (`GuildRulesTests` pins the five strings per guild) — a typo
here is silent forever, the same failure mode `ValidateStoryFlags` already catches for `flag.*` but
cannot catch for the `guild.*` family, since nothing enumerates what `GuildRules` would have produced.
⚠️ **THE RANK-GATED THIRD BRANCH GOES INSIDE AN EXISTING NODE, NOT ON ROOT.** 42B's header warned
the next rank-granting arc to add rank-aware content "as a THIRD branch rather than by narrowing"
the member/stranger pair — read literally as a third ROOT choice, `GuildRankAtLeast:1` and
`GuildRankAtLeast:0` are NOT disjoint (a rank-1 member satisfies both), so a third root choice would
sit permanently alongside the ordinary member greeting rather than replacing it. The Serjeant's
`ch_ranked` is instead one more choice INSIDE `member`, which is what "third branch, never a
narrowing" actually means: additive within the branch already reached, not a second gate at the door.

### Two things worth carrying into the next sub-phase

1. ⚠️ **A FORK DECIDED EARLY STILL NEEDS ITS PAYOFF GATED LATE.** Setting a branch flag at an offer
   node is free and instant; the reward it eventually authorizes is not, and conflating "the flag is
   set" with "the thing the flag promises has happened" is the gap 42C closed by nesting a
   `QuestCompleted` check inside the flag-routed node rather than reading the branch flag alone. 42D's
   finale is a bigger version of the same shape — a command dispute resolved by a choice, paid off by
   a much later objective — and the fix is the same nesting trick, not a new field.
2. ⚠️ **TWO RANK-GRANTING ARCS ON ONE GUILD WILL WANT THE SAME "THIRD BRANCH" SLOT.** `ch_ranked` on
   the Serjeant is gated on rank 1 alone; 42D grants a HIGHER rank on the same faction, and a second
   rank-aware reaction authored the same way (`GuildRankAtLeast` at the new rank) will sit alongside
   the first rather than replace it, exactly as the guidance intends — but only if the new one is
   authored as its own additive choice inside `member`, not as an edit to `ch_ranked`'s own condition.
   Narrowing an existing rank-gated choice upward is the same mistake invariant 18's own warning was
   written about, one level later.

---

## 42D — a re-visitable choice must never author a clearing effect

The carry-forward above called it exactly: the finale's payoff needed `QuestCompleted` nested outside
its branch flag, the same trick 42C used. What the carry-forward did not anticipate is that 42D grants
a rank via a SECOND mechanism 42C never had to worry about — `DialogueEffect.GuildRank` (added in
42I, after 42C shipped) — and that mechanism has a property `SetFlag` does not: **it clears every
rank flag above the one it names.** That is exactly right for a one-shot promotion and exactly wrong
for a choice a player can walk back to.

`ch_command_after` (member → "About the warrant -", gated `QuestCompleted quest.dawnwardens.command`)
stays visible forever once the command quest is done, the same way `ch_probation_active`'s siblings
already do — nothing in this dialogue shape ever hides a completed-quest choice again. First authored
with `Effect = GuildRank "faction.dawnwardens:2"`, it was correct on the FIRST visit and a silent
demotion on every visit after the finale: a player who finished the command arc, talked to Bram (rank
two), finished the finale (rank three, via a SEPARATE `GuildRank ":3"` on `ch_finale_done`), and then
re-opened the same "About the warrant -" line would have `GuildRank ":2"` fire again and **clear rank
three** — a guild rank silently regressing on a conversational dead end nobody would think to blame.
Caught before `--validate` (which cannot see this — both effects are individually well-formed) by
tracing the graph by hand for every choice a completed-quest gate leaves permanently open. The fix:
`ch_command_after` uses `Effect = SetFlag "guild.dawnwardens.rank2"` instead — the exact write-side
shape 42C already established for rank one, which only ever ADDS a flag. `ch_finale_done`'s own
`GuildRank ":3"` stays, because 3 is `faction.dawnwardens`' declared ceiling (`RankNameKeys.Count`)
and there is nothing above it left to clear — which is also why `GuildRank` back-fills ranks one and
two on its own the moment the finale is claimed, whether or not the player ever visited
`ch_command_after` at all: the two grants are redundant on the happy path and neither can leave a gap.

**The Iron King dispute reads a flag it is forbidden to touch, and the read had to happen at the
front door, not inside the quest.** `flag.iron_king_defeated` has no in-game consumer of its own
worth spending — the Build note is explicit that this arc must never gate or consume it — so the only
honest use left is flavour: two node texts (`command_offer_alive`/`command_offer_defeated`), picked by
`HasFlag`/`MissingFlag` on the SAME node's two exit choices, both landing on the identical
accept/decline pair beneath them. Nothing about availability, objectives or rewards differs; only
what Bram calls the warrant-rider's authority does.

### Two things worth carrying into the next sub-phase

1. ⚠️ **A CHOICE A COMPLETED QUEST LEAVES PERMANENTLY OPEN MUST NEVER CARRY A CLEARING EFFECT, ONLY
   AN ADDITIVE ONE.** `GuildRank` is the right tool exactly once, at the top of a guild's rank
   ladder where nothing above it can ever be cleared; anywhere a promotion can be reached by more
   than one route, or revisited after a later promotion, `SetFlag` on the exact `GuildRules`-derived
   string is the only effect that is safe to fire twice. The next rank-granting arc on a SECOND
   guild-hub choice (42B's carry-forward already flagged the third-branch slot as reusable) inherits
   this the moment it grants anything short of the ceiling from a node the player can walk back to.
2. ⚠️ **A DERIVED-PROSE FLAG (`flag.iron_king_defeated`) STAYS READ-ONLY BY PUTTING THE BRANCH AT THE
   NODE LEVEL, NOT THE EFFECT LEVEL.** There is no `DialogueEffect` that could safely touch a
   main-story flag from guild content even if one wanted to, so the only place "vary by an external
   flag without gating on it" can live is two node TEXTS reached by two mutually exclusive
   `HasFlag`/`MissingFlag` choices that reconverge immediately. Any future arc that wants to
   acknowledge an Act I/II main-story fact without spending it should reach for this shape rather than
   inventing a new condition or effect for a flag that already has an owner.

---

## 42E — knowledge is the hunter identity, and the ground was already waiting

42E landed as Ash Hunters' join arc as well as its induction quest — nothing in the roadmap before it
had ever offered `guild.ash_hunters.joined`, so the first question was the same one 42A closed for
guilds in general: **what kind of thing is "an induction quest with a spare/kill choice", and does it
already exist?** It does, twice over: a lair boss with a `DefeatFlagId` (35D/35F, the Ash dragon
shape) and a two-fork dialogue whose choices gate each other on the sibling's absence
(`quest.hollowreach.barrels`, 41D). `enemy.grimtusk` is a Grimtusk-shaped `EnemyArchetypeResource`
copy of `enemy.thornback_boar` — same stats, same model, different id — placed once via
`LairSpawnComponent` in the Deadfall thicket `tools/region_spec_ember.py` had already yarded and
never furnished (`Area_wn_deadfall`, authored in the 2026-08-28 layout rebuild and empty since). No
new objective type, no new resource kind, no new flag family beyond `flag.ash_hunters.*`.

**The distinct template id is the load-bearing decision.** `encounter.boar_territory` rolls
`enemy.thornback_boar` anywhere the region allows, and a Kill objective matches by `TemplateId`
alone — sharing an id would let any roadside boar the player happens to kill satisfy a quest that is
supposed to be about one named, placed individual. `enemy.grimtusk` exists so the two populations can
never be confused for each other, in either direction: the roaming population can't finish the quest,
and the quest's Kill objective can't be satisfied by a species-wide cull.

### The soft-lock that never shipped

The first draft used `SequentialObjectives = true` to make the objective ORDER read as the
investigation-then-verdict story: two clues, a debrief, then the kill. It built, it validated, and it
was wrong. A Kill objective fires off a **one-time `EntityDiedEvent` that never replays**, and
`QuestProgress.IsObjectiveActive` on a Sequential quest requires every earlier LIVE objective done
before a later one is even current — so a player who found the den before finishing the clues or the
debrief would kill Grimtusk while the objective wasn't listening, and nothing afterward could ever
catch up. The quest would sit open forever with a corpse it could not credit, behind a green
`--validate` that has no way to simulate an out-of-order kill. ⚠️ **A Kill objective and
`SequentialObjectives` do not mix unless the kill is authored LAST with nothing live after it** — the
"target killed early" verify line this sub-phase was given is exactly the case that ordering breaks,
which is presumably why it was named. The fix was to drop `SequentialObjectives` entirely and let
`RequiredFlagId`/`ForbiddenFlagId` alone carry the branch, the same shape
`quest.hollowreach.barrels` already proves: a kill counts whenever it happens, spare-marked or not,
quest-started or not.

### Two things worth carrying into the next sub-phase

1. ⚠️ **A COMPANION'S KILL DID NOT CREDIT THE PLAYER, AND NOTHING HAD EVER NOTICED.**
   `QuestLogComponent.OnEntityDied` required `e.Killer` to be `ReferenceEquals` the quest log's own
   `Entity` — true for the player, never true for a companion, whose `CharacterActionComponent` packets
   carry the companion as `Source`. Every existing Kill objective in the game has been silently
   uncompletable by a companion's final hit since Phase 32C, and nothing caught it because no quest
   before this one was verified against that state on purpose. Fixed at the one choke point
   (`e.Killer is Companions.CompanionEntity` now also credits), which is the shape 42B's own
   carry-forward predicted: a new fact on a surface reveals what that surface never subscribed to,
   and here the surface was an existing rule nobody had exercised rather than a new one. 42F reuses
   Kill objectives against a dragon a companion may well land the last hit on — the fix already
   covers it, but **verify it again there rather than assuming this note is enough**.
2. ⚠️ **A NEVER-FURNISHED YARD IS FREE GROUND, AND IT IS WORTH CHECKING BEFORE AUTHORING A NEW PAD.**
   `Area_wn_deadfall` and its extension yard were sitting in `region_spec_ember.py` since 42B's own
   predecessor pass, levelled and clear of every route, with nothing ever placed on them — the cell's
   own header comment called the Deadfall "ambush ground" and left it at that. Reading the region spec
   before reaching for `compose_building.py`'s pad-authoring step (the 42B recipe's step 2) saved an
   entire yard-and-regenerate cycle. **Grep the target cell's `Yard(...)` calls for one already shaped
   right before authoring a new one** — a levelled pad with nothing on it is not always a road (the
   42B trap); sometimes it is simply unclaimed.

---

## 42G — the seams were all already there

42G is the sub-phase where the "cheap kind that already exists" question (42A's own carry-forward)
paid off hardest: every noun the entry asked for — a lost tome, a ley site, the Ancient's knowledge —
turned out to be something the repo already had, half-written into 42B's own flavour text or 35F's
own dragon content, waiting for a quest to point at it. **Zero new scenes, zero new items, zero new
spells, zero new flags, zero new C#.** The whole sub-phase is three `.tres` quests, two edited
dialogue graphs and one `strings.csv` block.

**The admission trial is Tam Quillfellow's own shop comment, read literally.** `EmbermarketScriptorium.
tres` has carried this line since 38L: "The Treatise on Wardings sits behind Honored... a book the
Veiled Archive would rather nobody had copied." Nothing before 42G ever spent it. `quest.veiled_
archive.lost_tome` is that line turned into an errand, and Keeper Ysolde Marr's own 42B stranger
greeting — "a person joins us by bringing us something we did not have" — is quoted almost verbatim
in the offer node, because it already said exactly what the trial needed to say.

**The ley-site survey is Ferris Vail's own 42B member line, and it named its own destinations.**
"Two of these say the ley line under the market runs north. The third was written by somebody who
walked it" was authored in 42B with no quest behind it. `quest.veiled_archive.ley_survey` is that
walk: two Reach objectives at the Ember Crown and Frostfang waystones (39.5A travel nodes, already
mapped, needing no new placement) which happen to sit at opposite ends of `RegionResource.
WeavePotency` (1.0 and 0.5) — the "low/high-potency regions" Verify line is two existing field
values on two existing `.tres` files, not new content.

**The Ancient-knowledge lead adds no dragon content because 35F already built both outcomes.**
`dialogue.ancient_dragon` teaches `spell.elder_word` whether the Ancient is spared (the favour,
closed by `flag.ancient.taught`) or killed (the hoard `SpellTomeComponent`, gated on
`flag.ancient_dragon_defeated` — raised by `LairSpawnComponent.DefeatFlagId` the instant it dies,
independent of whether the tome is ever actually read). `quest.veiled_archive.ancient_lead`'s two
report objectives read those same two flags as `RequiredFlagId`, unmodified — the
`quest.ash_hunters.grimtusk` shape (41D: two Talk objectives on one dialogue, each behind a different
flag, only one ever live) applied to content two guilds does not own. This is also what makes
"duplicate spell reward" free: the Archive's own `LearnSpell` grant on rank one is a safety net for a
player who never revisited the hoard tome, and a no-op for one who already knows the word from the
Ancient directly — `SpellcastingComponent.Learn`'s existing idempotency, exercised, not extended.

⚠️ **A REPORT OBJECTIVE ON THE SAME NPC WHO OFFERS THE QUEST NEEDS `SequentialObjectives`, EVEN WHEN
NEITHER OBJECTIVE IS Kill.** 42E's soft-lock was Kill-plus-Sequential; this sub-phase's near-miss was
the opposite shape. `lost_tome` and `ley_survey` both report to the same officer who hands them out
(Ysolde Marr and Ferris Vail respectively), and a Talk objective completes on *any* end of its
dialogue (the repo-wide rule, not new to 42G) — so an unordered report objective would have ticked
the instant the *offering* conversation itself ended, crediting a report for work never done.
`ancient_lead`'s two report objectives sit on a *different* NPC from the one who offers the quest
(Ferris offers, Ysolde hears it), which is exactly why that one needed no ordering bool at all —
matching `quest.ash_hunters.grimtusk`'s own report pair, which has the same property for the same
reason. **The question worth asking before reaching for `SequentialObjectives` on a report objective
is not "is there a Kill in this quest" but "is the report on the same dialogue as the offer."**

### Two things worth carrying into the next sub-phase

1. ⚠️ **BEFORE AUTHORING A NEW PLACE, ITEM OR SPELL FOR A GUILD ARC, GREP THE PRIOR SUB-PHASES' OWN
   FLAVOUR TEXT FOR THE THING THE BRIEF IS ASKING FOR.** 42B's officer greetings and 38L's shop
   comment both named exactly what 42G needed two phases early, in plain prose, because a writer who
   already knew the guild's shape wrote lines that assumed the payoff existed. 42H's finale should
   grep `dlg.archive_keeper.*`/`dlg.archive_reader.*` and this sub-phase's own new nodes before
   inventing a location or an item — the seam is very often already sitting in `strings.csv`.
2. ⚠️ **TWO EXISTING OUTCOME FLAGS FROM AN UNRELATED SYSTEM ARE A COMPLETE BRANCH, AND REUSING THEM
   UNMODIFIED IS THE POINT.** `flag.ancient.taught` and `flag.ancient_dragon_defeated` were authored
   for 35F's own dragon-favour content with no guild in mind; 42G read them as `RequiredFlagId` and
   changed neither. 42H's truth-custody finale will want to know what the player ultimately DID with
   `spell.elder_word` and the Ancient's fate — the answer is almost certainly already sitting in these
   same two flags rather than a new one, exactly as this sub-phase's own reuse was.

---

## 42I — Iron Syndicate contract rank

42A and 42B closed off the seams a guild would want; 42I is the first sub-phase to actually WALK one
of them, and the walk found the one seam that did not exist yet: **nothing could write a guild flag
from dialogue.** `GuildRules` derives every flag name and `DialogueCondition.GuildRankAtLeast`/
`GuildNotMember` (42B) already read them safely, but the only code that ever *set* one was the `guild`
console command — which needs keyboard input no headless path reaches. Two new `DialogueEffect`
members, `JoinGuild` and `GuildRank`, are the whole of the new code: both call `GuildRules`'s own
flag-name builders and `GuildRules.CanJoin`, exactly mirroring what `DevCommands` already did, so a
guild id or a rank number is never a hand-typed string in a `.tres` (invariant 18) on the write side
either. 42C/E/G will use both unchanged.

**Recruitment routes through the roster, not a new surface.** Broker Ilder Vance's existing
`GuildNotMember` branch grows an accept/decline pair (`JoinGuild`); rank one is a `QuestCompleted`
check on the *last* contract in the chain, on his `member` branch. Dallow Grieve's `member` branch
grows GuildBoard's own offer/active/thanks triple (41B), three times over, for the bounty, the escort
and the judgment. **No new board, no new panel — the "contract board" the entry asks to recruit
through IS this conversation graph**, the same shape `dialogue.guild_board` already proved.

**"Varied paid work" is a `PrerequisiteQuestId` chain, not a flag AND.** `quest.iron.bounty` →
`quest.iron.escort` → `quest.iron.judgment`, each naming the last as its prerequisite. That turns
"complete all three, in any combination" — which nothing here can express, since a `DialogueChoice`
carries one `Condition` — into "complete the last one," a single `QuestCompleted` check the rank-grant
choice already needed. **Rotation is the visible half of the same fact**: only one of the three
`QuestAvailable` conditions is ever true at once, so the postings visibly advance rung to rung, and
the "rotation overlap" Verify case is that the two already-closed rungs keep their own `thanks` line
live rather than vanishing — exactly GuildBoard's existing behaviour, exercised by a chain instead of
two independent quests.

**The spare/kill/dead-early shape is two objectives, not a state machine.** `Obj_kill` (Kill,
`ForbiddenFlagId` = the spare flag) is live from the moment the quest starts, so killing Corran Vess
before ever opening his dialogue satisfies it with zero new code — that is what makes "target dead
early" free. `Obj_spare` (Talk, `RequiredFlagId` = the same flag) is the ordinary "gated-off objective
is inert, not incomplete" mirror. `AllowsOneShotTarget = true` is the one field that has to be paid
for honestly: the promise it makes ("the offering conversation gates on the target still being
alive") is kept by a `MissingFlag`/`HasFlag` pair on Grieve's offer node, reading Corran's own
`LairSpawnComponent.DefeatFlagId` — the *only* thing in the game that turns a kill into a flag (35F),
reused here at one-actor scale instead of dragon scale.

⚠️ **CONTRABAND AND PUBLIC STANDING WERE ALREADY BUILT; THIS SUB-PHASE ONLY HAD TO POINT AT THEM.**
Corran's `FactionId` is `faction.villagers`, not `faction.outlaws` — he is a private citizen the
Syndicate wants found, not a criminal the realm is hunting — so killing him costs *public* standing
automatically through `ReputationComponent.OnEntityDied`, and his loot table is `BanditLoot.tres`
unchanged, which already carries two contraband entries from 38O. Neither needed a line of new code;
the entire "contraband/public standing integration" Verify line is two field values. The escort quest
pays the same idea from the other side: `FactionRewardId = faction.villagers`, negative — running the
Syndicate's cargo through town costs the same standing a contraband sale would, through the ordinary
`QuestResource.FactionRewardId` field.

⚠️ **NEITHER SPARE/KILL PATH PAYS DIFFERENTLY, FOR WHATTHEPOSTTOOK'S REASON (41D).** `QuestResource`
has one `GoldReward`/`XpReward`/`FactionRewardId` for the whole quest, so the ending is the flag
(`flag.iron.judgment_target_dead` vs `flag.iron.judgment_spared`) and nothing else — a per-branch
reward table would be invariant 5 waiting to happen, and it is exactly what 42J's finale needs to read
regardless.

### Retrospective + traps

⚠️ **A `DialogueChoice` CARRYING ONE EFFECT KEEPS SHOWING UP AS THE BINDING CONSTRAINT, AND THE FIX IS
ALWAYS THE SAME TWO-NODE SPLIT.** The escort's accept-then-recruit (Sedge/Tessa's shape, 41B) and the
rank-grant's own single-effect confirm both hit it again. There is no AND of two conditions either
(the "all three contracts done" question), and the chain-via-`PrerequisiteQuestId` answer above is
the cheap-kind-that-already-exists instance of that same constraint, not a new mechanism.

⚠️ **AN IDEMPOTENT EFFECT LETS A NODE SKIP THE `HasFlag`/`MissingFlag` PAIR IT WOULD OTHERWISE NEED —
BUT ONLY IF THE TEXT IS WRITTEN TO SURVIVE BEING SHOWN TWICE.** The honest way to gate "not yet rank
one" against "already rank one" would read the rank flag directly, which is exactly what invariant 18
forbids doing from a `.tres`. `GuildRank`'s effect is a no-op on a flag that is already set, so the
Broker's advancement node stays reachable and correct on a second visit; the line it speaks was
written as a standing statement of respect rather than a one-time announcement, on purpose, so
repeating it is not a bug. A future rank-grant that wants a genuinely one-time line will need a real
answer to this, not this shortcut.

⚠️ **THE MARK IS A `LairSpawnComponent` AT ONE-ACTOR SCALE, AND MOST OF 35D'S RECIPE DOES NOT APPLY AT
THAT SCALE.** No custom landform, no dedicated cell, no `roost.tscn` inheritance — Corran stands a few
metres off the Ledger House's own already-proven Yard, and `TerritoryRadius` on a new `ai_profiles`
variant (`ai.mark_guard`) is the only thing 35D's recipe insisted on that still applied (a leash, so a
fight that starts here does not chase into the next cell). `world_traversal_probe.gd` still passed
(336 routes) with nothing new on it, since nothing was placed on a road — but this sub-phase did not
render the placement, which 42B's own carried-forward finding says not to skip. That is this
sub-phase's own gap to hand forward.

### Two things worth carrying into the next sub-phase

1. ⚠️ **A GUILD'S WRITE PATH DID NOT EXIST UNTIL SOMETHING NEEDED IT, AND 42C IS NEXT.** `JoinGuild`
   and `GuildRank` are general — any guild id, any rank — precisely because the console command they
   mirror already was. 42C's probation and 42E/42G's rank ones should call them unchanged rather than
   re-deriving the same four flag writes a third and fourth time.
2. ⚠️ **THIS SUB-PHASE DID NOT RENDER ITS OWN PLACEMENT, AND 42B ALREADY SAID WHAT THAT COSTS.**
   `--validate` and the traversal probe are evidence about data and about routes respectively; neither
   looks at a door, a lean, or a standing collider the way a camera does. Corran and Rook Sallow sit
   on ground nothing has photographed. 42C's field partner and civilian threat should get the render
   42B's own finding asked for, and if this placement turns out to need correcting, that correction is
   this same trap firing a third time.
3. ⚠️ **(Checkpoint fix) `Goto` FIRES EVEN WHEN THE EFFECT REFUSES.** `DialogueSession.Choose` applies
   the effect then navigates unconditionally, so a refused `JoinGuild` (left member, `RejoinAllowed =
   false`) reached "you're in". Any `JoinGuild` choice with a `Goto` now needs `Condition = 16`
   (`GuildCanJoin`, same faction id); `--validate` enforces it.

## Integration 1 checkpoint fixes

- **42C:** probation after-lines were gated only on the fork flag (set before the quest starts); now
  `QuestCompleted` outside, branch flag inside (see 42C retrospective).
- **42E:** `Obj_debrief` (Talk on Halda) completed on the conversation that STARTS the quest. Gated on
  `flag.ash_hunters.grimtusk_clues_read`, set by whichever clue dialogue is read second (each clue
  has two "Note it." choices keyed on the other clue's flag). Known edge: a gate-shut objective is
  inert, so a player who has done everything else and reads the second clue last completes without
  the debrief — harmless, the investigation was still done.
- **42I:** new `DialogueCondition.GuildCanJoin` (16), required by `--validate` on navigating `JoinGuild`.
- **Lifecycle FATAL `gchandle.is_released()`:** the GC finalizer thread disposed the old session's
  wrapper of a cached C# Resource (the progression curve — the crash log shows its `GD.Load` failing on
  the same frame) while the next session's `Build` re-loaded it. `BeginSession` now blocks on
  `GC.Collect/WaitForPendingFinalizers` after `DestroySession`. 1/3 -> 4/4. Content volume only moved
  the timing; 42I's data did not cause it.

Two things worth carrying: put the condition the node text presupposes outermost when nesting; and
any per-session `GD.Load` of a C# Resource is exposed to the finalizer race if session start ever
stops draining finalizers.

---
