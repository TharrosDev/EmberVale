#!/usr/bin/env python3
"""Author the perk catalogue (progression upgrade, P5).

One table below generates two things that MUST agree, so they cannot drift:

  1. data/perks/*.tres        - one PerkResource per perk (tree position, gates, effects)
  2. data/locale/strings.csv  - perk.<id>.name / perk.<id>.desc, inside the
                                '# --- BEGIN progression:perks ---' block

Edit the table here, never the .tres. Descriptions are written with {0}, {1} placeholders that are
filled from the perk's own effect values (unit-aware), so a retune cannot leave a stale number in the text.

Tree rules (the validator and tests/Embervale.Tests/PerkCatalogueTests.cs re-check every one):
  tier 1 free; tier 2 needs 2 points in the branch; tier 3 needs 5; tier 4 needs 9; the tier 5 capstone
  needs 14 and a prerequisite. Branches total about 40 points against the 54 a character earns
  (49 level points + 5 milestone points from P9), so a build is a choice, never "everything".
  Effect totals stay under the PerkEffectMath caps, so a cap is a backstop, not a design tool.

Enum ordinals are read from the C# sources (a .tres stores an enum as its index), so reordering an enum
cannot silently reshuffle the catalogue; the enums are append-only anyway.

Idempotent. Usage:  python tools/gen_perks.py [--check]
        --check exits 1 if anything would change, without writing (a gate, not a generator).
"""

import io
import os
import re
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
TRES_DIR = os.path.join(ROOT, "data", "perks")
CSV = os.path.join(ROOT, "data", "locale", "strings.csv")
MARK_BEGIN = "# --- BEGIN progression:perks ---"
MARK_END = "# --- END progression:perks ---"


def enum_members(rel_path, name):
    """Member names of `enum <name>` in a C# file, in declaration order (= the stored ordinal)."""
    text = io.open(os.path.join(ROOT, rel_path), encoding="utf-8").read()
    body = re.search(r"enum\s+" + name + r"\s*\{(.*?)\n\}", text, re.S).group(1)
    body = re.sub(r"//[^\n]*", "", body)
    body = re.sub(r"/\*.*?\*/", "", body, flags=re.S)
    return [m.strip().split("=")[0].strip() for m in body.split(",") if m.strip()]


KINDS = enum_members("src/Progression/PerkEffectKind.cs", "PerkEffectKind")
BRANCHES = enum_members("src/Progression/PerkBranch.cs", "PerkBranch")
STATS = enum_members("src/Stats/StatType.cs", "StatType")
MODS = enum_members("src/Stats/StatModifier.cs", "ModifierType")

# The tier gates, in branch points already spent. Mirrors PerkRules.TierGate (checked by --validate).
GATE = {1: 0, 2: 2, 3: 5, 4: 9, 5: 14}

# ---- effect constructors ----------------------------------------------------------------------


def E(kind, value, arg=""):
    """A non-stat effect (PerkEffectKind), `value` per rank."""
    assert kind in KINDS, kind
    return dict(type="effect", kind=kind, value=value, arg=arg)


def S(stat, value, mod="Flat"):
    """An extra stat modifier, `value` per rank."""
    assert stat in STATS and mod in MODS, (stat, mod)
    return dict(type="stat", stat=stat, value=value, mod=mod)


def num(v):
    """A short number: 15 not 15.0, 0.05 not 0.05000001."""
    v = round(v, 6)
    return str(int(v)) if v == int(v) else ("%g" % v)


def show(fx):
    """How an effect's per-rank value reads in a description."""
    v = abs(fx["value"])
    if fx["type"] == "stat":
        if fx["mod"] == "PercentAdd" or fx["stat"] in ("CritChance", "CritDamage", "AttackSpeed"):
            return num(v * 100) + "%"
        return num(v)
    if fx["kind"] == "HaggleChanceBonus":
        return num(v)                       # percentage points
    if fx["kind"] == "LootQuality":
        return num(v)                       # a quality bonus, not a percentage
    return num(v * 100) + "%"


# ---- the table --------------------------------------------------------------------------------

PERKS = []


