#!/usr/bin/env python3
"""The planned item catalogue (ics): the id manifest every lane reads.

PURE DATA. Importing this module reads no file, writes no file and starts nothing; it only builds
the tables below. It exists so the lanes can reference an id before the content lane has generated
the .tres that carries it, and so the ids are decided once.

    import sys; sys.path.insert(0, "tools/items"); import catalogue
    (or, from the repo root:  from tools.items import catalogue)

What is in here
---------------
TIER_LEVELS, TIER_REGION, TIER_ITEM_LEVEL, TIER_REQUIRED_LEVEL   the six realm tiers
EXISTING_ITEM_IDS, EXISTING_RECIPE_IDS    what main already ships; never reused, never renamed
EXISTING_RETROFIT                         tier / class fields to stamp on 12 existing equippables
ITEMS                                     every NEW item, one dict each (see ENTRY_FIELDS)
EQUIPPABLES, CONSUMABLES, NEW_MATERIALS, RECIPE_SCROLLS    ITEMS filtered by "kind"
ITEMS_BY_ID                               id -> entry
SETS, SET_IDS                             the item sets and their planned bonuses
UNIQUE_EFFECTS, UNIQUE_EFFECT_IDS         the unique effects (kind + numbers)
BOSS_SIGNATURE                            boss id -> the legendary it drops
MATERIAL_TIERS                            every crafting material (existing and new) by tier/role
PLANNED_RECIPES, PLANNED_RECIPE_IDS       the new recipes: output, station, discovery, ingredients
validate()                                the self-checks; an empty list means the catalogue is sound

Conventions (docs/IDS.md)
-------------------------
item.<subcategory>.<snake_name>; subcategories used here: weapon, armor (body armour AND shields,
as today), ring, amulet, ammo, potion, food, material, recipe_scroll.
set.<name>, unique.<name>, recipe.<name>. A recipe scroll is item.recipe_scroll.<recipe leaf>.

Enum-valued fields hold the C# member NAME ("MainHand", "Greatsword", "Heavy", "Rare", "Forge",
"RestoreMana"), never an ordinal: a generator maps names to ordinals by reading the C# source, the
way tools/gen_perks.py does.

The fifth realm is named in TIER_REGION only. Its name never appears in an item's display name:
tier-5 gear uses the neutral Gloam / Dusk / Moonsilver vocabulary.

Run it for a census and the self-check:  python tools/items/catalogue.py
"""

# ---------------------------------------------------------------------------------------------
# Tiers
# ---------------------------------------------------------------------------------------------

#: tier -> (lowest, highest) player level the tier is meant for. Tiers overlap on purpose.
TIER_LEVELS = {1: (1, 10), 2: (8, 20), 3: (18, 30), 4: (28, 40), 5: (36, 46), 6: (44, 50)}

#: tier -> the realm whose loot and merchants carry it.
TIER_REGION = {
    1: "region.ember_crown",
    2: "region.frostfang_reach",
    3: "region.ashen_wilds",
    4: "region.sunspire",
    5: "region.pale_concord",
    6: "region.celestial",
}

#: tier -> ItemResource.ItemLevel of that tier's ordinary gear.
TIER_ITEM_LEVEL = {1: 5, 2: 14, 3: 24, 4: 34, 5: 41, 6: 47}

#: tier -> ItemResource.RequiredLevel of that tier's ordinary gear (0 = no requirement).
TIER_REQUIRED_LEVEL = {1: 0, 2: 8, 3: 18, 4: 28, 5: 36, 6: 44}

#: The keys every ITEMS entry carries, in order. Fields that do not apply hold "" / 0 / False / ().
ENTRY_FIELDS = (
    "id",                # item.<subcat>.<name>
    "name",              # English display name
    "kind",              # "equippable" | "consumable" | "material" | "scroll"
    "slot",              # EquipmentSlot member name; "None" for a non-equippable
    "weapon_class",      # WeaponClass member name; "None" for armour, accessories, ammo
    "armor_weight",      # ArmorWeight member name; "None" unless body armour
    "two_handed",        # bool
    "tier",              # 1..6
    "item_level",        # ItemResource.ItemLevel
    "required_level",    # ItemResource.RequiredLevel
    "rarity_floor",      # ItemRarity member name: the lowest rarity it exists at
    "set_id",            # set.* or ""
    "unique_effect_id",  # unique.* or ""
    "source",            # "drop" | "craft" | "shop" | "quest" | "boss:<boss id>" | "guild:<faction id>"
    # consumables only:
    "effect",            # ConsumableEffectKind member name, or ""
    "magnitude",         # float
    "duration",          # seconds; 0 = instant
    "buff_stat",         # StatType member name, or ""
    "buff_kind",         # ModifierType member name, or ""
    "cure_status_ids",   # tuple of status.* ids; () on a Cure means "every harmful status"
    "cooldown",          # seconds
    "cooldown_group",    # "potion" | "elixir" | "food" | ""
    # scrolls only:
    "teaches",           # recipe.* the scroll teaches, or ""
)

# ---------------------------------------------------------------------------------------------
# What main already ships (never reuse, never rename)
# ---------------------------------------------------------------------------------------------

EXISTING_ITEM_IDS = frozenset("""
item.kit.alchemy item.ammo.arrows item.relic.ashen_heart item.decor.banner item.material.barrow_ash
item.material.beast_pelt item.material.boat_pitch item.decor.brazier item.ring.brokers_signet
item.material.charcoal item.material.coal item.material.copper_ore item.decor.crate
item.relic.crimson_heart item.ring.custodians_seal item.food.bread item.armor.dawn_bulwark
item.decor.display_stand item.material.dragon_scale item.armor.drakescale_mail
item.material.dreamsmoke item.material.dyed_wool item.material.elemental_mote
item.material.emberbloom item.food.field_ration item.material.fish_hooks item.material.fish_oil
item.kit.forge item.tome.forged_writ item.food.fresh_catch item.material.frost_pelt
item.material.goblin_hide item.currency.gold item.material.grain_sack item.material.grave_dust
item.material.healing_herb item.potion.health item.tome.herbal item.relic.hollow_heart
item.weapon.hunting_bow item.amulet.huntmasters_tally item.weapon.iron_dagger item.relic.iron_heart
item.material.iron_ingot item.material.iron_ore item.ring.iron item.armor.leather_cap
item.material.leather_strips item.armor.leather_vest item.material.linen_bolt
item.material.net_cordage item.material.oak_plank item.material.pitch item.gem.moonstone_rough
item.armor.round_shield item.gem.ruby item.material.rune_shard item.material.sailcloth
item.material.salt item.food.salted_eel item.gem.sapphire item.material.scrap item.food.smoked_fish
item.material.spice_pouch item.material.steel_ingot item.weapon.steel_sword item.potion.stamina
item.relic.storm_heart item.material.tanned_hide item.material.tarn_roe item.material.untaxed_fur
item.armor.warden_aegis item.amulet.warders_ashglass item.material.warding_chalk item.tome.wardings
item.relic.wild_heart item.kit.workbench
""".split())

