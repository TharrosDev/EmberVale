"""Generates the loot side of the item catalogue: affixes, tier pools, boss tables, the cache table.

Pure Python, no Godot. Run from the repository root:

    python tools/items/gen_loot.py            write everything
    python tools/items/gen_loot.py --check    exit 1 if a file on disk differs from what would be written

What it owns
------------
data/affixes/<New>.tres        every affix in NEW_AFFIXES (the eleven pre-ics affixes are hand files and
                               are only checked, never rewritten: their ids and labels are in saves)
data/loot/tiers/Tier<N><Pool>  Gear / Materials / Supplies / Scrolls for each of the six realm tiers
data/loot/bosses/<Boss>BossLoot.tres   one chest table per Flamebearer, signature legendary first
data/loot/ChestLoot.tres       what an ordinary cache rolls
data/loot/<Family>Loot.tres    PATCHED, not rewritten: the legacy fixed gear rows are removed and four
                               nested rows (Entry_tier_*) are added, so the authored flavour drops and
                               their comments stay exactly as written
data/enemies/<Boss>.tres       only the LootTablePath line of the seven Flamebearer archetypes

Every item id written is checked against tools/items/catalogue.py (the planned items plus the 77
that already exist); an unknown id is a hard error, not a table that silently drops nothing.

How the pieces fit: a family table has Tier 0, so LootComponent rolls it at the tier of the realm the
enemy died in, and its nested rows carry "{tier}" in their path (LootTiers.ResolvePath), which is how
one BeastLoot.tres drops Frostfang gear in Frostfang and Sunspire gear in Sunspire.
"""

import hashlib
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(HERE))
sys.path.insert(0, HERE)

import catalogue  # noqa: E402

RARITY = {"Common": 0, "Uncommon": 1, "Rare": 2, "Epic": 3, "Legendary": 4}
STAT = {
    "Health": 0, "Stamina": 1, "Mana": 2, "Strength": 3, "Dexterity": 4, "Intelligence": 5, "Vitality": 6,
    "Endurance": 7, "Armor": 8, "PhysicalPower": 9, "SpellPower": 10, "MoveSpeed": 11, "AttackSpeed": 12,
    "CritChance": 13, "CritDamage": 14, "FireResist": 15, "FrostResist": 16, "LightningResist": 17,
    "ArcaneResist": 18, "NatureResist": 19, "NecroticResist": 20,
}
EFFECT = {"Stat": 0, "HealthRegen": 1, "StaminaRegen": 2, "ManaRegen": 3}
FLAT, PERCENT_ADD = 0, 1
PREFIX, SUFFIX = 0, 1

KNOWN_ITEM_IDS = set(catalogue.ITEMS_BY_ID) | set(catalogue.EXISTING_ITEM_IDS)
GOLD = "item.currency.gold"

_UID_ALPHABET = "abcdefghijklmnopqrstuvwxy012345678"


def uid_for(key):
    """A stable Godot uid for a generated file: derived from its path, so regenerating never churns it."""
    digest = int(hashlib.sha1(key.encode("utf-8")).hexdigest(), 16)
    chars = []
    for _ in range(12):
        digest, rem = divmod(digest, len(_UID_ALPHABET))
        chars.append(_UID_ALPHABET[rem])
    return "uid://c" + "".join(chars)


def fnum(value):
    """A float the way Godot writes one in a .tres (always with a decimal point)."""
    text = repr(float(value))
    return text if "." in text or "e" in text else text + ".0"


def require_item(item_id, where):
    if item_id not in KNOWN_ITEM_IDS:
        raise SystemExit("gen_loot: %s names '%s', which is neither a catalogue item nor an existing one"
                         % (where, item_id))


# ---------------------------------------------------------------------------------------------
# Affixes
# ---------------------------------------------------------------------------------------------

def _affix(file, affix_id, label, kind, stat, lo, hi, rarity="Uncommon", weight=1.0, mod=FLAT, level=0,
           percent=False, group="", effect="Stat", weapons=True, armor=True, accessories=True):
    return dict(file=file, id=affix_id, label=label, kind=kind, stat=stat, lo=lo, hi=hi, rarity=rarity,
                weight=weight, mod=mod, level=level, percent=percent, group=group, effect=effect,
                weapons=weapons, armor=armor, accessories=accessories)