def perk(pid, name, branch, tier, col, rank, desc, fx=(), pre=(), cost=None, corr=0, legacy=None, uid=""):
    """pid is the id tail (perk.<pid>). `legacy` = (stat, mod, value) for the six pre-tree perks, whose
    stat bonus lives in the resource's own Stat fields so the old .tres shape (and saves) stay valid."""
    if cost is None:
        cost = 1 if tier <= 2 else 2
    cap = tier == 5
    if cap:
        cost = 3
    PERKS.append(dict(pid=pid, name=name, branch=branch, tier=tier, col=col, rank=rank, cost=cost,
                      pre=["perk." + p for p in pre], bp=GATE[tier], cap=cap, corr=corr,
                      fx=list(fx), legacy=legacy, desc=desc, uid=uid))


# -- Warrior: heavy hits, deep pools, a shield that holds ----------------------------------------
W = "Warrior"
perk("might", "Might", W, 1, 0, 5, "Blows land harder. +2 Physical Power per rank.",
     legacy=("PhysicalPower", "Flat", 2.0))
perk("toughness", "Toughness", W, 1, 1, 5, "Hardened against harm. +15 maximum Health per rank.",
     legacy=("Health", "Flat", 15.0))
perk("iron_stance", "Iron Stance", W, 1, 2, 3,
     "Plant your feet behind the shield. Blocking costs {0} less Stamina per rank.",
     fx=[E("BlockStaminaMult", -0.08)])
perk("brawler", "Brawler", W, 1, 3, 3,
     "Swing like you mean to keep swinging. Attacks cost {0} less Stamina per rank.",
     fx=[E("AttackStaminaMult", -0.05)])
perk("warding", "Warding", W, 2, 0, 5, "Thicker hide, sturdier guard. +3 Armor per rank.",
     legacy=("Armor", "Flat", 3.0))
perk("second_wind", "Second Wind", W, 2, 1, 3,
     "Breath comes back fast. Sprinting costs {0} less Stamina per rank.",
     fx=[E("SprintStaminaMult", -0.05)], pre=["toughness"])
perk("riposte", "Riposte", W, 2, 2, 2,
     "A clean parry costs little to give. Parrying costs {0} less Stamina per rank.",
     fx=[E("ParryStaminaMult", -0.15)], pre=["iron_stance"])
perk("crushing_blows", "Crushing Blows", W, 3, 1, 3,
     "Finish what you started. Critical hits deal {0} more damage per rank.",
     fx=[S("CritDamage", 0.12)], pre=["might"])
perk("shield_wall", "Shield Wall", W, 3, 2, 2,
     "Nothing gets past the rim. +{0} Armor and blocking costs {1} less Stamina per rank.",
     fx=[S("Armor", 0.06, "PercentAdd"), E("BlockStaminaMult", -0.06)], pre=["riposte"])
perk("unbroken", "Unbroken", W, 4, 1, 2,
     "You keep standing. +{0} maximum Health per rank.",
     fx=[S("Health", 0.05, "PercentAdd")], pre=["toughness"], cost=1)
perk("warlord", "Warlord", W, 5, 1, 1,
     "The field is yours. +{0} Physical Power and attacks cost {1} less Stamina.",
     fx=[S("PhysicalPower", 0.10, "PercentAdd"), E("AttackStaminaMult", -0.10)], pre=["crushing_blows"])

# -- Archer: the bow, the eye, the quick foot ----------------------------------------------------
A = "Archer"
perk("precision", "Precision", A, 1, 0, 3, "A keener eye for weak points. +2% Crit Chance per rank.",
     legacy=("CritChance", "Flat", 0.02))
perk("steady_aim", "Steady Aim", A, 1, 1, 3,
     "Hold the string without strain. Drawing a bow costs {0} less Stamina per rank.",
     fx=[E("BowDrawStaminaMult", -0.08)])
perk("endurance_training", "Endurance Training", A, 1, 2, 5,
     "Lungs and legs that last. +10 maximum Stamina per rank.",
     legacy=("Stamina", "Flat", 10.0))
perk("fleet_foot", "Fleet Foot", A, 1, 3, 3,
     "Light on the trail. +{0} Move Speed per rank.", fx=[S("MoveSpeed", 0.15)])