EXISTING_RECIPE_IDS = frozenset("""
recipe.kit.alchemy recipe.kit.banner recipe.kit.brazier recipe.kit.crate recipe.kit.display_stand
recipe.drakescale_mail recipe.kit.forge recipe.health_potion recipe.iron_ingot recipe.iron_ring
recipe.leather_cap recipe.leather_strips recipe.leather_vest recipe.steel_sword recipe.kit.workbench
""".split())

#: Existing equippables that occupy a cell of the tier grid (so no new item is planned for that
#: cell), or that simply need the new classification fields. id -> the fields to stamp on the
#: existing .tres. Nothing else about these items changes.
EXISTING_RETROFIT = {
    "item.weapon.iron_dagger": {"tier": 1, "item_level": 5, "weapon_class": "Dagger", "armor_weight": "None", "two_handed": False},
    "item.weapon.hunting_bow": {"tier": 1, "item_level": 5, "weapon_class": "Bow", "armor_weight": "None", "two_handed": True},
    "item.weapon.steel_sword": {"tier": 2, "item_level": 14, "weapon_class": "Sword", "armor_weight": "None", "two_handed": False},
    "item.armor.round_shield": {"tier": 1, "item_level": 5, "weapon_class": "Shield", "armor_weight": "None", "two_handed": False},
    "item.armor.dawn_bulwark": {"tier": 3, "item_level": 26, "weapon_class": "Shield", "armor_weight": "None", "two_handed": False},
    "item.armor.leather_cap": {"tier": 1, "item_level": 3, "weapon_class": "None", "armor_weight": "Medium", "two_handed": False},
    "item.armor.leather_vest": {"tier": 1, "item_level": 3, "weapon_class": "None", "armor_weight": "Medium", "two_handed": False},
    "item.armor.warden_aegis": {"tier": 2, "item_level": 16, "weapon_class": "None", "armor_weight": "Heavy", "two_handed": False},
    "item.armor.drakescale_mail": {"tier": 3, "item_level": 28, "weapon_class": "None", "armor_weight": "Heavy", "two_handed": False},
    "item.ring.iron": {"tier": 1, "item_level": 5, "weapon_class": "None", "armor_weight": "None", "two_handed": False},
    "item.ammo.arrows": {"tier": 1, "item_level": 1, "weapon_class": "None", "armor_weight": "None", "two_handed": False},
    "item.amulet.huntmasters_tally": {"tier": 2, "item_level": 18, "weapon_class": "None", "armor_weight": "None", "two_handed": False},
}

# ---------------------------------------------------------------------------------------------
# Vocabulary: what each tier's gear is made of. Tier 5 is deliberately realm-neutral.
# ---------------------------------------------------------------------------------------------

METAL = {1: "Iron", 2: "Steel", 3: "Blacksteel", 4: "Sunsteel", 5: "Moonsilver", 6: "Starmetal"}
WOOD = {1: "Oak", 2: "Frostpine", 3: "Cinderwood", 4: "Goldenpalm", 5: "Gloamwood", 6: "Starwood"}
CLOTH = {1: "Homespun", 2: "Frostwool", 3: "Ashweave", 4: "Sunsilk", 5: "Gloamweave", 6: "Starweave"}
HIDE = {1: "Rawhide", 2: "Frosthide", 3: "Ashhide", 4: "Dunehide", 5: "Duskhide", 6: "Skyhide"}

#: armour weight -> (tier vocabulary, ((slot, piece noun), ...))
ARMOR_LINES = {
    "Light": (CLOTH, (("Head", "Hood"), ("Chest", "Robe"), ("Hands", "Wraps"), ("Legs", "Trousers"), ("Feet", "Shoes"))),
    "Medium": (HIDE, (("Head", "Coif"), ("Chest", "Jerkin"), ("Hands", "Gloves"), ("Legs", "Breeches"), ("Feet", "Boots"))),
    "Heavy": (METAL, (("Head", "Helm"), ("Chest", "Cuirass"), ("Hands", "Gauntlets"), ("Legs", "Greaves"), ("Feet", "Sabatons"))),
}

#: weapon class -> (tier vocabulary, noun, two-handed)
WEAPON_CLASSES = {
    "Sword": (METAL, "Sword", False),
    "Dagger": (METAL, "Dagger", False),
    "Axe": (METAL, "Axe", False),
    "Mace": (METAL, "Mace", False),
    "Spear": (METAL, "Spear", False),
    "Staff": (WOOD, "Staff", True),
    "Greatsword": (METAL, "Greatsword", True),
    "Bow": (WOOD, "Bow", True),
}

#: Grid cells an existing item already fills: (tier, weapon class).
_EXISTING_WEAPON_CELLS = {(1, "Dagger"), (1, "Bow"), (2, "Sword")}

RING_NAMES = {1: "Copper Ring", 2: "Silver Ring", 3: "Obsidian Ring", 4: "Gold Ring", 5: "Moonsilver Ring", 6: "Starmetal Ring"}
AMULET_NAMES = {
    1: "Copper Charm", 2: "Silver Pendant", 3: "Obsidian Talisman",
    4: "Gold Medallion", 5: "Moonsilver Locket", 6: "Starmetal Torc",
}


def _slug(name):
    """'Frostfang Hunter's Bow' -> 'frostfang_hunters_bow'."""
    out = []
    for ch in name.lower():
        if ch.isalnum():
            out.append(ch)
        elif ch in " -" and out and out[-1] != "_":
            out.append("_")
    return "".join(out).strip("_")


def _grid_rarity(tier):
    return "Common" if tier <= 3 else ("Uncommon" if tier <= 5 else "Rare")


def _entry(item_id, name, kind, tier, source, **over):
    entry = {
        "id": item_id, "name": name, "kind": kind,
        "slot": "None", "weapon_class": "None", "armor_weight": "None", "two_handed": False,
        "tier": tier, "item_level": TIER_ITEM_LEVEL[tier], "required_level": TIER_REQUIRED_LEVEL[tier],
        "rarity_floor": "Common", "set_id": "", "unique_effect_id": "", "source": source,
        "effect": "", "magnitude": 0.0, "duration": 0.0, "buff_stat": "", "buff_kind": "",
        "cure_status_ids": (), "cooldown": 0.0, "cooldown_group": "", "teaches": "",
    }
    unknown = set(over) - set(entry)
    assert not unknown, unknown
    entry.update(over)
    return entry


ITEMS = []

# ---------------------------------------------------------------------------------------------
# The tier grid: 8 weapon classes, 15 armour pieces, a shield, a ring, an amulet and arrows per
# tier, less the cells an existing item fills (EXISTING_RETROFIT).
# ---------------------------------------------------------------------------------------------