#: The authored range is the range at item level 1; AffixDefinition.ScaleForLevel grows it from there.
NEW_AFFIXES = [
    # --- The six resistance schools (frost included: nothing rolled it before) ---
    _affix("SuffixOfTheHearth", "affix.suffix.hearth", "of the Hearth", SUFFIX, "FireResist", 4, 12, weight=0.5),
    _affix("SuffixOfTheThaw", "affix.suffix.thaw", "of the Thaw", SUFFIX, "FrostResist", 4, 12, weight=0.5),
    _affix("SuffixOfGrounding", "affix.suffix.grounding", "of Grounding", SUFFIX, "LightningResist", 4, 12, weight=0.5),
    _affix("SuffixOfTheVeil", "affix.suffix.veil", "of the Veil", SUFFIX, "ArcaneResist", 4, 12, weight=0.5),
    _affix("SuffixOfTheGrove", "affix.suffix.grove", "of the Grove", SUFFIX, "NatureResist", 4, 12, weight=0.5),
    _affix("SuffixOfTheVigil", "affix.suffix.vigil", "of the Vigil", SUFFIX, "NecroticResist", 4, 12, weight=0.5),

    # --- Mana and the three regeneration rates ---
    _affix("PrefixSages", "affix.prefix.sages", "Sage's", PREFIX, "Mana", 10, 30, weight=0.8),
    _affix("SuffixOfTheWellspring", "affix.suffix.wellspring", "of the Wellspring", SUFFIX, "Mana", 0.4, 1.2,
           rarity="Rare", weight=0.6, effect="ManaRegen", weapons=False),
    _affix("SuffixOfSecondWind", "affix.suffix.second_wind", "of Second Wind", SUFFIX, "Stamina", 1.0, 3.0,
           weight=0.7, effect="StaminaRegen", weapons=False),
    _affix("SuffixOfMending", "affix.suffix.mending", "of Mending", SUFFIX, "Health", 0.3, 1.0,
           rarity="Rare", weight=0.5, effect="HealthRegen", weapons=False),

    # --- Attack speed and the percent variants (gated: a fraction means little on starter gear) ---
    _affix("PrefixQuick", "affix.prefix.quick", "Quick", PREFIX, "AttackSpeed", 0.03, 0.08, rarity="Rare",
           weight=0.6, percent=True, group="haste", armor=False),
    _affix("PrefixBrutal", "affix.prefix.brutal", "Brutal", PREFIX, "PhysicalPower", 0.04, 0.10, rarity="Rare",
           weight=0.6, mod=PERCENT_ADD, level=10, percent=True, group="power", armor=False, accessories=False),
    _affix("PrefixRuned", "affix.prefix.runed", "Runed", PREFIX, "SpellPower", 0.04, 0.10, rarity="Rare",
           weight=0.6, mod=PERCENT_ADD, level=10, percent=True, group="power", armor=False),
    _affix("PrefixBulwark", "affix.prefix.bulwark", "Bulwark", PREFIX, "Armor", 0.04, 0.10, rarity="Rare",
           weight=0.6, mod=PERCENT_ADD, level=10, percent=True, weapons=False, accessories=False),
    _affix("PrefixStalwart", "affix.prefix.stalwart", "Stalwart", PREFIX, "Health", 0.03, 0.08, rarity="Rare",
           weight=0.6, mod=PERCENT_ADD, level=14, percent=True, weapons=False),
    _affix("PrefixTireless", "affix.prefix.tireless", "Tireless", PREFIX, "Stamina", 0.04, 0.10,
           weight=0.6, mod=PERCENT_ADD, level=8, percent=True, weapons=False),
    _affix("PrefixLucid", "affix.prefix.lucid", "Lucid", PREFIX, "Mana", 0.04, 0.10,
           weight=0.6, mod=PERCENT_ADD, level=8, percent=True),
    _affix("SuffixOfTheWind", "affix.suffix.wind", "of the Wind", SUFFIX, "MoveSpeed", 0.02, 0.05, rarity="Rare",
           weight=0.5, mod=PERCENT_ADD, level=14, percent=True, group="haste", weapons=False),

    # --- Primary attributes: one per item ---
    _affix("SuffixOfMight", "affix.suffix.might", "of Might", SUFFIX, "Strength", 1, 3, weight=0.5, group="attribute"),
    _affix("SuffixOfFinesse", "affix.suffix.finesse", "of Finesse", SUFFIX, "Dexterity", 1, 3, weight=0.5, group="attribute"),
    _affix("SuffixOfTheScholar", "affix.suffix.scholar", "of the Scholar", SUFFIX, "Intelligence", 1, 3, weight=0.5,
           group="attribute"),
    _affix("SuffixOfVitality", "affix.suffix.vitality", "of Vitality", SUFFIX, "Vitality", 1, 3, weight=0.5,
           group="attribute"),
    _affix("SuffixOfEndurance", "affix.suffix.endurance", "of Endurance", SUFFIX, "Endurance", 1, 3, weight=0.5,
           group="attribute"),

    # --- Signature affixes: Epic and Legendary only. LootGenerator draws an Epic+ item's first affix
    # from these, which is what stops two Legendaries being the same four common stats. ---
    _affix("PrefixKingsbane", "affix.prefix.kingsbane", "Kingsbane", PREFIX, "PhysicalPower", 0.10, 0.16, rarity="Epic",
           mod=PERCENT_ADD, percent=True, group="power", armor=False, accessories=False),
    _affix("PrefixArchmages", "affix.prefix.archmages", "Archmage's", PREFIX, "SpellPower", 0.10, 0.16, rarity="Epic",
           mod=PERCENT_ADD, percent=True, group="power", armor=False),
    _affix("PrefixUnbroken", "affix.prefix.unbroken", "Unbroken", PREFIX, "Armor", 0.10, 0.18, rarity="Epic",
           mod=PERCENT_ADD, percent=True, weapons=False, accessories=False),
    _affix("PrefixTitans", "affix.prefix.titans", "Titan's", PREFIX, "Health", 0.08, 0.14, rarity="Epic",
           mod=PERCENT_ADD, percent=True, weapons=False),
    _affix("SuffixOfSlaughter", "affix.suffix.slaughter", "of Slaughter", SUFFIX, "CritDamage", 0.30, 0.60,
           rarity="Epic", percent=True, armor=False),
    _affix("SuffixOfTheStorm", "affix.suffix.storm", "of the Storm", SUFFIX, "AttackSpeed", 0.08, 0.14,
           rarity="Epic", percent=True, group="haste", armor=False, accessories=False),
    _affix("SuffixOfPrecision", "affix.suffix.precision", "of Precision", SUFFIX, "CritChance", 0.06, 0.10,
           rarity="Epic", percent=True, armor=False),
    _affix("SuffixOfTheDeepWell", "affix.suffix.deep_well", "of the Deep Well", SUFFIX, "Mana", 1.2, 2.5,
           rarity="Epic", effect="ManaRegen", armor=False),
    _affix("SuffixOfThePhoenix", "affix.suffix.phoenix", "of the Phoenix", SUFFIX, "Health", 1.0, 2.5,
           rarity="Legendary", effect="HealthRegen", weapons=False),
]