perk("evasion", "Evasion", A, 2, 0, 3,
     "Move before the blow lands. Dodging costs {0} less Stamina per rank.",
     fx=[E("DodgeStaminaMult", -0.06)])
perk("hunters_eye", "Hunter's Eye", A, 2, 1, 3,
     "You know what is worth taking. Slain foes drop better gear, raising loot quality by {0} per rank.",
     fx=[E("LootQuality", 0.04)])
perk("wind_reader", "Wind Reader", A, 2, 2, 2,
     "You read the weak point and the breeze. Critical hits deal {0} more damage per rank.",
     fx=[S("CritDamage", 0.08)], pre=["precision"])
perk("far_shot", "Far Shot", A, 3, 0, 3,
     "Draw to the ear and let it fly. Arrows hit {0} harder per rank.",
     fx=[E("RangedPowerBonus", 0.05)], pre=["steady_aim"])
perk("quickdraw", "Quickdraw", A, 3, 1, 2,
     "Nock, draw, loose. Drawing costs {0} less Stamina and attacks are {1} faster per rank.",
     fx=[E("BowDrawStaminaMult", -0.07), S("AttackSpeed", 0.03)], pre=["steady_aim"])
perk("marksman", "Marksman", A, 4, 0, 2,
     "Every shot is placed. Arrows hit {0} harder and +{1} Crit Chance per rank.",
     fx=[E("RangedPowerBonus", 0.06), S("CritChance", 0.02)], pre=["far_shot"])
perk("deadeye", "Deadeye", A, 5, 0, 1,
     "One arrow, one answer. Arrows hit {0} harder and critical hits deal {1} more damage.",
     fx=[E("RangedPowerBonus", 0.10), S("CritDamage", 0.15)], pre=["marksman"])

# -- Mage: depth, economy, the schools ------------------------------------------------------------
M = "Mage"
perk("arcane_focus", "Arcane Focus", M, 1, 0, 5, "Thought sharpened to a point. +{0} Spell Power per rank.",
     fx=[S("SpellPower", 2.0)])
perk("thrift", "Thrift", M, 1, 1, 3, "Spend less to cast. Every spell costs {0} less mana per rank.",
     fx=[E("ManaCostMult", -0.05)])
perk("deep_well", "Deep Well", M, 1, 2, 5, "A larger reservoir to draw on. +{0} maximum Mana per rank.",
     fx=[S("Mana", 10.0)])
perk("elementalist", "Elementalist", M, 2, 0, 3,
     "Mastery of the three loud schools. Fire, Frost and Lightning spells hit {0} harder per rank.",
     fx=[E("SchoolPowerBonus", 0.04, "Fire"), E("SchoolPowerBonus", 0.04, "Frost"),
         E("SchoolPowerBonus", 0.04, "Lightning")], pre=["arcane_focus"])
perk("ward_weaver", "Ward Weaver", M, 2, 1, 3,
     "Wards woven into the hem. +{0} Fire, Frost, Lightning and Arcane resistance per rank.",
     fx=[S("FireResist", 3.0), S("FrostResist", 3.0), S("LightningResist", 3.0), S("ArcaneResist", 3.0)],
     pre=["deep_well"])
perk("wild_attunement", "Wild Attunement", M, 2, 2, 3,
     "The quiet schools answer you. Nature and Necrotic spells hit {0} harder per rank.",
     fx=[E("SchoolPowerBonus", 0.05, "Nature"), E("SchoolPowerBonus", 0.05, "Necrotic")],
     pre=["arcane_focus"])
perk("channeler", "Channeler", M, 3, 0, 3,
     "Steady hands shape the weave. Spells crit {0} more often per rank.",
     fx=[E("SpellCritBonus", 0.03)], pre=["elementalist"])
perk("wellspring", "Wellspring", M, 3, 2, 2,
     "The well refills itself. +{0} maximum Mana per rank.",
     fx=[S("Mana", 0.08, "PercentAdd")], pre=["deep_well"])