for _tier in range(1, 7):
    for _cls, (_vocab, _noun, _two) in WEAPON_CLASSES.items():
        if (_tier, _cls) in _EXISTING_WEAPON_CELLS:
            continue
        _name = "%s %s" % (_vocab[_tier], _noun)
        ITEMS.append(_entry("item.weapon." + _slug(_name), _name, "equippable", _tier, "drop",
                            slot="MainHand", weapon_class=_cls, two_handed=_two, rarity_floor=_grid_rarity(_tier)))

    for _weight, (_vocab, _pieces) in ARMOR_LINES.items():
        for _slot, _noun in _pieces:
            _name = "%s %s" % (_vocab[_tier], _noun)
            ITEMS.append(_entry("item.armor." + _slug(_name), _name, "equippable", _tier, "drop",
                                slot=_slot, armor_weight=_weight, rarity_floor=_grid_rarity(_tier)))

    if _tier > 1:  # tier 1 is the existing item.armor.round_shield
        _name = "%s Shield" % METAL[_tier]
        ITEMS.append(_entry("item.armor." + _slug(_name), _name, "equippable", _tier, "drop",
                            slot="OffHand", weapon_class="Shield", rarity_floor=_grid_rarity(_tier)))

    # Tier 1 also keeps the existing item.ring.iron; the copper ring is the second tier-1 ring.
    _name = RING_NAMES[_tier]
    ITEMS.append(_entry("item.ring." + _slug(_name.replace(" Ring", "")), _name, "equippable", _tier, "drop",
                        slot="Ring", rarity_floor=_grid_rarity(_tier)))

    _name = AMULET_NAMES[_tier]
    ITEMS.append(_entry("item.amulet." + _slug(_name), _name, "equippable", _tier, "drop",
                        slot="Amulet", rarity_floor=_grid_rarity(_tier)))

    if _tier > 1:  # tier 1 is the existing item.ammo.arrows
        _name = "%s Arrows" % METAL[_tier]
        ITEMS.append(_entry("item.ammo." + _slug(_name), _name, "equippable", _tier, "shop",
                            slot="Ammo", rarity_floor="Common"))

# ---------------------------------------------------------------------------------------------
# Item sets. Pieces are dedicated items (never grid items), so a set is found, not assembled from
# ordinary drops. bonuses: (pieces required, StatType name or "", value, ModifierType name,
# unique effect id or "").
# ---------------------------------------------------------------------------------------------

#: piece tuple: (slot, weapon class, armour weight, noun, two-handed)
SETS = [
    {
        "id": "set.emberguard", "name": "Emberguard", "tier": 1, "rarity_floor": "Rare", "source": "drop",
        "pieces_spec": (("Head", "None", "Heavy", "Helm", False), ("Chest", "None", "Heavy", "Cuirass", False),
                        ("OffHand", "Shield", "None", "Shield", False)),
        "bonuses": ((2, "Armor", 10.0, "Flat", ""), (3, "FireResist", 15.0, "Flat", "")),
    },
    {
        "id": "set.frostfang_hunter", "name": "Frostfang Hunter's", "tier": 2, "rarity_floor": "Rare", "source": "drop",
        "pieces_spec": (("Head", "None", "Medium", "Coif", False), ("Chest", "None", "Medium", "Jerkin", False),
                        ("Hands", "None", "Medium", "Gloves", False), ("MainHand", "Bow", "None", "Bow", True)),
        "bonuses": ((2, "CritChance", 0.03, "Flat", ""), (4, "FrostResist", 20.0, "Flat", "")),
    },
    {
        "id": "set.stormcaller", "name": "Stormcaller's", "tier": 2, "rarity_floor": "Rare", "source": "drop",
        "pieces_spec": (("Head", "None", "Light", "Hood", False), ("Chest", "None", "Light", "Robe", False),
                        ("MainHand", "Staff", "None", "Staff", True)),
        "bonuses": ((2, "Mana", 25.0, "Flat", ""), (3, "LightningResist", 20.0, "Flat", "")),
    },
    {
        "id": "set.ashen_reaver", "name": "Ashen Reaver's", "tier": 3, "rarity_floor": "Epic", "source": "drop",
        "pieces_spec": (("Head", "None", "Heavy", "Helm", False), ("Chest", "None", "Heavy", "Cuirass", False),
                        ("Legs", "None", "Heavy", "Greaves", False), ("MainHand", "Axe", "None", "Axe", False)),
        "bonuses": ((2, "PhysicalPower", 6.0, "Flat", ""), (4, "", 0.0, "Flat", "unique.reavers_fury")),
    },
    {
        "id": "set.wildheart", "name": "Wildheart", "tier": 3, "rarity_floor": "Epic", "source": "quest",
        "pieces_spec": (("Amulet", "None", "None", "Totem", False), ("Ring", "None", "None", "Band", False),
                        ("Hands", "None", "Medium", "Gloves", False)),
        "bonuses": ((2, "Health", 40.0, "Flat", ""), (3, "NatureResist", 25.0, "Flat", "unique.wildheart_feast")),
    },
    {
        "id": "set.sunspire_templar", "name": "Sunspire Templar's", "tier": 4, "rarity_floor": "Epic", "source": "drop",
        "pieces_spec": (("Head", "None", "Heavy", "Helm", False), ("Chest", "None", "Heavy", "Cuirass", False),
                        ("Hands", "None", "Heavy", "Gauntlets", False), ("Legs", "None", "Heavy", "Greaves", False),
                        ("Feet", "None", "Heavy", "Sabatons", False)),
        "bonuses": ((2, "Armor", 20.0, "Flat", ""), (4, "FireResist", 30.0, "Flat", ""),
                    (5, "", 0.0, "Flat", "unique.templars_rebuke")),
    },
    {
        "id": "set.gloamwarden", "name": "Gloamwarden's", "tier": 5, "rarity_floor": "Epic", "source": "drop",
        "pieces_spec": (("Head", "None", "Medium", "Coif", False), ("Chest", "None", "Medium", "Jerkin", False),
                        ("Legs", "None", "Medium", "Breeches", False), ("MainHand", "Dagger", "None", "Dagger", False)),
        "bonuses": ((2, "CritChance", 0.05, "Flat", ""), (4, "", 0.0, "Flat", "unique.gloamwardens_step")),
    },
    {
        "id": "set.starfall", "name": "Starfall", "tier": 6, "rarity_floor": "Epic", "source": "drop",
        "pieces_spec": (("Head", "None", "Light", "Hood", False), ("Chest", "None", "Light", "Robe", False),
                        ("Hands", "None", "Light", "Wraps", False), ("Legs", "None", "Light", "Trousers", False),
                        ("Feet", "None", "Light", "Shoes", False)),
        "bonuses": ((2, "Mana", 60.0, "Flat", ""), (4, "SpellPower", 20.0, "Flat", ""),
                    (5, "", 0.0, "Flat", "unique.starfall_echo")),
    },
]

_SUBCAT_OF_SLOT = {"MainHand": "weapon", "Ring": "ring", "Amulet": "amulet"}

for _set in SETS:
    _set["display_name_key"] = _set["id"] + ".name"
    _piece_ids = []
    for _slot, _cls, _weight, _noun, _two in _set["pieces_spec"]:
        _name = "%s %s" % (_set["name"], _noun)
        _id = "item.%s.%s" % (_SUBCAT_OF_SLOT.get(_slot, "armor"), _slug(_name))
        _piece_ids.append(_id)
        ITEMS.append(_entry(_id, _name, "equippable", _set["tier"], _set["source"],
                            slot=_slot, weapon_class=_cls, armor_weight=_weight, two_handed=_two,
                            item_level=TIER_ITEM_LEVEL[_set["tier"]] + 2,
                            rarity_floor=_set["rarity_floor"], set_id=_set["id"]))
    _set["pieces"] = tuple(_piece_ids)