#: The pre-ics affixes, by id. Hand-authored files; listed so --check can prove none went missing.
LEGACY_AFFIX_IDS = (
    "affix.prefix.arcane", "affix.prefix.keen", "affix.prefix.sturdy", "affix.prefix.vicious",
    "affix.prefix.vigorous", "affix.suffix.swiftness", "affix.suffix.bear", "affix.suffix.eagle",
    "affix.suffix.ox", "affix.suffix.tiger", "affix.suffix.warding",
)


def render_affix(a):
    lines = [
        '[gd_resource type="Resource" script_class="AffixDefinition" load_steps=2 format=3 uid="%s"]'
        % uid_for("affix/" + a["id"]),
        "",
        '[ext_resource type="Script" path="res://src/Items/AffixDefinition.cs" id="1_affix"]',
        "",
        "[resource]",
        'script = ExtResource("1_affix")',
        'Id = "%s"' % a["id"],
        'Label = "%s"' % a["label"],
        "Kind = %d" % a["kind"],
        "Stat = %d" % STAT[a["stat"]],
        "ModifierType = %d" % a["mod"],
        "MinValue = %s" % fnum(a["lo"]),
        "MaxValue = %s" % fnum(a["hi"]),
        "MinRarity = %d" % RARITY[a["rarity"]],
        "Weight = %s" % fnum(a["weight"]),
    ]
    if a["level"]:
        lines.append("MinItemLevel = %d" % a["level"])
    if a["percent"]:
        lines.append("IsPercent = true")
    if a["group"]:
        lines.append('Group = "%s"' % a["group"])
    if a["effect"] != "Stat":
        lines.append("Effect = %d" % EFFECT[a["effect"]])
    lines += [
        "ForWeapons = %s" % str(a["weapons"]).lower(),
        "ForArmor = %s" % str(a["armor"]).lower(),
        "ForAccessories = %s" % str(a["accessories"]).lower(),
    ]
    return "\n".join(lines) + "\n"