perk("arcane_economy", "Arcane Economy", M, 4, 1, 2,
     "Nothing wasted in the weave. Every spell costs {0} less mana per rank.",
     fx=[E("ManaCostMult", -0.08)], pre=["thrift"])
perk("archmage", "Archmage", M, 5, 1, 1,
     "Every school bends to you. All spells hit {0} harder and crit {1} more often.",
     fx=[E("SchoolPowerBonus", 0.10), E("SpellCritBonus", 0.04)], pre=["channeler"])

# -- Rogue: crit, dodge, loot, gold (there is no stealth system) --------------------------------
R = "Rogue"
perk("cutpurse", "Cutpurse", R, 1, 0, 2,
     "You know a fence who asks no questions. Merchants pay {0} more per rank.",
     fx=[E("SellBonus", 0.01)])
perk("light_feet", "Light Feet", R, 1, 1, 3,
     "A runner's economy of effort. Sprinting costs {0} less Stamina per rank.",
     fx=[E("SprintStaminaMult", -0.08)])
perk("keen_edge", "Keen Edge", R, 1, 2, 3, "A blade kept for the right moment. +{0} Crit Chance per rank.",
     fx=[S("CritChance", 0.02)])
perk("quick_hands", "Quick Hands", R, 1, 3, 4, "Fast fingers, fast steel. +{0} Attack Speed per rank.",
     fx=[S("AttackSpeed", 0.02)])
perk("fortune", "Fortune", R, 2, 0, 3,
     "Luck follows the bold. Slain foes drop better gear, raising loot quality by {0} per rank.",
     fx=[E("LootQuality", 0.05)], pre=["cutpurse"])
perk("backstep_adept", "Backstep Adept", R, 2, 1, 2,
     "Give ground without paying for it. Backsteps cost {0} less Stamina per rank.",
     fx=[E("DodgeStaminaMult", -0.10, "backstep")], pre=["light_feet"])
perk("dirty_fighting", "Dirty Fighting", R, 2, 2, 3,
     "No rules in an alley. Critical hits deal {0} more damage per rank.",
     fx=[S("CritDamage", 0.08)], pre=["keen_edge"])
perk("shadowstep", "Shadowstep", R, 3, 1, 2,
     "Roll out of trouble cheaply. Dodge rolls cost {0} less Stamina per rank.",
     fx=[E("DodgeStaminaMult", -0.08, "roll")], pre=["backstep_adept"])
perk("ruthless", "Ruthless", R, 3, 2, 3,
     "End it quickly. +{0} Crit Chance and attacks cost {1} less Stamina per rank.",
     fx=[S("CritChance", 0.02), E("AttackStaminaMult", -0.04)], pre=["keen_edge"])
perk("lucky_break", "Lucky Break", R, 4, 0, 2,
     "The dice remember you. Slain foes drop better gear, raising loot quality by {0} per rank.",
     fx=[E("LootQuality", 0.05)], pre=["fortune"])
perk("cold_calculation", "Cold Calculation", R, 4, 2, 2,
     "Every opening counted. Critical hits deal {0} more damage per rank.",
     fx=[S("CritDamage", 0.10)], pre=["ruthless"])
perk("shadowdancer", "Shadowdancer", R, 5, 1, 1,
     "You are never where the blow lands. Dodging costs {0} less Stamina, +{1} Move Speed and +{2} Crit Chance.",
     fx=[E("DodgeStaminaMult", -0.10), S("MoveSpeed", 0.3), S("CritChance", 0.04)], pre=["shadowstep"])

# -- Crafter: the bench, the salvage heap, the forge ---------------------------------------------
C = "Crafter"
perk("thrifty_hands", "Thrifty Hands", C, 1, 0, 3,
     "Nothing wasted at the bench. A craft has a {0} chance per rank to give one unit of its largest material back.",
     fx=[E("MaterialSaveChance", 0.04)])
perk("salvager", "Salvager", C, 1, 1, 2, "Strip it clean. Salvaging recovers {0} more of the materials per rank.",
     fx=[E("SalvageYieldBonus", 0.05)])
perk("forge_lore", "Forge Lore", C, 1, 2, 3, "You know how fire behaves. +{0} Fire resistance per rank.",
     fx=[S("FireResist", 5.0)])