SET_IDS = tuple(s["id"] for s in SETS)

# ---------------------------------------------------------------------------------------------
# Unique effects. "kind" is a UniqueEffectKind member name; the numbers are the
# UniqueEffectResource fields of the same name. Locale keys are <id>.name and <id>.desc; "desc" is
# the English tooltip with the resource's placeholders ({0} magnitude, {1} chance, {2} threshold,
# {3} duration, {4} cooldown), which the content lane formats (fractions shown as percentages).
# ---------------------------------------------------------------------------------------------


def _fx(effect_id, name, kind, desc, magnitude=0.0, chance=1.0, threshold=0.0, duration=0.0, cooldown=0.0, status_id=""):
    return {
        "id": effect_id, "name": name, "kind": kind, "desc": desc,
        "magnitude": magnitude, "chance": chance, "threshold": threshold,
        "duration": duration, "cooldown": cooldown, "status_id": status_id,
        "name_key": effect_id + ".name", "description_key": effect_id + ".desc",
    }


UNIQUE_EFFECTS = [
    # --- Flamebearer signatures ---
    _fx("unique.crownbreaker", "Crownbreaker", "CritExecute",
        "Critical hits deal {0} more damage to enemies below {2} health.", magnitude=0.50, threshold=0.30),
    _fx("unique.stormcleaver", "Tyrant's Thunder", "OnHitStatus",
        "Hits have a {1} chance to brand the target with storm.", chance=0.25, status_id="status.stormbrand"),
    _fx("unique.packlords_hunger", "Packlord's Hunger", "OnKillHeal",
        "A kill restores {0} of your health.", magnitude=0.08),
    _fx("unique.prophets_pyre", "Prophet's Pyre", "SpellEcho",
        "Spells have a {1} chance to echo at {0} power, once every {4} seconds.", magnitude=0.50, chance=0.15, cooldown=4.0),
    _fx("unique.hollow_diadem", "Hollow Grace", "ManaShield",
        "{0} of the damage you take is paid from mana while any remains.", magnitude=0.25),
    _fx("unique.ashen_oath", "Ashen Oath", "LowHealthPower",
        "Below {2} health you deal {0} more damage.", magnitude=0.30, threshold=0.35),
    _fx("unique.unmaking_touch", "Unmaking Touch", "OnHitStatus",
        "Hits have a {1} chance to set decay on the target.", chance=0.20, status_id="status.decay"),
    # --- Guild rewards ---
    _fx("unique.mirrorguard", "Mirrorguard", "BlockReflect",
        "A block returns {0} of the blocked damage to the attacker.", magnitude=0.40),
    _fx("unique.farsight", "Farsight", "CritExecute",
        "Critical hits deal {0} more damage to enemies below {2} health.", magnitude=0.75, threshold=0.25),
    _fx("unique.sages_reservoir", "Sage's Reservoir", "SpellEcho",
        "Spells have a {1} chance to echo at {0} power, once every {4} seconds.", magnitude=0.75, chance=0.10, cooldown=6.0),
    _fx("unique.magpies_luck", "Magpie's Luck", "GoldFind",
        "You find {0} more gold.", magnitude=0.20),
    _fx("unique.emberpike", "Kindling Point", "OnHitStatus",
        "Hits have a {1} chance to set the target burning.", chance=0.30, status_id="status.burning"),
    # --- Quest and world rewards ---
    _fx("unique.windrunner", "Windrunner", "DodgeRefund",
        "A dodge that avoids a hit refunds {0} stamina.", magnitude=12.0),
    _fx("unique.briarheart", "Briarheart", "ThornsFlat",
        "Melee attackers take {0} damage when they hit you.", magnitude=14.0),
    _fx("unique.gravetender", "Gravetender's Due", "OnKillHeal",
        "A kill restores {0} of your health.", magnitude=0.05),
    _fx("unique.winters_bite", "Winter's Bite", "OnHitStatus",
        "Hits have a {1} chance to chill the target.", chance=0.35, status_id="status.chill"),
    _fx("unique.last_ember", "The Last Ember", "LowHealthPower",
        "Below {2} health you deal {0} more damage.", magnitude=0.40, threshold=0.25),
    # --- Set bonuses ---
    _fx("unique.reavers_fury", "Reaver's Fury", "LowHealthPower",
        "Below {2} health you deal {0} more damage.", magnitude=0.20, threshold=0.50),
    _fx("unique.wildheart_feast", "Wildheart Feast", "OnKillHeal",
        "A kill restores {0} of your health.", magnitude=0.04),
    _fx("unique.templars_rebuke", "Templar's Rebuke", "BlockReflect",
        "A block returns {0} of the blocked damage to the attacker.", magnitude=0.30),
    _fx("unique.gloamwardens_step", "Gloamwarden's Step", "DodgeRefund",
        "A dodge that avoids a hit refunds {0} stamina.", magnitude=18.0),
    _fx("unique.starfall_echo", "Starfall Echo", "SpellEcho",
        "Spells have a {1} chance to echo at {0} power, once every {4} seconds.", magnitude=0.60, chance=0.20, cooldown=5.0),
]

UNIQUE_EFFECT_IDS = tuple(e["id"] for e in UNIQUE_EFFECTS)

# ---------------------------------------------------------------------------------------------
# Named legendaries. One per Flamebearer (BOSS_SIGNATURE), one per guild, and five from quests and
# the open world. item_level is the top of the tier, where the fight that drops it sits.
# ---------------------------------------------------------------------------------------------


def _legend(item_id, name, tier, source, effect, slot, weapon_class="None", armor_weight="None", two_handed=False):
    return _entry(item_id, name, "equippable", tier, source, slot=slot, weapon_class=weapon_class,
                  armor_weight=armor_weight, two_handed=two_handed, item_level=TIER_LEVELS[tier][1],
                  rarity_floor="Legendary", unique_effect_id=effect)