# ---------------------------------------------------------------------------------------------
# Loot tables
# ---------------------------------------------------------------------------------------------

def _slug(item_id):
    return re.sub(r"[^a-z0-9]+", "_", item_id.split(".", 1)[1].lower()).strip("_")


def entry(sub_id, item="", chance=1.0, lo=1, hi=1, roll=False, table="", group="", weight=1.0,
          min_level=0, max_level=0, min_rarity="Common", once=False):
    return dict(sub_id=sub_id, item=item, chance=chance, lo=lo, hi=hi, roll=roll, table=table, group=group,
                weight=weight, min_level=min_level, max_level=max_level, min_rarity=min_rarity, once=once)


def render_entry(e):
    """One LootEntry sub-resource. The five original fields are always written, in the order the
    hand tables use; every ics field is written only off its default."""
    lines = [
        '[sub_resource type="Resource" id="%s"]' % e["sub_id"],
        'script = ExtResource("2_entry")',
        'ItemId = "%s"' % e["item"],
        "DropChance = %s" % fnum(e["chance"]),
        "MinQuantity = %d" % e["lo"],
        "MaxQuantity = %d" % e["hi"],
        "RollAffixes = %s" % str(e["roll"]).lower(),
    ]
    if e["table"]:
        lines.append('TablePath = "%s"' % e["table"])
    if e["group"]:
        lines.append('Group = "%s"' % e["group"])
    if e["weight"] != 1.0:
        lines.append("Weight = %s" % fnum(e["weight"]))
    if e["min_level"]:
        lines.append("MinLevel = %d" % e["min_level"])
    if e["max_level"]:
        lines.append("MaxLevel = %d" % e["max_level"])
    if e["min_rarity"] != "Common":
        lines.append("MinRarity = %d" % RARITY[e["min_rarity"]])
    if e["once"]:
        lines.append("OncePerSave = true")
    return "\n".join(lines)


def render_table(key, header_comment, entries, gold=(0.0, 0, 0), quality=0.0, tier=0, chest=False):
    for e in entries:
        if e["item"]:
            require_item(e["item"], key)
    out = [
        '[gd_resource type="Resource" script_class="LootTable" format=3 uid="%s"]' % uid_for("loot/" + key),
        "",
        '[ext_resource type="Script" path="res://src/Loot/LootTable.cs" id="1_table"]',
        '[ext_resource type="Script" path="res://src/Loot/LootEntry.cs" id="2_entry"]',
        "",
    ]
    out += ["; " + line if line else ";" for line in header_comment]
    out.append("")
    for e in entries:
        out.append(render_entry(e))
        out.append("")
    chance, lo, hi = gold
    out += [
        "[resource]",
        'script = ExtResource("1_table")',
        "Entries = Array[Resource]([%s])" % ", ".join('SubResource("%s")' % e["sub_id"] for e in entries),
        'GoldItemId = "%s"' % GOLD,
        "GoldChance = %s" % fnum(chance),
        "GoldMin = %d" % lo,
        "GoldMax = %d" % hi,
        "QualityBonus = %s" % fnum(quality),
    ]
    if tier:
        out.append("Tier = %d" % tier)
    if chest:
        out.append("DropsAsChest = true")
    return "\n".join(out) + "\n"


GENERATED = "Generated by tools/items/gen_loot.py from tools/items/catalogue.py. Do not edit by hand."

#: Existing tier-1 / tier-2 gear that fills a grid cell the catalogue leaves to it (EXISTING_RETROFIT).
#: The four named retrofit pieces (Dawn Bulwark, Warden Aegis, Drakescale Mail, Huntmaster's Tally)
#: are rewards with their own sources and stay out of the random pools.
LEGACY_POOL_GEAR = {
    1: ("item.weapon.iron_dagger", "item.weapon.hunting_bow", "item.armor.round_shield",
        "item.armor.leather_cap", "item.armor.leather_vest", "item.ring.iron"),
    2: ("item.weapon.steel_sword",),
}

SET_PIECE_WEIGHT = 0.3
DROP_LEGENDARY_WEIGHT = 0.04


def tier_path(pool):
    return "res://data/loot/tiers/Tier{tier}%s.tres" % pool