perk("stout_back", "Stout Back", C, 1, 3, 4, "Hauling ore builds a frame. +{0} maximum Health per rank.",
     fx=[S("Health", 10.0)])
perk("artisans_eye", "Artisan's Eye", C, 2, 0, 3,
     "You see what a piece teaches. Salvaging earns {0} more experience per rank.",
     fx=[E("CraftXpMult", 0.10)], pre=["thrifty_hands"])
perk("reclaimer", "Reclaimer", C, 2, 1, 2,
     "Nothing is truly broken. Salvaging recovers {0} more of the materials per rank.",
     fx=[E("SalvageYieldBonus", 0.06)], pre=["salvager"])
perk("leather_apron", "Leather Apron", C, 2, 2, 3, "Sparks find leather instead of skin. +{0} Armor per rank.",
     fx=[S("Armor", 2.0)], pre=["forge_lore"])
perk("efficient_hands", "Efficient Hands", C, 3, 0, 2,
     "A practised cut. A craft has a further {0} chance per rank to give a material back.",
     fx=[E("MaterialSaveChance", 0.03)], pre=["thrifty_hands"])
perk("deep_salvage", "Deep Salvage", C, 3, 1, 2,
     "Down to the last rivet. Salvaging recovers {0} more of the materials per rank.",
     fx=[E("SalvageYieldBonus", 0.06)], pre=["reclaimer"])
perk("forge_ward", "Forge Ward", C, 3, 2, 2, "+{0} Fire resistance and +{1} Armor per rank.",
     fx=[S("FireResist", 8.0), S("Armor", 3.0)], pre=["leather_apron"])
perk("bench_mastery", "Bench Mastery", C, 4, 1, 2,
     "The work teaches you back. Salvaging earns {0} more experience per rank.",
     fx=[E("CraftXpMult", 0.05)], pre=["artisans_eye"])
perk("master_artisan", "Master Artisan", C, 5, 1, 1,
     "A craftsman's economy. A craft has a further {0} chance to give a material back, and salvaging recovers {1} more.",
     fx=[E("MaterialSaveChance", 0.07), E("SalvageYieldBonus", 0.08)], pre=["bench_mastery"])

# -- Social: coin, standing, learning ------------------------------------------------------------
So = "Social"
perk("silver_tongue", "Silver Tongue", So, 1, 0, 3,
     "Smooth words, softer prices. Haggling succeeds {0} points more often per rank, wherever a merchant haggles at all.",
     fx=[E("HaggleChanceBonus", 5)])
perk("road_wise", "Road Wise", So, 1, 1, 2,
     "You know what a bed and a ferry should cost. Services cost {0} less per rank.",
     fx=[E("ServicePriceMult", -0.02)])
perk("quick_study", "Quick Study", So, 1, 2, 3, "Lessons stick. You earn {0} more experience per rank.",
     fx=[E("XpGainMult", 0.02)])
perk("well_regarded", "Well Regarded", So, 1, 3, 3,
     "A good name travels. Standing with factions rises {0} faster per rank.",
     fx=[E("RepGainMult", 0.03)])
perk("fair_dealer", "Fair Dealer", So, 2, 0, 3, "A reputation for honest trade. Merchants ask {0} less per rank.",
     fx=[E("BuyDiscount", 0.01)], pre=["silver_tongue"])
perk("appraiser", "Appraiser", So, 2, 1, 3, "You know what your goods are worth. Merchants pay {0} more per rank.",
     fx=[E("SellBonus", 0.01)], pre=["silver_tongue"])
perk("pathfinder", "Pathfinder", So, 2, 2, 3, "You have walked every road twice. +{0} Move Speed per rank.",
     fx=[S("MoveSpeed", 0.10)], pre=["road_wise"])
perk("negotiator", "Negotiator", So, 3, 0, 2,
     "You never open with your real number. Haggling succeeds {0} points more often per rank, wherever a merchant haggles at all.",
     fx=[E("HaggleChanceBonus", 5)], pre=["fair_dealer"])
perk("scholar", "Scholar", So, 3, 2, 2, "Study pays. You earn {0} more experience per rank.",
     fx=[E("XpGainMult", 0.02)], pre=["quick_study"])