LEGENDARIES = [
    _legend("item.weapon.crownbreaker", "Crownbreaker", 1, "boss:boss.iron_king", "unique.crownbreaker", "MainHand", "Mace"),
    _legend("item.weapon.stormcleaver", "Stormcleaver", 2, "boss:boss.storm_tyrant", "unique.stormcleaver", "MainHand", "Axe"),
    _legend("item.weapon.packlords_talon", "Packlord's Talon", 3, "boss:boss.beast_lord", "unique.packlords_hunger", "MainHand", "Dagger"),
    _legend("item.weapon.prophets_pyre", "Prophet's Pyre", 4, "boss:boss.crimson_prophet", "unique.prophets_pyre",
            "MainHand", "Staff", two_handed=True),
    _legend("item.armor.hollow_diadem", "The Hollow Diadem", 5, "boss:boss.hollow_queen", "unique.hollow_diadem",
            "Head", armor_weight="Light"),
    _legend("item.weapon.ashen_oath", "Ashen Oath", 6, "boss:boss.ashen_knight", "unique.ashen_oath",
            "MainHand", "Greatsword", two_handed=True),
    _legend("item.ring.unmakers_signet", "Unmaker's Signet", 6, "boss:boss.morthul", "unique.unmaking_touch", "Ring"),
    _legend("item.armor.mirrorguard", "Mirrorguard", 3, "guild:faction.dawnwardens", "unique.mirrorguard", "OffHand", "Shield"),
    _legend("item.weapon.farsight", "Farsight", 4, "guild:faction.ash_hunters", "unique.farsight",
            "MainHand", "Bow", two_handed=True),
    _legend("item.amulet.sages_reservoir", "Sage's Reservoir", 4, "guild:faction.veiled_archive", "unique.sages_reservoir", "Amulet"),
    _legend("item.amulet.magpies_charm", "Magpie's Charm", 2, "guild:faction.iron_syndicate", "unique.magpies_luck", "Amulet"),
    _legend("item.weapon.emberpike", "Emberpike", 3, "guild:faction.emberbound", "unique.emberpike", "MainHand", "Spear"),
    _legend("item.armor.windrunner_treads", "Windrunner Treads", 2, "quest", "unique.windrunner", "Feet", armor_weight="Medium"),
    _legend("item.armor.briarheart_cuirass", "Briarheart Cuirass", 3, "quest", "unique.briarheart", "Chest", armor_weight="Heavy"),
    _legend("item.armor.gravetenders_grips", "Gravetender's Grips", 4, "quest", "unique.gravetender", "Hands", armor_weight="Light"),
    _legend("item.weapon.winters_bite", "Winter's Bite", 2, "drop", "unique.winters_bite", "MainHand", "Sword"),
    _legend("item.ring.last_ember", "The Last Ember", 5, "quest", "unique.last_ember", "Ring"),
]
ITEMS.extend(LEGENDARIES)

#: boss id -> the legendary it always drops (one per Flamebearer, Morthul included).
BOSS_SIGNATURE = {
    e["source"].split(":", 1)[1]: e["id"] for e in LEGENDARIES if e["source"].startswith("boss:")
}

# ---------------------------------------------------------------------------------------------
# Consumables. Three strengths: lesser (tiers 1-2), greater (tiers 3-4), superior (tiers 5-6).
# The existing item.potion.health is the "lesser" health potion and item.potion.stamina keeps its
# legacy heal; neither is replaced. Cooldown groups: "potion" for restores and cures, "elixir" for
# timed buffs and resistances, "food" for meals, so one of each can be in effect.
# ---------------------------------------------------------------------------------------------

STRENGTHS = (("lesser", "Lesser", 1), ("greater", "Greater", 3), ("superior", "Superior", 5))


def _potion(leaf, name, tier, source, effect, magnitude, duration=0.0, buff_stat="", buff_kind="",
            cure=(), cooldown=8.0, group="potion", subcat="potion"):
    return _entry("item.%s.%s" % (subcat, leaf), name, "consumable", tier, source,
                  effect=effect, magnitude=magnitude, duration=duration, buff_stat=buff_stat,
                  buff_kind=buff_kind, cure_status_ids=tuple(cure), cooldown=cooldown, cooldown_group=group)


# Instant restores. (The lesser health potion is the existing item.potion.health, 40 health.)
ITEMS.append(_potion("health_greater", "Greater Health Potion", 3, "craft", "Heal", 110.0))
ITEMS.append(_potion("health_superior", "Superior Health Potion", 5, "craft", "Heal", 220.0))

for (_leaf, _label, _tier), _stamina, _mana in zip(STRENGTHS, (40.0, 80.0, 140.0), (35.0, 75.0, 130.0)):
    ITEMS.append(_potion("stamina_" + _leaf, "%s Stamina Draught" % _label, _tier, "craft", "RestoreStamina", _stamina))
    ITEMS.append(_potion("mana_" + _leaf, "%s Mana Potion" % _label, _tier, "craft", "RestoreMana", _mana))

# Cures: lesser clears the two elemental afflictions, greater the common harmful set, superior all.
_CURES = (
    ("lesser", "Lesser Antidote", 1, ("status.burning", "status.chill")),
    ("greater", "Greater Antidote", 3, ("status.burning", "status.chill", "status.decay", "status.swarmed", "status.rooted")),
    ("superior", "Panacea", 5, ()),
)
for _leaf, _name, _tier, _ids in _CURES:
    ITEMS.append(_potion("cure_" + _leaf, _name, _tier, "craft", "Cure", 0.0, cure=_ids))

# Resistance elixirs: one per school and strength, three minutes each.
RESIST_SCHOOLS = ("Fire", "Frost", "Lightning", "Arcane", "Nature", "Necrotic")
for (_leaf, _label, _tier), _amount in zip(STRENGTHS, (15.0, 30.0, 50.0)):
    for _school in RESIST_SCHOOLS:
        ITEMS.append(_potion("resist_%s_%s" % (_school.lower(), _leaf), "%s %s Ward Elixir" % (_label, _school), _tier, "shop",
                             "Buff", _amount, duration=180.0, buff_stat=_school + "Resist", buff_kind="Flat",
                             cooldown=2.0, group="elixir"))

# Buff elixirs: might (physical power), insight (spell power), stoneskin (armour); two minutes.
_BUFFS = (
    ("might", "Elixir of Might", "PhysicalPower", (4.0, 9.0, 16.0)),
    ("insight", "Elixir of Insight", "SpellPower", (5.0, 11.0, 19.0)),
    ("stoneskin", "Stoneskin Elixir", "Armor", (10.0, 22.0, 38.0)),
)
for _leaf, _name, _stat, _amounts in _BUFFS:
    for (_strength, _label, _tier), _amount in zip(STRENGTHS, _amounts):
        ITEMS.append(_potion("%s_%s" % (_leaf, _strength), "%s %s" % (_label, _name), _tier,
                             "craft" if _strength == "lesser" else "shop",
                             "Buff", _amount, duration=120.0, buff_stat=_stat, buff_kind="Flat",
                             cooldown=2.0, group="elixir"))

# Cooked food: a heal spread over its duration (a restore with a duration is spread evenly), or a
# long gentle buff. No survival need is implied: food is a consumable with a "food" trade tag.
_FOODS = (
    ("grilled_catch", "Grilled Catch", 1, "Heal", 45.0, 12.0, "", ""),
    ("hearth_stew", "Hearth Stew", 1, "Heal", 70.0, 20.0, "", ""),
    ("oatcake", "Honeyed Oatcake", 1, "RestoreStamina", 60.0, 15.0, "", ""),
    ("eel_pie", "Eel Pie", 2, "Heal", 120.0, 20.0, "", ""),
    ("spiced_roast", "Spiced Roast", 3, "Buff", 25.0, 600.0, "Health", "Flat"),
    ("frostfang_broth", "Frostfang Broth", 2, "Buff", 12.0, 600.0, "FrostResist", "Flat"),
    ("ember_tea", "Emberbloom Tea", 3, "RestoreMana", 90.0, 20.0, "", ""),
    ("roe_toast", "Tarn Roe Toast", 2, "Buff", 15.0, 600.0, "Stamina", "Flat"),
)
for _leaf, _name, _tier, _effect, _amount, _duration, _stat, _kind in _FOODS:
    ITEMS.append(_potion(_leaf, _name, _tier, "craft", _effect, _amount, duration=_duration, buff_stat=_stat,
                         buff_kind=_kind, cooldown=30.0, group="food", subcat="food"))