def gear_pool(tier):
    """Every piece of gear the tier drops, as one pick-one group. Ordinary pieces weigh 1, set
    pieces 0.3 with their set's rarity floor, a world-drop legendary 0.04."""
    rows = []
    for item_id in LEGACY_POOL_GEAR.get(tier, ()):
        rows.append(entry("Entry_" + _slug(item_id), item_id, roll=True, group="gear"))
    for e in catalogue.EQUIPPABLES:
        if e["tier"] != tier or e["source"] != "drop" or e["slot"] == "Ammo":
            continue
        weight, floor = 1.0, "Common"
        if e["set_id"]:
            weight, floor = SET_PIECE_WEIGHT, e["rarity_floor"]
        elif e["rarity_floor"] == "Legendary":
            weight, floor = DROP_LEGENDARY_WEIGHT, "Legendary"
        rows.append(entry("Entry_" + _slug(e["id"]), e["id"], roll=True, group="gear", weight=weight, min_rarity=floor))
    return rows


def material_pool(tier):
    """Raw materials: the tier's own at full weight, the tier below at a third (earlier realms'
    reagents stay useful in later recipes), and the tier's refined metal as a rare find."""
    rows = []
    for m in catalogue.MATERIAL_TIERS:
        if m["source"] != "drop":
            continue
        if m["tier"] == tier:
            rows.append(entry("Entry_" + _slug(m["id"]), m["id"], lo=1, hi=3, group="material"))
        elif m["tier"] == tier - 1:
            rows.append(entry("Entry_" + _slug(m["id"]), m["id"], lo=1, hi=2, group="material", weight=0.35))
    ingot = catalogue.TIER_METAL[tier]
    rows.append(entry("Entry_" + _slug(ingot), ingot, group="material", weight=0.25))
    return rows


_STRENGTH_OF_TIER = {1: "lesser", 2: "lesser", 3: "greater", 4: "greater", 5: "superior", 6: "superior"}
_AMMO_OF_TIER = {
    1: "item.ammo.arrows", 2: "item.ammo.steel_arrows", 3: "item.ammo.blacksteel_arrows",
    4: "item.ammo.sunsteel_arrows", 5: "item.ammo.moonsilver_arrows", 6: "item.ammo.starmetal_arrows",
}


def supply_pool(tier):
    """Consumables and ammunition: the restoratives of the tier's strength, the buff draughts at a
    lower weight, food up to the tier, and the tier's arrows."""
    strength = _STRENGTH_OF_TIER[tier]
    rows = []
    health = "item.potion.health" if strength == "lesser" else "item.potion.health_" + strength
    rows.append(entry("Entry_" + _slug(health), health, group="supply", weight=2.0))
    for leaf in ("stamina", "mana", "cure"):
        item_id = "item.potion.%s_%s" % (leaf, strength)
        rows.append(entry("Entry_" + _slug(item_id), item_id, group="supply"))
    for leaf in ("might", "insight", "stoneskin"):
        item_id = "item.potion.%s_%s" % (leaf, strength)
        rows.append(entry("Entry_" + _slug(item_id), item_id, group="supply", weight=0.4))
    for school in catalogue.RESIST_SCHOOLS:
        item_id = "item.potion.resist_%s_%s" % (school.lower(), strength)
        rows.append(entry("Entry_" + _slug(item_id), item_id, group="supply", weight=0.15))
    for e in catalogue.CONSUMABLES:
        if e["id"].startswith("item.food.") and e["tier"] <= tier:
            rows.append(entry("Entry_" + _slug(e["id"]), e["id"], group="supply", weight=0.5))
    ammo = _AMMO_OF_TIER[tier]
    rows.append(entry("Entry_" + _slug(ammo), ammo, lo=6, hi=14, group="supply", weight=1.2))
    return rows


def scroll_pool(tier):
    """Recipe scrolls for this tier and the next: a realm teaches its own recipes and hints at the
    following realm's. Every scroll-discovered recipe in PLANNED_RECIPES lands in at least one pool."""
    rows = []
    for e in catalogue.RECIPE_SCROLLS:
        if e["tier"] in (tier, tier + 1):
            rows.append(entry("Entry_" + _slug(e["id"]), e["id"], group="scroll",
                              weight=1.0 if e["tier"] == tier else 0.4))
    return rows