perk("respected", "Respected", So, 3, 3, 2, "Doors open. Standing with factions rises {0} faster per rank.",
     fx=[E("RepGainMult", 0.05)], pre=["well_regarded"])
perk("shrewd_buyer", "Shrewd Buyer", So, 4, 0, 2, "You have seen every markup. Merchants ask a further {0} less per rank.",
     fx=[E("BuyDiscount", 0.01)], pre=["negotiator"])
perk("diplomat", "Diplomat", So, 5, 1, 1,
     "Rooms settle when you walk in. Standing with factions rises {0} faster and services cost {1} less.",
     fx=[E("RepGainMult", 0.05), E("ServicePriceMult", -0.01)], pre=["negotiator"])

# -- Ashbound: the corruption's own passives. Always listed; each says which tier opens it ------
Ab = "Ashbound"
perk("cinder_veil", "Cinder Veil", Ab, 1, 0, 3,
     "Ash thickens on the skin. +{0} Fire resistance and +{1} Necrotic resistance per rank. Needs the Touched.",
     fx=[S("FireResist", 5.0), S("NecroticResist", 3.0)], corr=1)
perk("ashborn_might", "Ashborn Might", Ab, 1, 1, 3,
     "The corruption answers when called. +3 Spell Power per rank. Only the Marked may take it.",
     legacy=("SpellPower", "Flat", 3.0), corr=2)
perk("ember_heart", "Ember Heart", Ab, 2, 1, 3,
     "Something burns where the pulse was. +{0} maximum Health and +{1} Physical Power per rank. Only the Ashbound may take it.",
     fx=[S("Health", 15.0), S("PhysicalPower", 2.0)], pre=["ashborn_might"], cost=2, corr=3)


# ---- emit -------------------------------------------------------------------------------------


def description(p):
    return p["desc"].format(*[show(f) for f in p["fx"]])


def pascal(pid):
    return "".join(part.capitalize() for part in pid.split("_"))


def fnum(v):
    return repr(round(float(v), 6))


def existing_uid(path):
    if os.path.exists(path):
        m = re.search(r'uid="(uid://[^"]+)"', io.open(path, encoding="utf-8").read())
        if m:
            return m.group(1)
    return ""


def build_tres():
    out = {}
    for p in PERKS:
        path = os.path.join(TRES_DIR, pascal(p["pid"]) + ".tres")
        subs = []
        for f in p["fx"]:
            sid = "Effect_%d" % (len(subs) + 1)
            lines = ['[sub_resource type="Resource" id="%s"]' % sid, 'script = ExtResource("2_effect")']
            if f["type"] == "effect":
                lines.append("Kind = %d" % KINDS.index(f["kind"]))
                if f["arg"]:
                    lines.append('Arg = "%s"' % f["arg"])
            else:
                lines.append("Stat = %d" % STATS.index(f["stat"]))
                lines.append("ModifierType = %d" % MODS.index(f["mod"]))
            lines.append("ValuePerRank = " + fnum(f["value"]))
            subs.append((sid, lines))

        uid = existing_uid(path)
        head = '[gd_resource type="Resource" script_class="PerkResource" load_steps=%d format=3%s]' % (
            len(subs) + 2, (' uid="%s"' % uid) if uid else "")
        out_lines = [head, "", '[ext_resource type="Script" path="res://src/Progression/PerkResource.cs" id="1_perk"]']
        if subs:
            out_lines.append('[ext_resource type="Script" path="res://src/Progression/PerkEffectResource.cs" id="2_effect"]')
        out_lines.append("")
        for _, lines in subs:
            out_lines.extend(lines)
            out_lines.append("")
        out_lines += ["[resource]", 'script = ExtResource("1_perk")',
                      'Id = "perk.%s"' % p["pid"],
                      'DisplayName = "%s"' % p["name"],
                      'Description = "%s"' % description(p).replace('"', '\\"'),
                      "MaxRank = %d" % p["rank"], "Cost = %d" % p["cost"]]
        if p["legacy"]:
            stat, mod, value = p["legacy"]
            out_lines += ["Stat = %d" % STATS.index(stat), "ModifierType = %d" % MODS.index(mod),
                          "ValuePerRank = " + fnum(value)]
        else:
            out_lines.append("ValuePerRank = 0.0")
        if p["corr"]:
            out_lines.append("MinCorruptionTier = %d" % p["corr"])
        out_lines += ["Branch = %d" % BRANCHES.index(p["branch"]), "Tier = %d" % p["tier"], "Column = %d" % p["col"],
                      "PrerequisiteIds = Array[String]([%s])" % ", ".join('"%s"' % x for x in p["pre"]),
                      "BranchPointsRequired = %d" % p["bp"]]
        if p["cap"]:
            out_lines.append("IsCapstone = true")
        if subs:
            out_lines.append("Effects = [%s]" % ", ".join('SubResource("%s")' % sid for sid, _ in subs))
        out[path] = "\n".join(out_lines) + "\n"
    return out