# ---------------------------------------------------------------------------------------------
# Materials. Every existing crafting material gets a tier, a role and the station that works it;
# five new ones fill the tiers that had no metal (3-6) and no top-tier reagent.
# role: metal | ore | fuel | leather | cloth | wood | reagent | gem | scrap
# station: the CraftingStationType member that PRODUCES it ("" = raw: gathered, dropped or bought).
# ---------------------------------------------------------------------------------------------


def _mat(item_id, tier, role, station, source, new=False, name=""):
    return {"id": item_id, "tier": tier, "role": role, "station": station, "source": source, "new": new, "name": name}


MATERIAL_TIERS = [
    # tier 1 - Ember Crown
    _mat("item.material.iron_ore", 1, "ore", "", "drop"),
    _mat("item.material.copper_ore", 1, "ore", "", "drop"),
    _mat("item.material.coal", 1, "fuel", "", "drop"),
    _mat("item.material.charcoal", 1, "fuel", "", "shop"),
    _mat("item.material.iron_ingot", 1, "metal", "Forge", "craft"),
    _mat("item.material.goblin_hide", 1, "leather", "", "drop"),
    _mat("item.material.tanned_hide", 1, "leather", "", "shop"),
    _mat("item.material.leather_strips", 1, "leather", "Workbench", "craft"),
    _mat("item.material.linen_bolt", 1, "cloth", "", "shop"),
    _mat("item.material.oak_plank", 1, "wood", "", "shop"),
    _mat("item.material.healing_herb", 1, "reagent", "", "drop"),
    _mat("item.material.scrap", 1, "scrap", "", "drop"),
    # tier 2 - Frostfang Reach
    _mat("item.material.steel_ingot", 2, "metal", "Forge", "craft"),
    _mat("item.material.frost_pelt", 2, "leather", "", "drop"),
    _mat("item.material.dyed_wool", 2, "cloth", "", "shop"),
    _mat("item.material.emberbloom", 2, "reagent", "", "drop"),
    _mat("item.material.elemental_mote", 2, "reagent", "", "drop"),
    # tier 3 - Ashen Wilds
    _mat("item.material.blacksteel_ingot", 3, "metal", "Forge", "craft", new=True, name="Blacksteel Ingot"),
    _mat("item.material.beast_pelt", 3, "leather", "", "drop"),
    _mat("item.material.sailcloth", 3, "cloth", "", "shop"),
    _mat("item.material.grave_dust", 3, "reagent", "", "drop"),
    _mat("item.material.rune_shard", 3, "reagent", "", "drop"),
    _mat("item.gem.ruby", 3, "gem", "", "drop"),
    # tier 4 - Sunspire
    _mat("item.material.sunsteel_ingot", 4, "metal", "Forge", "craft", new=True, name="Sunsteel Ingot"),
    _mat("item.material.dreamsmoke", 4, "reagent", "", "shop"),
    _mat("item.gem.sapphire", 4, "gem", "", "drop"),
    # tier 5 - the fifth realm
    _mat("item.material.moonsilver_ingot", 5, "metal", "Forge", "craft", new=True, name="Moonsilver Ingot"),
    _mat("item.material.barrow_ash", 5, "reagent", "", "drop"),
    _mat("item.gem.moonstone_rough", 5, "gem", "", "drop"),
    _mat("item.material.dragon_scale", 5, "leather", "", "drop"),
    # tier 6 - Celestial
    _mat("item.material.starmetal_ingot", 6, "metal", "Forge", "craft", new=True, name="Starmetal Ingot"),
    _mat("item.material.celestial_dust", 6, "reagent", "", "drop", new=True, name="Celestial Dust"),
]

for _m in MATERIAL_TIERS:
    if _m["new"]:
        ITEMS.append(_entry(_m["id"], _m["name"], "material", _m["tier"], _m["source"], item_level=0, required_level=0))

#: tier -> the material of each role that tier's recipes use.
TIER_METAL = {
    1: "item.material.iron_ingot", 2: "item.material.steel_ingot", 3: "item.material.blacksteel_ingot",
    4: "item.material.sunsteel_ingot", 5: "item.material.moonsilver_ingot", 6: "item.material.starmetal_ingot",
}
TIER_LEATHER = {
    1: "item.material.tanned_hide", 2: "item.material.frost_pelt", 3: "item.material.beast_pelt",
    4: "item.material.beast_pelt", 5: "item.material.dragon_scale", 6: "item.material.dragon_scale",
}
TIER_CLOTH = {
    1: "item.material.linen_bolt", 2: "item.material.dyed_wool", 3: "item.material.sailcloth",
    4: "item.material.sailcloth", 5: "item.material.sailcloth", 6: "item.material.sailcloth",
}
#: The tier's catalyst: what makes tier-N cloth, hide and wood more than tier-1 goods. None below tier 3.
TIER_CATALYST = {
    1: "", 2: "", 3: "item.material.grave_dust", 4: "item.material.dreamsmoke",
    5: "item.material.barrow_ash", 6: "item.material.celestial_dust",
}
WOOD_MATERIAL = "item.material.oak_plank"
BINDING = "item.material.leather_strips"

# ---------------------------------------------------------------------------------------------
# Planned recipes. discovery: "trainer" (taught for gold, like today's recipes), "scroll" (learned
# by using item.recipe_scroll.<leaf>, which drops or is sold), "known" (every character starts
# with it). station is a CraftingStationType member name. Existing recipes are not repeated.
# ---------------------------------------------------------------------------------------------

PLANNED_RECIPES = []


def _recipe(leaf, output, station, tier, discovery, ingredients, quantity=1):
    PLANNED_RECIPES.append({
        "id": "recipe." + leaf, "output": output, "quantity": quantity, "station": station,
        "tier": tier, "discovery": discovery,
        "ingredients": tuple((i, q) for i, q in ingredients if i),
        "scroll_id": "item.recipe_scroll." + leaf if discovery == "scroll" else "",
    })


def _gear_discovery(tier):
    return "trainer" if tier <= 2 else "scroll"


# Ingots.
_recipe("steel_ingot", "item.material.steel_ingot", "Forge", 2, "trainer",
        (("item.material.iron_ingot", 2), ("item.material.coal", 1)))
_recipe("blacksteel_ingot", "item.material.blacksteel_ingot", "Forge", 3, "scroll",
        (("item.material.steel_ingot", 2), ("item.material.charcoal", 2), ("item.material.grave_dust", 1)))
_recipe("sunsteel_ingot", "item.material.sunsteel_ingot", "Forge", 4, "scroll",
        (("item.material.steel_ingot", 2), ("item.material.emberbloom", 2), ("item.material.elemental_mote", 1)))
_recipe("moonsilver_ingot", "item.material.moonsilver_ingot", "Forge", 5, "scroll",
        (("item.material.steel_ingot", 2), ("item.gem.moonstone_rough", 1), ("item.material.rune_shard", 1)))
_recipe("starmetal_ingot", "item.material.starmetal_ingot", "Forge", 6, "scroll",
        (("item.material.moonsilver_ingot", 2), ("item.material.celestial_dust", 2), ("item.gem.sapphire", 1)))