def tier_tables():
    files = {}
    for tier in range(1, 7):
        low, high = catalogue.TIER_LEVELS[tier]
        note = "Tier %d (%s, levels %d-%d)." % (tier, catalogue.TIER_REGION[tier], low, high)
        files["data/loot/tiers/Tier%dGear.tres" % tier] = render_table(
            "tiers/Tier%dGear" % tier,
            [GENERATED, note + " One piece of gear, picked by weight; the roll's rarity, level and affixes",
             "come from whoever rolls this table. Set pieces and world-drop legendaries are in the same",
             "group at a low weight, with their rarity floor on the row."],
            gear_pool(tier))
        files["data/loot/tiers/Tier%dMaterials.tres" % tier] = render_table(
            "tiers/Tier%dMaterials" % tier,
            [GENERATED, note + " One stack of raw material, picked by weight."],
            material_pool(tier))
        files["data/loot/tiers/Tier%dSupplies.tres" % tier] = render_table(
            "tiers/Tier%dSupplies" % tier,
            [GENERATED, note + " One consumable or a quiver of the tier's arrows, picked by weight."],
            supply_pool(tier))
        files["data/loot/tiers/Tier%dScrolls.tres" % tier] = render_table(
            "tiers/Tier%dScrolls" % tier,
            [GENERATED, note + " One recipe scroll, picked by weight: this tier's recipes and, less often,",
             "the next tier's."],
            scroll_pool(tier))
    return files


#: boss id -> (enemy archetype file stem, table file stem)
BOSS_FILES = {
    "boss.iron_king": "IronKing",
    "boss.storm_tyrant": "StormTyrant",
    "boss.beast_lord": "BeastLord",
    "boss.crimson_prophet": "CrimsonProphet",
    "boss.hollow_queen": "HollowQueen",
    "boss.ashen_knight": "AshenKnight",
    "boss.morthul": "Morthul",
}


def boss_table_path(stem):
    return "data/loot/bosses/%sBossLoot.tres" % stem


def boss_tables():
    files = {}
    for boss_id in catalogue.FLAMEBEARER_BOSSES:
        stem = BOSS_FILES[boss_id]
        signature = catalogue.BOSS_SIGNATURE[boss_id]
        tier = catalogue.ITEMS_BY_ID[signature]["tier"]
        rows = [
            entry("Entry_signature", signature, roll=True, min_rarity="Legendary", once=True),
            entry("Entry_gear", table=tier_path("Gear"), lo=2, hi=3, min_rarity="Rare"),
            entry("Entry_materials", table=tier_path("Materials"), lo=2, hi=3),
            entry("Entry_ingots", catalogue.TIER_METAL[tier], lo=2, hi=4),
            entry("Entry_supplies", table=tier_path("Supplies"), lo=1, hi=2),
            entry("Entry_scroll", table=tier_path("Scrolls")),
        ]
        files[boss_table_path(stem)] = render_table(
            "bosses/" + stem,
            [GENERATED,
             "%s's reward chest (tier %d). DropsAsChest: LootComponent stands a persistent chest where the" % (boss_id, tier),
             "boss fell and the chest rolls this table when opened. The signature legendary is OncePerSave,",
             "so it is guaranteed on the first kill and never duplicated; the rest is tier loot at Rare or",
             "better. The relic and the story flag are still BossResource's, not this table's."],
            rows, gold=(1.0, 40 + (60 * tier), 80 + (120 * tier)), quality=0.6, tier=tier, chest=True)
    return files


def chest_table():
    rows = [
        entry("Entry_gear", table=tier_path("Gear"), lo=1, hi=2, min_rarity="Uncommon"),
        entry("Entry_materials", table=tier_path("Materials"), lo=1, hi=2),
        entry("Entry_supplies", table=tier_path("Supplies")),
        entry("Entry_scroll", table=tier_path("Scrolls"), chance=0.25),
    ]
    return {"data/loot/ChestLoot.tres": render_table(
        "ChestLoot",
        [GENERATED,
         "What an ordinary cache rolls on its first open (ContainerLootComponent.DefaultTablePath). Tier 0:",
         "it takes the tier of the realm the chest stands in, so the same table is level-appropriate",
         "everywhere. Replaces the old rule of two or three Legendaries drawn from every equippable."],
        rows, gold=(0.9, 15, 45), quality=0.25)}


# ---------------------------------------------------------------------------------------------
# Family tables: patched in place
# ---------------------------------------------------------------------------------------------