def csv_cell(text):
    return '"%s"' % text.replace('"', '""') if any(c in text for c in ',"\n') else text


def build_locale(existing):
    lines = [MARK_BEGIN]
    for p in PERKS:
        lines.append("perk.%s.name,%s" % (p["pid"], csv_cell(p["name"])))
        lines.append("perk.%s.desc,%s" % (p["pid"], csv_cell(description(p))))
    lines.append(MARK_END)
    # strings.csv is CRLF; a block spliced in with LF would rewrite itself on every run and make
    # --check permanently dirty (same trap as gen_map_locations.py).
    sep = "\r\n" if "\r\n" in existing else "\n"
    block = sep.join(lines)
    if MARK_BEGIN in existing:
        return re.sub(re.escape(MARK_BEGIN) + r".*?" + re.escape(MARK_END), lambda _: block, existing, flags=re.S)
    return existing + ("" if existing.endswith(sep) else sep) + block + sep


def check_table():
    ids = [p["pid"] for p in PERKS]
    assert len(ids) == len(set(ids)), "duplicate perk id"
    seen = set()
    by_id = {p["pid"]: p for p in PERKS}
    for p in PERKS:
        cell = (p["branch"], p["tier"], p["col"])
        assert cell not in seen, "two perks share %s" % (cell,)
        seen.add(cell)
        for q in p["pre"]:
            pre = by_id[q[len("perk."):]]
            assert pre["branch"] == p["branch"] and pre["tier"] < p["tier"], "%s: bad prerequisite %s" % (p["pid"], q)
    totals = {}
    for p in PERKS:
        totals[p["branch"]] = totals.get(p["branch"], 0) + p["rank"] * p["cost"]
    return totals


def main():
    check = "--check" in sys.argv
    totals = check_table()
    changed = []
    if not os.path.isdir(TRES_DIR):
        os.makedirs(TRES_DIR)

    wanted = build_tres()
    for path, text in wanted.items():
        old = io.open(path, encoding="utf-8", newline="").read() if os.path.exists(path) else None
        if old != text:
            changed.append(path)
            if not check:
                io.open(path, "w", encoding="utf-8", newline="").write(text)

    # A perk file the table no longer names is stale; the generator owns the whole directory.
    for name in sorted(os.listdir(TRES_DIR)):
        full = os.path.join(TRES_DIR, name)
        if name.endswith(".tres") and full not in wanted:
            changed.append(full)
            if not check:
                os.remove(full)

    old_csv = io.open(CSV, encoding="utf-8", newline="").read()
    new_csv = build_locale(old_csv)
    if new_csv != old_csv:
        changed.append(CSV)
        if not check:
            io.open(CSV, "w", encoding="utf-8", newline="").write(new_csv)

    summary = "%d perks; branch points: %s" % (len(PERKS), ", ".join("%s %d" % kv for kv in totals.items()))
    if check:
        for c in changed:
            print("WOULD CHANGE", os.path.relpath(c, ROOT))
        print("%s; %d file(s) out of date" % (summary, len(changed)))
        return 1 if changed else 0
    print("%s; wrote %d file(s)" % (summary, len(changed)))
    return 0


if __name__ == "__main__":
    sys.exit(main())