# Gear: per tier a sword, a bow, a staff, and the chest piece of each armour line.
for _tier in range(1, 7):
    _metal, _catalyst = TIER_METAL[_tier], TIER_CATALYST[_tier]
    _disc = _gear_discovery(_tier)
    for _cls, _n in (("Sword", 3),):
        if (_tier, _cls) in _EXISTING_WEAPON_CELLS:
            continue  # recipe.steel_sword already exists
        _leaf = _slug("%s %s" % (METAL[_tier], _cls))
        _recipe(_leaf, "item.weapon." + _leaf, "Forge", _tier, _disc, ((_metal, _n), (BINDING, 1)))

    _bow = "item.weapon.hunting_bow" if _tier == 1 else "item.weapon." + _slug(WOOD[_tier] + " Bow")
    _recipe(_bow.rsplit(".", 1)[1], _bow, "Workbench", _tier, _disc,
            ((WOOD_MATERIAL, 3), (BINDING, 2), (_catalyst, 1)))
    _staff = _slug(WOOD[_tier] + " Staff")
    _recipe(_staff, "item.weapon." + _staff, "Workbench", _tier, _disc,
            ((WOOD_MATERIAL, 4), ("item.material.elemental_mote", 1 if _tier < 4 else 2), (_catalyst, 1)))

    _heavy = _slug(METAL[_tier] + " Cuirass")
    _recipe(_heavy, "item.armor." + _heavy, "Forge", _tier, _disc, ((_metal, 5), (BINDING, 2)))
    _medium = _slug(HIDE[_tier] + " Jerkin")
    _recipe(_medium, "item.armor." + _medium, "Workbench", _tier, _disc,
            ((TIER_LEATHER[_tier], 4), (BINDING, 2), (_catalyst, 1)))
    _light = _slug(CLOTH[_tier] + " Robe")
    _recipe(_light, "item.armor." + _light, "Workbench", _tier, _disc,
            ((TIER_CLOTH[_tier], 4), (BINDING, 1), (_catalyst, 2 if _tier >= 3 else 0)))

# Accessories and ammunition.
_recipe("copper_ring", "item.ring.copper", "Forge", 1, "trainer",
        (("item.material.copper_ore", 3), ("item.material.coal", 1)))
_recipe("copper_charm", "item.amulet.copper_charm", "Forge", 1, "trainer",
        (("item.material.copper_ore", 4), ("item.material.leather_strips", 1)))
_recipe("arrows", "item.ammo.arrows", "Workbench", 1, "known",
        (("item.material.oak_plank", 1), ("item.material.scrap", 2)), quantity=20)
_recipe("steel_arrows", "item.ammo.steel_arrows", "Workbench", 2, "trainer",
        (("item.material.oak_plank", 1), ("item.material.steel_ingot", 1)), quantity=20)
_recipe("blacksteel_arrows", "item.ammo.blacksteel_arrows", "Workbench", 3, "scroll",
        (("item.material.oak_plank", 1), ("item.material.blacksteel_ingot", 1)), quantity=20)

# Alchemy.
_recipe("health_greater", "item.potion.health_greater", "Alchemy", 3, "scroll",
        (("item.material.healing_herb", 4), ("item.material.emberbloom", 1)))
_recipe("health_superior", "item.potion.health_superior", "Alchemy", 5, "scroll",
        (("item.material.healing_herb", 6), ("item.material.emberbloom", 2), ("item.material.barrow_ash", 1)))
_recipe("stamina_lesser", "item.potion.stamina_lesser", "Alchemy", 1, "trainer",
        (("item.material.healing_herb", 1), ("item.material.salt", 1)))
_recipe("stamina_greater", "item.potion.stamina_greater", "Alchemy", 3, "scroll",
        (("item.material.healing_herb", 2), ("item.material.salt", 2), ("item.material.grave_dust", 1)))
_recipe("stamina_superior", "item.potion.stamina_superior", "Alchemy", 5, "scroll",
        (("item.material.healing_herb", 3), ("item.material.spice_pouch", 1), ("item.material.barrow_ash", 1)))
_recipe("mana_lesser", "item.potion.mana_lesser", "Alchemy", 1, "trainer",
        (("item.material.healing_herb", 1), ("item.material.elemental_mote", 1)))
_recipe("mana_greater", "item.potion.mana_greater", "Alchemy", 3, "scroll",
        (("item.material.elemental_mote", 2), ("item.material.rune_shard", 1)))
_recipe("mana_superior", "item.potion.mana_superior", "Alchemy", 5, "scroll",
        (("item.material.elemental_mote", 3), ("item.material.rune_shard", 2), ("item.gem.sapphire", 1)))
_recipe("cure_lesser", "item.potion.cure_lesser", "Alchemy", 1, "trainer",
        (("item.material.healing_herb", 2), ("item.material.charcoal", 1)))
_recipe("cure_greater", "item.potion.cure_greater", "Alchemy", 3, "scroll",
        (("item.material.healing_herb", 3), ("item.material.warding_chalk", 1)))
_recipe("cure_superior", "item.potion.cure_superior", "Alchemy", 5, "scroll",
        (("item.material.healing_herb", 4), ("item.material.warding_chalk", 2), ("item.material.dreamsmoke", 1)))
_recipe("might_lesser", "item.potion.might_lesser", "Alchemy", 1, "trainer",
        (("item.material.healing_herb", 1), ("item.material.iron_ore", 1)))
_recipe("insight_lesser", "item.potion.insight_lesser", "Alchemy", 1, "trainer",
        (("item.material.healing_herb", 1), ("item.material.warding_chalk", 1)))
_recipe("stoneskin_lesser", "item.potion.stoneskin_lesser", "Alchemy", 1, "trainer",
        (("item.material.healing_herb", 1), ("item.material.coal", 1)))

# Cooking: no station (CraftingStationType.Hand), known from the start.
_recipe("grilled_catch", "item.food.grilled_catch", "Hand", 1, "known",
        (("item.food.fresh_catch", 1), ("item.material.charcoal", 1)))
_recipe("hearth_stew", "item.food.hearth_stew", "Hand", 1, "known",
        (("item.material.grain_sack", 1), ("item.material.healing_herb", 1), ("item.material.salt", 1)))
_recipe("oatcake", "item.food.oatcake", "Hand", 1, "known",
        (("item.material.grain_sack", 1),))
_recipe("eel_pie", "item.food.eel_pie", "Hand", 2, "known",
        (("item.food.salted_eel", 1), ("item.material.grain_sack", 1)))
_recipe("spiced_roast", "item.food.spiced_roast", "Hand", 3, "scroll",
        (("item.material.spice_pouch", 1), ("item.food.fresh_catch", 2)))
_recipe("frostfang_broth", "item.food.frostfang_broth", "Hand", 2, "scroll",
        (("item.food.fresh_catch", 1), ("item.material.fish_oil", 1), ("item.material.salt", 1)))
_recipe("ember_tea", "item.food.ember_tea", "Hand", 3, "scroll",
        (("item.material.emberbloom", 2),))
_recipe("roe_toast", "item.food.roe_toast", "Hand", 2, "known",
        (("item.material.tarn_roe", 1), ("item.material.grain_sack", 1)))