#: file stem -> (gear chance, gear rolls, gear floor, materials chance, supplies chance, scroll chance)
FAMILY = {
    "BanditLoot": (0.22, (1, 1), "Common", 0.35, 0.25, 0.04),
    "ClanRaiderLoot": (0.22, (1, 1), "Common", 0.35, 0.25, 0.04),
    "ClanShamanLoot": (0.20, (1, 1), "Common", 0.40, 0.30, 0.10),
    "ClanTamerLoot": (0.20, (1, 1), "Common", 0.35, 0.25, 0.04),
    "SyndicateLoot": (0.25, (1, 1), "Common", 0.35, 0.30, 0.06),
    "FallenLoot": (0.22, (1, 1), "Common", 0.35, 0.22, 0.04),
    "GoblinLoot": (0.18, (1, 1), "Common", 0.35, 0.18, 0.03),
    "NecromancerLoot": (0.25, (1, 1), "Common", 0.45, 0.30, 0.12),
    "UndeadLoot": (0.15, (1, 1), "Common", 0.35, 0.08, 0.02),
    "ConstructLoot": (0.12, (1, 1), "Common", 0.40, 0.05, 0.02),
    "ElementalLoot": (0.10, (1, 1), "Common", 0.40, 0.08, 0.02),
    "BeastLoot": (0.05, (1, 1), "Common", 0.20, 0.04, 0.0),
    "BeastHideLoot": (0.04, (1, 1), "Common", 0.20, 0.04, 0.0),
    "FrostDrakeLoot": (0.45, (1, 1), "Uncommon", 0.60, 0.20, 0.10),
    "DragonLoot": (1.0, (1, 2), "Rare", 1.0, 0.50, 0.50),
    "AshDragonLoot": (1.0, (2, 2), "Rare", 1.0, 0.50, 0.60),
    "AncientDragonLoot": (1.0, (2, 3), "Epic", 1.0, 0.60, 0.80),
}

_BLOCK = re.compile(r'\[sub_resource type="Resource" id="([^"]+)"\]')
_MARKER = "; ics: tier pools, added by tools/items/gen_loot.py."
_ENTRIES = re.compile(r"^Entries = Array\[Resource\]\(\[(.*)\]\)$", re.M)
_TIER_IDS = ("Entry_tier_gear", "Entry_tier_materials", "Entry_tier_supplies", "Entry_tier_scrolls")


def family_rows(stem):
    gear, (lo, hi), floor, materials, supplies, scrolls = FAMILY[stem]
    rows = [
        entry("Entry_tier_gear", table=tier_path("Gear"), chance=gear, lo=lo, hi=hi, min_rarity=floor),
        entry("Entry_tier_materials", table=tier_path("Materials"), chance=materials),
        entry("Entry_tier_supplies", table=tier_path("Supplies"), chance=supplies),
    ]
    if scrolls > 0:
        rows.append(entry("Entry_tier_scrolls", table=tier_path("Scrolls"), chance=scrolls))
    return rows


def patch_family(stem, text):
    """Removes the legacy fixed gear rows and any earlier Entry_tier_* rows, then adds this run's.
    Everything else in the file (flavour drops, comments, gold, quality) is left byte for byte."""
    # A .tres is paragraphs separated by blank lines: the header, the ext_resources, comment
    # blocks, one paragraph per sub_resource, and the final [resource].
    paragraphs = [p for p in re.split(r"\n{2,}", text.replace("\r\n", "\n").strip("\n")) if p.strip()]
    kept, removed, resource = [], [], None
    for paragraph in paragraphs:
        block = _BLOCK.match(paragraph)
        if block is not None and (block.group(1) in _TIER_IDS or "RollAffixes = true" in paragraph):
            removed.append(block.group(1))
        elif paragraph.startswith(_MARKER):
            continue
        elif paragraph.startswith("[resource]"):
            resource = paragraph
        else:
            kept.append(paragraph)

    listing = _ENTRIES.search(resource or "")
    if listing is None:
        raise SystemExit("gen_loot: %s has no Entries line to patch" % stem)

    rows = family_rows(stem)
    refs = [ref for ref in (part.strip() for part in listing.group(1).split(",")) if ref
            and ref[len('SubResource("'):-2] not in removed]
    refs += ['SubResource("%s")' % r["sub_id"] for r in rows]
    resource = _ENTRIES.sub(lambda _: "Entries = Array[Resource]([%s])" % ", ".join(refs), resource, count=1)

    kept.append(_MARKER + " {tier} is the realm tier of the roll.")
    kept += [render_entry(r) for r in rows]
    kept.append(resource)
    return "\n\n".join(kept) + "\n"


