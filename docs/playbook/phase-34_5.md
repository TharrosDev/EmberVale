## Phase 34.5 — Frostfang Clans & Beast-Race Factions `[F/C]` ✅ **complete**

> LORE names Frostfang's warrior clans/beast races as a culture, not generic
> wildlife. Give them a faction identity before they dissolve into the bestiary.
>
> It landed as one: a hold you can walk into, warriors who ignore you until you
> give them a reason, and a rank chain that moves your standing both ways. The
> authoring recipes are docs/RECIPES.md; this block is the log.

- [x] **34.5A — Frostfang Clans `FactionResource` + hub presence** `[F/C]` ✅
  - **Done when:** the clan faction exists with a hub/outpost; reputation/dread
    (23G) applies to it like any faction.
  - **Landed:** `faction.frostfang_clans` (`data/factions/FrostfangClans.tres`) —
    `DefaultReputation 10`, `Enemies` the Hollow, `Allies` the beasts. Its
    `HostileThreshold` is **`1` (Hostile), not the usual `2`** — at `2` a merely
    *Touched* player (dread −5) would arrive at a hostile hub, which makes the
    whole hold unenterable for a corruption the game treats as minor. Reputation
    and dread need no wiring: `ReputationComponent` seeds every faction in the
    database, so the clans appear in the character screen the moment the `.tres`
    lands.
  - **Landed:** the clan hold —
    `scenes/regions/frostfang_reach/clan_hold.tscn`, a new `frostfang_reach.clan_hold`
    cell at `(100, 0, −20)`. Town-hub parity: navmesh + baker, three longhouses,
    three tents, four braziers (white-blue, per `ART_STYLE.md`'s Frostfang light),
    dead pines/rocks/glaciers, all three crafting stations, a waystone, and four
    NPCs tagged `faction.frostfang_clans` — Hjalvar Stormbound (chief), Sigrun
    Ironhand (quartermaster), Yrsa Houndmother (beast-tamer, seeding 34.5B) and
    Old Vetle (hearthkeeper). Each has a `Loc`-keyed conversation and a schedule.
  - **The cell carries its own floor, and has to.** `GameBootstrap.BuildEnvironment`
    builds one 80 × 80 ground plane at the world origin; Frostfang sits at x ≈ 100,
    so outside a cell's own greybox there is nothing under you but the infinite
    `WorldBoundaryShape3D`. The hold's 60 m floor is sized to cover the region
    `SpawnPoint (100, 1.2, 0)` as well, so you arrive standing on it.
  - **Two region edits with teeth:** the glacier cell moved from `(100, 0, −14)` to
    `(100, 0, −60)` (its ice props sat inside the new floor), and Frostfang finally
    has a safe zone — `SafeZoneCenter (100, 0, −20)`, radius 30 — without which the
    `EncounterDirector` spawns wolves in the middle of the hold.
  - **Schedule destinations are absolute world space**, not cell-local, so every
    entry in the four `data/schedules/Clan*.tres` is authored around x ≈ 100. This
    is the trap to remember when 34.5B/C add more clan NPCs.
  - **Verified:** `dotnet build` clean, `dotnet test` 611/611, and
    `--validate` exits 0. The cell scene was additionally load-checked headless
    (`load(…).instantiate()` → 9 children) because `ContentValidator` only proves a
    cell's `ScenePath` *exists*, never that it parses.
  - **Still owed (maintainer, at the keyboard)** — the `F1` console and `M`/`I`
    screens no remote session can drive:
    - Walk the hold: ground renders, the four NPCs stand on it, `E` opens each
      conversation, the waystone registers a fast-travel node, `M` shows a
      **Clan Hold** POI.
    - The Done-when itself: character screen reads *Frostfang Clans — Neutral*;
      `rep faction.frostfang_clans -80` turns the hold hostile; raising corruption
      subtracts the Dread line from it like any other faction.
    - Stand in the hold at night for a minute — the safe zone should keep the
      ambient spawner out.
  - **Known limits:** no clan combatants exist yet (34.5B), so killing a clansman
    means killing a peaceful NPC; the hold is still reachable only behind
    `flag.iron_king_defeated`; and `TravelNodeComponent.RegionId` is not validated
    (`ContentValidator.cs:757`), so that one field fails silently if it ever drifts.
- [x] **34.5B — Clan archetypes (raider, beast-tamer, shaman)** `[C]` ✅
  - **Done when:** three clan archetypes exist on the Phase 34 matrix with
    distinct loot/AI profiles.
  - **Landed:** `enemy.clan_raider` (`ai.shielded`, poise 75, steel sword),
    `enemy.clan_beast_tamer` (`ai.pack_flanker`, fast and thin), and
    `enemy.clan_shaman` (`ai.caster`, frost nova + lesser heal) — three distinct
    existing AI profiles, three distinct loot tables, no new profile file and no
    new weapon. All three carry `FrostResist` 35–60, which is what the Reach's
    creatures should cost a fire build, free off 34E.
  - **They are neutral, and that is the feature.** The clans sit at Neutral
    standing after 34.5A, so `EnemyAIComponent.PlayerIsTarget` returns false and a
    clan patrol *ignores you*. Hit one and it fights back; drop your standing and
    the whole faction turns. The archetypes are the first actors in the game that
    are hostile-team but not hostile.
  - **Two bugs that had to be fixed for that to be true**, both root-caused rather
    than worked around:
    - **Companions attacked neutrals.** `CompanionAIComponent` targeted on team
      alone, and `EnemyArchetypeFactory` builds *every* archetype on the hostile
      team — so Kael would have opened fire on a clansman on sight and started a
      war the player never chose. `PlayerWouldFight` now gates the proximity scan
      on the player's standing, mirroring `EnemyAIComponent.PlayerIsTarget`. It
      deliberately does **not** gate the lock-on focus or the
      `OnDamageDealt` reaction, so assisting a fight the player starts and
      defending one they didn't both still work, and an unfactioned actor is
      hostile exactly as before.
    - **Encounters had no region filter** — the known limit logged under Phase 34.
      `EncounterResource.RegionIds` (**empty = anywhere**, so all 28 existing files
      were untouched) plus one predicate in `EncounterDirector.PickEligible`, fed
      by a new `RegionStreamer.ActiveRegionId`. The streamer is re-`Configure`d at
      both places the region changes, so it needed no `GameBootstrap` edit and no
      new file. `encounter.frost_stalker` and `encounter.rime_drift` are now gated
      to Frostfang, which takes 0.75 of weight out of the Ember Crown pool 34F.5
      tuned — the valley loses two creatures that never belonged there.
  - **The validator got stricter again**, same habit: an encounter naming an
    unknown region now fails `--validate`. A typo there would otherwise narrow the
    encounter to nowhere, and the only symptom is a creature that quietly stops
    appearing. **Proven by making it fail** before it was trusted.
  - **Verified:** `dotnet build` clean, `dotnet test` 611/611, `--validate` exits 0
    (26 archetypes, 29 templates, 29 bestiary entries, 31 encounters, 488 strings).
  - **Still owed (maintainer, at the keyboard)** — all of it needs `F1`:
    - `region goto region.frostfang_reach`, `spawn 1 enemy.clan_raider` — it should
      ignore you until you swing, then fight.
    - **With Kael recruited, spawn a clansman beside him — he must not open fire.**
      This is the fix most likely to be wrong.
    - `spawn 1 enemy.clan_shaman` — casting proves the mana pool landed (a caster
      with no mana just stands there, silently); mending a hurt clansman proves the
      ally-heal path.
    - `rep faction.frostfang_clans -80` → all three turn hostile on sight.
    - Stand in the Ember Crown a few minutes: no clan patrol, no frost stalker, no
      rime shard. Then the same in Frostfang outside the hold's 30 m safe zone.
  - **Known limits:** **one encounter = one template id** still holds, so a
    beast-tamer cannot spawn *with* her stalkers — `encounter.clan_hunt` and the
    now-Frostfang-only `encounter.frost_stalker` overlap by chance instead, the
    same compromise 34G recorded for the necromancer and its husks. And the region
    gate is a whitelist on encounters only; world events are still global.
- [x] **34.5C — Clan questline + rank chain** `[C]` ✅
  - **Done when:** a short multi-quest arc with rank-up flags is completable;
    `validate-all` green.
  - **Landed — the rank chain:** three links on `PrerequisiteQuestId`, one rank
    each. `quest.clan.proving` (Hjalvar; break 4 `enemy.rime_shard`, the one
    creature that is both Frostfang-only and hostile) → **`flag.clan.named`** ·
    `quest.clan.stores` (Sigrun; 5 beast pelts) → **`flag.clan.sworn`** ·
    `quest.clan.hollow` (Hjalvar; 5 `enemy.hollow_husk`, the faction's declared
    `Enemies`) → **`flag.clan.hearth_kin`**. Nothing in the arc asks you to kill a
    frost stalker or a clansman: the tamer's own 34.5A line makes stalkers
    clan-raised, and `faction.beasts` is a clan ally.
  - **The fiction was already written and unfired.** Hjalvar: *"a name is what you
    carry, not what you are given."* Sigrun: *"come back when the hold knows your
    name."* Her line is now a literal `HasFlag flag.clan.named` gate — she refuses
    to trade until the hold has named you, which is what she always said.
  - **Landed — the betrayal branch:** `quest.clan.exile.proof` (kill 3
    `enemy.clan_raider`) → `flag.clan.oathbreaker`, then `quest.clan.exile.rite`
    (kill 2 `enemy.clan_shaman`) → `flag.clan.bloodfeud`. Given by a new NPC,
    **Halvar One-Hand**, an exile camped at his own fire in the hold's far corner —
    a rival faction would have needed an NPC from nowhere; an exile explains
    himself. He has no `FactionComponent`: he is nobody's.
  - **The branch pays in Syndicate standing, not negative clan standing.** Killing
    clansmen already costs 12 a head automatically, so the two contracts cost ~36
    and ~24 clan reputation on their own; adding a negative quest reward would have
    been charging twice for one act. It also closes the branch behind you — enough
    kills and the hold turns hostile and stops talking, exactly as 34.5B designed.
  - **Mutual exclusivity is two `MissingFlag` gates**, no new machinery: the chief's
    work hub needs `MissingFlag flag.clan.oathbreaker`, the exile's needs
    `MissingFlag flag.clan.hearth_kin`. A `DialogueChoice` has **one** `Effect`, so
    it cannot both start a quest and set a flag — the flag rides on the following
    node's farewell choice, the `Elder.tres` shape.
  - **Quests can now move reputation.** `QuestResource` gained
    `FactionRewardId`/`FactionRewardAmount`, mirroring `WorldEventResource` field
    for field, applied in `GrantRewards` **before** the no-inventory bail (standing
    is owed whether or not you can carry anything). That is what makes rank visible
    with **no UI work at all** — the character screen already lists the clans, so
    the arc walks the tier Neutral → Friendly → Honored. Phase 42A still owns the
    real rank framework and display; this is the field it will build on.
  - **The validator got stricter again, and this one was overdue.** Story flags are
    the only id family with no database behind them, so nothing had ever checked
    them: a mistyped `HasFlag` is a gate that never opens, silently and for good.
    `ValidateStoryFlags` now cross-references readers against writers — dialogue
    `HasFlag`/`MissingFlag` args and `RegionResource.UnlockFlagId` against every
    `SetFlag`/`ClearFlag` effect plus the three code constants. The reverse is
    *not* an error: a flag set and never read is a legitimate record of what
    happened. **Proven by making it fail** on a doctored `flag.clan.namd`.
  - **Verified:** `dotnet build` clean, `dotnet test` **619/619** (the per-file
    dialogue suite picked up the new conversation), `--validate` exits 0 with the
    full graph battery — 13 quests, 12 conversations, 585 strings. The edited cell
    scene was load-checked headless again (11 children, exile and his fire present).
  - **Still owed (maintainer, at the keyboard)** — needs `F1` and the `I`/`J` screens:
    - Walk the loyal arc: accept the proving from Hjalvar, `spawn 4 enemy.rime_shard`,
      kill them, turn in. The journal tracks it and the character screen's
      **Frostfang Clans** line climbs.
    - Sigrun must **refuse** before `flag.clan.named` and offer the stores after it.
      That gate is the whole point of the rank chain.
    - Finish link 3: the chief greets you as hearth-kin, Yrsa and Old Vetle have new
      lines, and **the exile's offer is gone**.
    - On a separate save, take Halvar's contract instead: Syndicate standing rises,
      clan standing falls ~36, and the chief's work hub disappears.
    - `F5`/`F9` across a rank-up — flags and quest progress are separate `ISaveable`s
      and both must survive.
  - **Known limits:** objectives are still only Kill/Collect, so "go and speak to
    someone" cannot be an objective — every turn-in is a conversation the player has
    to remember to have. Rank is invisible outside dialogue and the reputation tier
    it grants (42A owns a real rank display). And a quest completed once can never
    be re-taken, so the arc is one-way per save.

### Phase 34.5 — what outlived the session

- **The clans are the first faction the game treats as a people rather than a spawn
  table**: a hold you can walk into, warriors who ignore you until you give them a
  reason, and an arc that moves your standing in both directions.
- **Three durable rules moved into the permanent docs**, which are the ones to trust:
  docs/RECIPES.md now records that an encounter without `RegionIds` rolls in every
  region, and that a quest can pay in faction standing.
- **The validator gained three checks in three sub-phases**, each closing a failure
  mode with no symptom: an encounter narrowed to a region that does not exist, a
  quest paying an unknown faction, and a flag nothing ever sets. All three were
  proven by making them fail.
- **Two neutral-actor bugs were fixed at the root**, not at the call site: companions
  no longer open fire on factions the player is at peace with, and ambient encounters
  no longer leak across realms.

---