PLANNED_RECIPE_IDS = tuple(r["id"] for r in PLANNED_RECIPES)

# Recipe scrolls: one item per scroll-discovered recipe. Using it teaches the recipe.
for _r in PLANNED_RECIPES:
    if _r["discovery"] == "scroll":
        _out = next((e for e in ITEMS if e["id"] == _r["output"]), None)
        _name = "Recipe: " + (_out["name"] if _out else _r["output"].rsplit(".", 1)[1].replace("_", " ").title())
        ITEMS.append(_entry(_r["scroll_id"], _name, "scroll", _r["tier"], "drop",
                            item_level=0, required_level=0, rarity_floor="Uncommon", teaches=_r["id"]))

# ---------------------------------------------------------------------------------------------
# Views
# ---------------------------------------------------------------------------------------------

EQUIPPABLES = [e for e in ITEMS if e["kind"] == "equippable"]
CONSUMABLES = [e for e in ITEMS if e["kind"] == "consumable"]
NEW_MATERIALS = [e for e in ITEMS if e["kind"] == "material"]
RECIPE_SCROLLS = [e for e in ITEMS if e["kind"] == "scroll"]
ITEMS_BY_ID = {e["id"]: e for e in ITEMS}
SETS_BY_ID = {s["id"]: s for s in SETS}
UNIQUE_EFFECTS_BY_ID = {e["id"]: e for e in UNIQUE_EFFECTS}

UNIQUE_EFFECT_KINDS = (
    "OnHitStatus", "OnKillHeal", "LowHealthPower", "BlockReflect", "SpellEcho",
    "DodgeRefund", "GoldFind", "ThornsFlat", "CritExecute", "ManaShield",
)
FLAMEBEARER_BOSSES = (
    "boss.iron_king", "boss.storm_tyrant", "boss.beast_lord", "boss.crimson_prophet",
    "boss.hollow_queen", "boss.ashen_knight", "boss.morthul",
)
STATIONS = ("Hand", "Forge", "Workbench", "Alchemy")
#: Words that must never appear in an item's display name (the fifth realm is unnamed until its reveal).
FORBIDDEN_NAME_WORDS = ("pale", "concord")


def validate():
    """Returns a list of problems; empty when the catalogue is internally consistent."""
    import re

    problems = []
    id_shape = re.compile(r"^[a-z]+(\.[a-z0-9_]+)+$")
    seen = set()
    for e in ITEMS:
        if tuple(e) != ENTRY_FIELDS:
            problems.append("%s: fields differ from ENTRY_FIELDS" % e["id"])
        if not id_shape.match(e["id"]) or not e["id"].startswith("item.") or e["id"].count(".") != 2:
            problems.append("%s: not item.<subcat>.<name>" % e["id"])
        if e["id"] in seen:
            problems.append("%s: duplicate id" % e["id"])
        if e["id"] in EXISTING_ITEM_IDS:
            problems.append("%s: reuses an existing id" % e["id"])
        seen.add(e["id"])
        if any(w in e["name"].lower() for w in FORBIDDEN_NAME_WORDS):
            problems.append("%s: display name '%s' names the fifth realm" % (e["id"], e["name"]))
        if e["tier"] not in TIER_LEVELS:
            problems.append("%s: tier %r" % (e["id"], e["tier"]))
        if e["set_id"] and e["set_id"] not in SETS_BY_ID:
            problems.append("%s: unknown set %s" % (e["id"], e["set_id"]))
        if e["unique_effect_id"] and e["unique_effect_id"] not in UNIQUE_EFFECTS_BY_ID:
            problems.append("%s: unknown unique effect %s" % (e["id"], e["unique_effect_id"]))
        if e["kind"] == "equippable" and e["slot"] == "None":
            problems.append("%s: equippable with no slot" % e["id"])
        if e["kind"] == "scroll" and e["teaches"] not in PLANNED_RECIPE_IDS:
            problems.append("%s: teaches unknown recipe %s" % (e["id"], e["teaches"]))

    for s in SETS:
        if not 3 <= len(s["pieces"]) <= 5:
            problems.append("%s: %d pieces" % (s["id"], len(s["pieces"])))
        for pieces, _stat, _value, _mod, effect in s["bonuses"]:
            if not 2 <= pieces <= len(s["pieces"]):
                problems.append("%s: bonus at %d pieces" % (s["id"], pieces))
            if effect and effect not in UNIQUE_EFFECTS_BY_ID:
                problems.append("%s: unknown bonus effect %s" % (s["id"], effect))

    for fx in UNIQUE_EFFECTS:
        if fx["kind"] not in UNIQUE_EFFECT_KINDS:
            problems.append("%s: unknown kind %s" % (fx["id"], fx["kind"]))
    if len(set(UNIQUE_EFFECT_IDS)) != len(UNIQUE_EFFECT_IDS):
        problems.append("duplicate unique effect id")

    for boss in FLAMEBEARER_BOSSES:
        if boss not in BOSS_SIGNATURE:
            problems.append("%s: no signature drop" % boss)

    known_items = EXISTING_ITEM_IDS | set(ITEMS_BY_ID)
    for m in MATERIAL_TIERS:
        if m["id"] not in known_items:
            problems.append("%s: material is neither existing nor planned" % m["id"])
        if m["new"] == (m["id"] in EXISTING_ITEM_IDS):
            problems.append("%s: 'new' flag disagrees with EXISTING_ITEM_IDS" % m["id"])

    recipe_ids = set()
    for r in PLANNED_RECIPES:
        if r["id"] in recipe_ids or r["id"] in EXISTING_RECIPE_IDS:
            problems.append("%s: duplicate or existing recipe id" % r["id"])
        recipe_ids.add(r["id"])
        if r["output"] not in known_items:
            problems.append("%s: unknown output %s" % (r["id"], r["output"]))
        if r["station"] not in STATIONS:
            problems.append("%s: unknown station %s" % (r["id"], r["station"]))
        if not r["ingredients"]:
            problems.append("%s: no ingredients" % r["id"])
        for item_id, quantity in r["ingredients"]:
            if item_id not in known_items or quantity <= 0:
                problems.append("%s: bad ingredient %s x%s" % (r["id"], item_id, quantity))

    for item_id in EXISTING_RETROFIT:
        if item_id not in EXISTING_ITEM_IDS:
            problems.append("%s: retrofit of an id that does not exist" % item_id)

    return problems


if __name__ == "__main__":
    import collections
    import sys

    print("items: %d new (%d equippable, %d consumable, %d material, %d scroll)" % (
        len(ITEMS), len(EQUIPPABLES), len(CONSUMABLES), len(NEW_MATERIALS), len(RECIPE_SCROLLS)))
    per_tier = collections.Counter(e["tier"] for e in EQUIPPABLES)
    print("equippables per tier: " + ", ".join("T%d %d" % (t, per_tier[t]) for t in sorted(per_tier)))
    print("sets: %d, unique effects: %d, legendaries: %d, recipes: %d" % (
        len(SETS), len(UNIQUE_EFFECTS), len(LEGENDARIES), len(PLANNED_RECIPES)))
    issues = validate()
    for issue in issues:
        print("PROBLEM: " + issue)
    sys.exit(1 if issues else 0)
