## Phase 51 — Itemization, Loot & Reward Economy Pass [C]

> **Live audit (planning):** 63 item templates exist. Nine slots work, but authored gear covers
> MainHand (two swords + dagger), Head (one), Chest (two), Ring (one); OffHand, Hands, Legs, Feet
> and Amulet are empty. ItemType has Misc/Consumable/Weapon/Armor/Material/Quest. The old claim that
> a bow placeholder exists is false; Phase 45B–D owns ranged mechanics and the proof bow.

- [ ] **51A — Catalogue/slot/reward/loot census and targets** [C/P]
  - **Build / Author:** census by type, slot, family, rarity/tier, realm, stats/affixes, acquisition
    source and uniqueness. Set breadth ranges from progression bands/content sources, not item-count vanity.
  - **Verify:** all existing items/nine slots, unreachable/duplicate/placeholder rows.
  - **Done when:** every planned item has tier, realm, purpose and owner.

- [ ] **51B — Melee weapon-family breadth** [C]
  - **Build / Author:** approved light/standard/heavy families using existing timing/stamina/poise/combo;
    starter/mid/end examples with realm identity.
  - **Do not:** invent alternate attacks under catalogue work.
  - **Verify:** timing ranges, equip/animation, affix pools and viable distinctions.
  - **Done when:** every family has a progression spine without near-identical filler.

- [ ] **51C — Physical ranged catalogue** [C]
  - **Dependency:** frozen 45D pipeline.
  - **Build / Author:** bow tiers/realm identities and approved quiver/ammo only; loot/shop/quest placement
    and ranged affixes within frozen mechanics.
  - **Verify:** fire every template, both cameras, assets, rewards/save/economy.
  - **Done when:** ranged supports a full build spine, not one proof bow.

- [ ] **51D — Armor across Head/Chest/Hands/Legs/Feet** [C]
  - **Build / Author:** supported light/medium/heavy identities; fill every body slot across tiers/realms;
    resistance gear answers threats without immunity.
  - **Verify:** all slots equip/save/UI, mixed/full stats, affixes and clipping handoff.
  - **Done when:** every body slot poses meaningful choices without a fake armor-class mechanic.

- [ ] **51E — Rings, amulets and accessory identity** [C]
  - **Build / Author:** fill Amulet and broaden Ring with utility/build-shaping bonuses; realm/guild/
    quest placement and affix-overlap rules.
  - **Verify:** stacking, comparison UI, save and unique duplication.
  - **Done when:** accessories change builds rather than repeat flat stats.

- [ ] **51F — Affix and set-family breadth** [C]
  - **Build / Author:** audit actual stat exports, including current Frost-only template resistance;
    supported bonuses/weights. Sets use a frozen G2 mechanism or are coordinated named items—no hidden feature.
  - **Verify:** pool by tier/family, both range ends, no immunity, statistical roll report.
  - **Done when:** varied builds have complete validator coverage.

- [ ] **51G — Consumables, materials and recipes** [C]
  - **Build / Author:** combat consumables, realm materials and existing-station recipes; food stays
    instant-heal, never hunger; every material has a source and sink.
  - **Verify:** source/sink graph, recipe reachability, stack/value/economy/full inventory.
  - **Done when:** no orphan material or unreachable recipe remains.

- [ ] **51H — Spell tomes and full spellbook breadth** [C]
  - **Build / Author:** viable tiered choices per school, signature charged/channeled/corrupted lines,
    tomes and a few relic spells on frozen Phase 29.5 mechanics.
  - **Verify:** school/delivery/tier matrix, duplicate learning, Weave/corruption, enemy/player use.
  - **Done when:** every school is a reachable build spine.

- [ ] **51I — Divine relics and major unique rewards** [C]
  - **Build / Author:** one identity/power/corruption choice per Flamebearer plus guild/quest rewards;
    combat/story guardians only—no trial/puzzle/trap/vault. Define accept/refuse/duplicate/full-pack recovery.
  - **Verify:** boss choices, repeated defeat/reload, UI/save and ending graph.
  - **Done when:** every unique is obtainable exactly once or recoverable by policy.

- [ ] **51J — Loot tables, economy placement and duplicate protection** [C/P]
  - **Build / Author:** curate enemy/lair/event/shop/quest/boss tables by realm/tier; prevent unique random
    drops, oversupply and dead tables; use economy report without Phase 56 numeric tuning.
  - **Verify:** statistical drop/source report, validators/negative tests, overflow and clean progression sample.
  - **Done when:** every catalogue row is reachable and no table empties or floods progression.

---