def family_tables():
    files = {}
    for stem in FAMILY:
        path = "data/loot/%s.tres" % stem
        with open(os.path.join(ROOT, path), encoding="utf-8", newline="") as handle:
            files[path] = patch_family(stem, handle.read())
    return files


# ---------------------------------------------------------------------------------------------
# Boss archetypes: the LootTablePath line only
# ---------------------------------------------------------------------------------------------

_LOOT_LINE = re.compile(r'^LootTablePath = "[^"]*"$', re.M)


def boss_archetypes():
    files = {}
    for boss_id in catalogue.FLAMEBEARER_BOSSES:
        stem = BOSS_FILES[boss_id]
        path = "data/enemies/%s.tres" % stem
        with open(os.path.join(ROOT, path), encoding="utf-8", newline="") as handle:
            text = handle.read()
        if len(_LOOT_LINE.findall(text)) != 1:
            raise SystemExit("gen_loot: %s must have exactly one LootTablePath line" % path)
        files[path] = _LOOT_LINE.sub('LootTablePath = "res://%s"' % boss_table_path(stem), text)
    return files


# ---------------------------------------------------------------------------------------------
# Self-checks and entry point
# ---------------------------------------------------------------------------------------------

def self_check():
    problems = []
    ids = [a["id"] for a in NEW_AFFIXES]
    for affix_id in set(ids):
        if ids.count(affix_id) > 1 or affix_id in LEGACY_AFFIX_IDS:
            problems.append("affix id '%s' is used twice" % affix_id)
    files = [a["file"] for a in NEW_AFFIXES]
    if len(set(files)) != len(files):
        problems.append("two affixes share a file name")
    for a in NEW_AFFIXES:
        if not a["lo"] > 0 or a["hi"] < a["lo"]:
            problems.append("affix '%s' has a bad range" % a["id"])
        if not (a["weapons"] or a["armor"] or a["accessories"]):
            problems.append("affix '%s' fits no gear family" % a["id"])
    pooled = set()
    for tier in range(1, 7):
        for pool in (gear_pool(tier), material_pool(tier), supply_pool(tier), scroll_pool(tier)):
            if not pool:
                problems.append("tier %d has an empty pool" % tier)
            pooled.update(e["item"] for e in pool)
    for scroll in catalogue.RECIPE_SCROLLS:
        if scroll["id"] not in pooled:
            problems.append("recipe scroll '%s' has no drop source" % scroll["id"])
    for e in catalogue.EQUIPPABLES:
        if e["source"] == "drop" and e["slot"] != "Ammo" and e["id"] not in pooled:
            problems.append("drop-sourced item '%s' is in no tier pool" % e["id"])
    for boss_id in catalogue.FLAMEBEARER_BOSSES:
        if boss_id not in catalogue.BOSS_SIGNATURE or boss_id not in BOSS_FILES:
            problems.append("boss '%s' has no signature or no file mapping" % boss_id)
    return problems


def build():
    files = {}
    for a in NEW_AFFIXES:
        files["data/affixes/%s.tres" % a["file"]] = render_affix(a)
    files.update(tier_tables())
    files.update(boss_tables())
    files.update(chest_table())
    files.update(family_tables())
    files.update(boss_archetypes())
    return files


def main(argv):
    check = "--check" in argv
    problems = self_check()
    if problems:
        print("gen_loot: %d problem(s):" % len(problems))
        for problem in problems:
            print("  " + problem)
        return 1

    files = build()
    stale = []
    for path, text in sorted(files.items()):
        full = os.path.join(ROOT, path)
        current = None
        if os.path.exists(full):
            with open(full, encoding="utf-8", newline="") as handle:
                current = handle.read().replace("\r\n", "\n")
        if current == text:
            continue
        stale.append(path)
        if not check:
            os.makedirs(os.path.dirname(full), exist_ok=True)
            with open(full, "w", encoding="utf-8", newline="\n") as handle:
                handle.write(text)

    if check:
        if stale:
            print("gen_loot: %d file(s) differ from the generator:" % len(stale))
            for path in stale:
                print("  " + path)
            return 1
        print("gen_loot: clean (%d files, %d new affixes)" % (len(files), len(NEW_AFFIXES)))
        return 0

    print("gen_loot: wrote %d of %d files (%d new affixes, %d tier pools, %d boss tables)"
          % (len(stale), len(files), len(NEW_AFFIXES), 24, len(catalogue.FLAMEBEARER_BOSSES)))
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
