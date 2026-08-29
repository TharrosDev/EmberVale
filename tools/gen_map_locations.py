#!/usr/bin/env python3
"""Author the Phase 39.5A map-location catalogue.

Generates three things that MUST agree with each other, from one table, so they cannot drift:

  1. data/map_locations/*.tres      — the catalogue (what a place is, what it links to)
  2. data/locale/strings.csv        — its player-facing name (and description, for settlements)
  3. scenes/regions/**/*.tscn       — a MapLocationComponent parented to the thing it names

(3) is the position, and it is the only record of it. See MapLocationResource's header for why.

Idempotent: re-running replaces the generated .tres files, rewrites the generated locale block,
and refuses to insert a marker into a scene that already has one for that id.

Usage:  python tools/gen_map_locations.py [--check]
        --check exits 1 if anything would change, without writing (a gate, not a generator).
"""

import io
import os
import re
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

# MapCategory member order in src/World/MapCategory.cs. A .tres stores an enum as its index, so
# THIS LIST IS A CONTRACT WITH THAT FILE: reordering the enum without reordering this silently
# recategorises every location. --validate cross-checks the result, which is what catches it.
CATEGORY = [
    "Capital", "Town", "Village", "Outpost", "Camp", "Wilds",
    "Smith", "Merchant", "Alchemist", "Provisioner", "Outfitter", "Jeweller", "Scriptorium",
    "Inn", "Bank", "Stable", "Trainer", "Contracts", "Crafting", "Arena",
    "Mine", "Dungeon", "Landmark",
    "Gate", "Waystone",
    "Home", "Waypoint",
]

# id-tail, category, cell, anchor node path in the scene, name, shop, service, dialogue, travel,
# reveal-with-cell, description (settlements only)
#
# "anchor" is a node path inside the cell scene. "." is the cell root (so the marker lands on the
# cell centre); anything else is the stall, counter or keeper the location IS.
L = []


def add(cell_file, tail, category, anchor, name, shop="", service="", dialogue="",
        travel="", reveal=False, desc="", name_key="", prop=""):
    L.append(dict(cell_file=cell_file, tail=tail, category=category, anchor=anchor, name=name,
                  shop=shop, service=service, dialogue=dialogue, travel=travel, reveal=reveal,
                  desc=desc, name_key=name_key, prop=prop))


# ── Ember Crown ──────────────────────────────────────────────────────────────────────────────
# The capital. Every settlement reveals with its cell; ⚠️ a region loads WHOLE (invariant 1), so in
# practice that means "known on entering the region" — which is the intent for the towns of a
# homeland, and is why nothing below that a player must FIND uses reveal=True.
add("ember_crown/town_hub", "ember_crown.town", "Capital", ".", "Ember Crown", reveal=True,
    desc="The seat of the realm. Its square holds the smith, the apothecary and the inn; the "
         "guild board stands at the south road.")
add("ember_crown/town_hub", "ember_crown.waystone", "Waystone", "Waystone", "Ember Crown Waystone",
    travel="travel.ember_crown.waystone")
add("ember_crown/town_hub", "ember_crown.smith", "Smith", "VendorSmith", "The Iron Anvil",
    shop="shop.ember_crown.smith", dialogue="dialogue.smith")
add("ember_crown/town_hub", "ember_crown.goods", "Merchant", "VendorGoods", "The Long Counter",
    shop="shop.ember_crown.goods", dialogue="dialogue.vendor_goods")
add("ember_crown/town_hub", "ember_crown.apothecary", "Alchemist", "VendorAlch", "The Green Retort",
    shop="shop.ember_crown.apothecary", dialogue="dialogue.apothecary")
add("ember_crown/town_hub", "ember_crown.inn", "Inn", "Innkeeper", "The Ember Rest",
    service="service.ember_crown.inn", dialogue="dialogue.innkeeper")
add("ember_crown/town_hub", "ember_crown.trainer", "Trainer", "Trainer", "The Trainer's Yard",
    service="service.ember_crown.trainer")
add("ember_crown/town_hub", "ember_crown.stable", "Stable", "Stablemaster", "The Ember Crown Stable",
    service="service.ember_crown.stable")
add("ember_crown/town_hub", "ember_crown.vault", "Bank", "GuildVault", "The Ember Crown Vault",
    service="service.ember_crown.bank")
add("ember_crown/town_hub", "ember_crown.guild_board", "Contracts", "GuildMarker", "The Guild Board",
    dialogue="dialogue.guild_board")
add("ember_crown/town_hub", "ember_crown.commission", "Contracts", "Nav/OrderBench", "The Order Bench",
    service="service.ember_crown.commission")
add("ember_crown/town_hub", "ember_crown.traveller", "Merchant", "WanderingTrader", "The Wandering Trader",
    shop="shop.ember_crown.traveller")
# One pin for three stations standing within two metres of each other: three near-identical
# markers on top of one another is the icon soup §50 names, and it is one place to a player.
add("ember_crown/town_hub", "ember_crown.crafting", "Crafting", "StationForge", "The Crafting Yard")
add("ember_crown/town_hub", "shrine.solaryn", "Landmark", "Nav/ShrineSolaryn", "Shrine of Solaryn")

# The market. Its display name is REUSED from the waystone's key rather than authored again —
# one place, one name, and renaming the waystone renames the map pin with it.
add("ember_crown/embermarket", "embermarket.market", "Town", ".", "", reveal=True,
    name_key="travel.ember_crown.embermarket.name",
    desc="Rows of stalls under awnings, a plaza at the north end, and the realm's densest run of "
         "trades — twelve merchants between the aisles and the forecourt.")
add("ember_crown/embermarket", "embermarket.waystone", "Waystone", "MarketWaystone", "Embermarket Waystone",
    travel="travel.ember_crown.embermarket")
add("ember_crown/embermarket", "embermarket.provisions", "Provisioner", "MerchantCorvin", "Corvin's Provisions",
    shop="shop.embermarket.provisions", dialogue="dialogue.corvin")
add("ember_crown/embermarket", "embermarket.fishmonger", "Provisioner", "MerchantHana", "Hana's Slab",
    shop="shop.embermarket.fishmonger", dialogue="dialogue.hana")
add("ember_crown/embermarket", "embermarket.weaver", "Outfitter", "MerchantSable", "Sable's Loom",
    shop="shop.embermarket.weaver", dialogue="dialogue.sable")
add("ember_crown/embermarket", "embermarket.scriptorium", "Scriptorium", "MerchantTam", "Tam's Scriptorium",
    shop="shop.embermarket.scriptorium", dialogue="dialogue.tam")
add("ember_crown/embermarket", "embermarket.collier", "Merchant", "MerchantDunmore", "The Collier's Stall",
    shop="shop.embermarket.collier", dialogue="dialogue.ash")
add("ember_crown/embermarket", "embermarket.ironmonger", "Smith", "MerchantGilda", "Gilda's Ironmongery",
    shop="shop.embermarket.ironmonger", dialogue="dialogue.gilda")
add("ember_crown/embermarket", "embermarket.tannery", "Outfitter", "MerchantPerrin", "Perrin's Tannery",
    shop="shop.embermarket.tannery", dialogue="dialogue.perrin")
add("ember_crown/embermarket", "embermarket.jeweller", "Jeweller", "MerchantNessa", "Nessa's Bench",
    shop="shop.embermarket.jeweller", dialogue="dialogue.nessa")
add("ember_crown/embermarket", "embermarket.herbalist", "Alchemist", "MerchantOdo", "Odo's Herbs",
    shop="shop.embermarket.herbalist", dialogue="dialogue.odo")
add("ember_crown/embermarket", "embermarket.joiner", "Outfitter", "MerchantHalvard", "Halvard's Joinery",
    shop="shop.embermarket.joiner", dialogue="dialogue.halvard")
add("ember_crown/embermarket", "embermarket.consignment", "Contracts", "MerchantMirelle", "Mirelle's Consignment",
    shop="shop.embermarket.consignment", dialogue="dialogue.mirelle")
add("ember_crown/embermarket", "embermarket.caravan", "Merchant", "TraderSera", "Sera's Caravan",
    shop="shop.embermarket.caravan")
add("ember_crown/embermarket", "embermarket.curios", "Jeweller", "TraderQuill", "Quill's Curios",
    shop="shop.embermarket.curios")
add("ember_crown/embermarket", "embermarket.collect", "Contracts", "ClerkHalder", "The Collection Desk",
    service="service.embermarket.collect")
add("ember_crown/embermarket", "embermarket.appraisal", "Jeweller", "BrightcutScales", "The Brightcut Scales",
    service="service.embermarket.appraisal")
add("ember_crown/embermarket", "embermarket.board", "Contracts", "MarketNotice", "The Market Notice",
    dialogue="dialogue.market_board")
add("ember_crown/embermarket", "shrine.nyth", "Landmark", "Nav/ShrineNyth", "Shrine of Nyth")

# The tolled crossing.
add("ember_crown/crossway_post", "crossway.post", "Outpost", ".", "", reveal=True,
    name_key="travel.ember_crown.crossway_post.name",
    desc="The wardens' post on the road between the realms. Nothing crosses here without the toll, "
         "a permit, or a word with the man at the side of the gate.")
add("ember_crown/crossway_post", "crossway.waystone", "Waystone", "CrosswayWaystone", "Crossway Waystone",
    travel="travel.ember_crown.crossway_post")
add("ember_crown/crossway_post", "crossway.board", "Contracts", "Nav/CaravanBoard", "The Caravan Board",
    service="service.crossway.contracts")
add("ember_crown/crossway_post", "crossway.permit", "Gate", "RoadWarden", "The Warden's Desk",
    service="service.crossway.permit")
add("ember_crown/crossway_post", "crossway.bribe", "Gate", "GateHand", "The Gate Hand",
    service="service.crossway.bribe")
add("ember_crown/crossway_post", "crossway.search", "Gate", "SearchWarden", "The Search Table",
    service="service.crossway.search")
add("ember_crown/crossway_post", "crossway.impound", "Gate", "ImpoundClerk", "The Impound Counter",
    service="service.crossway.impound")
add("ember_crown/crossway_post", "crossway.mercenary", "Contracts", "Mercenary", "The Hiring Post",
    service="service.crossway.mercenary", dialogue="dialogue.wren")
add("ember_crown/crossway_post", "shrine.tharos", "Landmark", "Nav/ShrineTharos", "Shrine of Tharos")

# The mine.
add("ember_crown/emberdeep_mine", "emberdeep.mine", "Mine", ".", "", reveal=True,
    name_key="travel.ember_crown.emberdeep_mine.name",
    desc="Ore out of the hill and food in at any price. The factor sells the realm's cheapest metal; "
         "the quartermaster pays its best coin for a full pack of provisions.")
add("ember_crown/emberdeep_mine", "emberdeep.waystone", "Waystone", "MineWaystone", "Emberdeep Waystone",
    travel="travel.ember_crown.emberdeep_mine")
add("ember_crown/emberdeep_mine", "emberdeep.factor", "Merchant", "Bregan", "The Factor's Office",
    shop="shop.emberdeep.factor", dialogue="dialogue.bregan")
add("ember_crown/emberdeep_mine", "emberdeep.quartermaster", "Provisioner", "Marta", "The Quartermaster's Store",
    shop="shop.emberdeep.quartermaster", dialogue="dialogue.marta")

# The lake landing.
add("ember_crown/tarn_landing", "tarn.landing", "Village", ".", "", reveal=True,
    name_key="travel.ember_crown.tarn_landing.name",
    desc="A jetty, a smokehouse and a chandlery on the tarn's edge.")
add("ember_crown/tarn_landing", "tarn.waystone", "Waystone", "LandingWaystone", "Landing Waystone",
    travel="travel.ember_crown.tarn_landing")
add("ember_crown/tarn_landing", "tarn.curer", "Provisioner", "Wenna", "Wenna's Smokehouse",
    shop="shop.tarn.curer", dialogue="dialogue.wenna")
add("ember_crown/tarn_landing", "tarn.chandler", "Outfitter", "Odger", "Odger's Chandlery",
    shop="shop.tarn.chandler", dialogue="dialogue.odger")
add("ember_crown/tarn_landing", "shrine.elyndra", "Landmark", "Nav/ShrineElyndra", "Shrine of Elyndra")

# The hollow.
add("ember_crown/hollowreach", "hollowreach.reach", "Village", ".", "", reveal=True,
    name_key="travel.ember_crown.hollowreach.name",
    desc="Boat-builders and salvagers in the low ground, and a bones table that will take your coin "
         "on a throw.")
add("ember_crown/hollowreach", "hollowreach.waystone", "Waystone", "ReachWaystone", "Hollowreach Waystone",
    travel="travel.ember_crown.hollowreach")
add("ember_crown/hollowreach", "hollowreach.hull", "Outfitter", "Sedge", "Sedge's Hull Works",
    shop="shop.hollowreach.hull", dialogue="dialogue.sedge")
add("ember_crown/hollowreach", "hollowreach.locker", "Merchant", "Coyle", "Coyle's Locker",
    shop="shop.hollowreach.locker", dialogue="dialogue.coyle")
add("ember_crown/hollowreach", "hollowreach.bones", "Arena", "Nav/BonesTable", "The Bones Table",
    service="service.hollowreach.bones")

# The homestead, the arena and the wilds.
add("ember_crown/ashfall_homestead", "ashfall.homestead", "Camp", ".", "Ashfall Homestead", reveal=True,
    desc="A farmstead on the ash flats east of the city, with a bed for anyone caught out after dark.")
add("ember_crown/ashfall_homestead", "ashfall.bed", "Inn", "AshfallBed", "The Ashfall Bed",
    service="service.ashfall.bed")
# The player's own holding — the realm's only property, and it was missing from the map entirely
# until a continuity audit found it. Its name is REUSED from the property rather than authored
# again, the same rule the five waystone settlements follow: one place, one name.
add("ember_crown/ashfall_homestead", "ashfall.cottage", "Home", "CottageDeed", "",
    name_key="property.cottage.name", prop="property.ember_crown.cottage")
add("ember_crown/ashfall_homestead", "shrine.veyra", "Landmark", "Nav/ShrineVeyra", "Shrine of Veyra")
add("ember_crown/arena", "ember_crown.arena", "Arena", ".", "The Ember Arena", reveal=True,
    desc="A ring of tiered stone past the north gate, well outside the walls.")
add("ember_crown/arena", "shrine.drakar", "Landmark", "Nav/ShrineDrakar", "Shrine of Drakar")
add("ember_crown/wilds_north", "wilds.north", "Wilds", ".", "The Northern Wilds", reveal=True,
    desc="Open country north of the city. Goblins range here.")
add("ember_crown/wilds_west", "wilds.west", "Wilds", ".", "The Western Wilds", reveal=True,
    desc="Broken ground west of the tarn.")

# ── Frostfang Reach ──────────────────────────────────────────────────────────────────────────
# Settlement-tier coverage only. Frostfang's interiors are a the 2026-08-28 layout rebuild world-layout question, and
# ⚠️ the three roosts deliberately do NOT reveal with their cell: a lair you have not found should
# not appear the moment you cross the border.
add("frostfang_reach/clan_hold", "frostfang.clan_hold", "Town", ".", "The Frostfang Clan Hold", reveal=True,
    desc="Longhouses banked against the wind, and the clan that keeps them.")
add("frostfang_reach/clan_hold", "frostfang.waystone", "Waystone", "Waystone", "Clan Hold Waystone",
    travel="travel.frostfang_reach.clan_hold")
add("frostfang_reach/glacier", "frostfang.glacier", "Wilds", ".", "The Glacier", reveal=True,
    desc="Blue ice and meltwater, north of the hold.")
add("frostfang_reach/dragon_roost", "frostfang.dragon_roost", "Dungeon", ".", "The Dragon Roost")
add("frostfang_reach/ash_roost", "frostfang.ash_roost", "Dungeon", ".", "The Ash Roost")
add("frostfang_reach/ancient_aerie", "frostfang.ancient_aerie", "Dungeon", ".", "The Ancient Aerie")


CELL_ID = {
    "ember_crown/town_hub": "ember_crown.town_hub",
    "ember_crown/embermarket": "ember_crown.embermarket",
    "ember_crown/crossway_post": "ember_crown.crossway_post",
    "ember_crown/emberdeep_mine": "ember_crown.emberdeep_mine",
    "ember_crown/tarn_landing": "ember_crown.tarn_landing",
    "ember_crown/hollowreach": "ember_crown.hollowreach",
    "ember_crown/ashfall_homestead": "ember_crown.ashfall_homestead",
    "ember_crown/arena": "ember_crown.arena",
    "ember_crown/wilds_north": "ember_crown.wilds_north",
    "ember_crown/wilds_west": "ember_crown.wilds_west",
    "frostfang_reach/clan_hold": "frostfang_reach.clan_hold",
    "frostfang_reach/glacier": "frostfang_reach.glacier",
    "frostfang_reach/dragon_roost": "frostfang_reach.dragon_roost",
    "frostfang_reach/ash_roost": "frostfang_reach.ash_roost",
    "frostfang_reach/ancient_aerie": "frostfang_reach.ancient_aerie",
}

TRES_DIR = os.path.join(ROOT, "data", "map_locations")
CSV = os.path.join(ROOT, "data", "locale", "strings.csv")
MARK_BEGIN = "# --- BEGIN generated map location names (tools/gen_map_locations.py) ---"
MARK_END = "# --- END generated map location names ---"

HEADER = """[gd_resource type="Resource" script_class="MapLocationResource" format=3]

[ext_resource type="Script" path="res://src/World/MapLocationResource.cs" id="1_maploc"]

; {title} — a map location (Phase 39.5A).
;
; GENERATED by tools/gen_map_locations.py. Edit the table there, not this file.
;
; @ IT HAS NO POSITION, AND THAT IS THE DESIGN. Where this stands is the transform of the
; MapLocationComponent parented to '{anchor}' in scenes/regions/{cell_file}.tscn. Move that node
; and the pin moves with it; there is no coordinate here to fall out of step with the world.
;
; The links below are ids, never copies: the map asks ShopDatabase what this sells and
; DialogueDatabase who keeps it, so a renamed shop or a re-priced service needs no edit here.

[resource]
script = ExtResource("1_maploc")
"""


def pascal(tail):
    return "".join(p.capitalize() for p in re.split(r"[._]", tail))


def tres_body(item):
    key = item["name_key"] or "location.{0}.name".format(item["tail"])
    desc_key = "location.{0}.desc".format(item["tail"]) if item["desc"] else ""
    return (
        'Id = "location.{tail}"\n'
        'NameKey = "{name_key}"\n'
        'DescriptionKey = "{desc_key}"\n'
        "Category = {cat}\n"
        "TierFromCategory = true\n"
        "Tier = 2\n"
        'CellId = "{cell}"\n'
        'ShopId = "{shop}"\n'
        'ServiceId = "{service}"\n'
        'DialogueId = "{dialogue}"\n'
        'PropertyId = "{prop}"\n'
        'TravelNodeId = "{travel}"\n'
        "RevealWithCell = {reveal}\n"
        'RequiredFlagId = ""\n'
    ).format(
        tail=item["tail"], name_key=key, desc_key=desc_key,
        cat=CATEGORY.index(item["category"]), cell=CELL_ID[item["cell_file"]],
        shop=item["shop"], service=item["service"], dialogue=item["dialogue"],
        prop=item["prop"], travel=item["travel"],
        reveal="true" if item["reveal"] else "false")


def build_tres():
    out = {}
    for item in L:
        title = item["name"] or item["name_key"]
        text = HEADER.format(title=title, anchor=item["anchor"],
                             cell_file=item["cell_file"]) + tres_body(item)
        out[os.path.join(TRES_DIR, pascal(item["tail"]) + ".tres")] = text.replace("@", "⚠️")
    return out


def build_locale(existing):
    lines = [MARK_BEGIN]
    for item in L:
        if not item["name_key"]:                       # only author a name we do not already have
            lines.append("location.{0}.name,{1}".format(item["tail"], item["name"]))
        if item["desc"]:
            lines.append('location.{0}.desc,"{1}"'.format(item["tail"], item["desc"]))
    lines.append(MARK_END)

    # ⚠️ Build the block with the FILE'S newline, not "\n". strings.csv is CRLF, and a block
    # spliced in with LF re-writes itself on every run — which makes --check permanently dirty and
    # therefore useless as a gate.
    sep = "\r\n" if "\r\n" in existing else "\n"
    block = sep.join(lines)

    if MARK_BEGIN in existing:
        return re.sub(re.escape(MARK_BEGIN) + r".*?" + re.escape(MARK_END),
                      lambda _: block, existing, flags=re.S)

    if not existing.endswith(sep):
        existing += sep
    return existing + block + sep


EXT = ('[ext_resource type="Script" path="res://src/World/MapLocationComponent.cs" '
       'id="maploc_39_5a"]')


def build_scenes():
    """Returns {scene path: new text} for every scene that needs markers added."""
    by_scene = {}
    for item in L:
        by_scene.setdefault(item["cell_file"], []).append(item)

    out = {}
    for cell_file, items in by_scene.items():
        path = os.path.join(ROOT, "scenes", "regions", cell_file + ".tscn")
        text = io.open(path, encoding="utf-8", newline="").read()
        nl = "\r\n" if "\r\n" in text else "\n"
        original = text

        if EXT not in text:
            last = None
            for m in re.finditer(r"^\[ext_resource .*$", text, re.M):
                last = m
            if last is None:
                raise SystemExit("no ext_resource block in " + path)
            text = text[:last.end()] + nl + EXT + text[last.end():]

        additions = []
        for item in items:
            lid = "location." + item["tail"]
            if 'LocationId = "{0}"'.format(lid) in text:
                continue                                # already placed; never duplicate
            anchor = item["anchor"]
            if not re.search(r'^\[node name="{0}"'.format(re.escape(anchor.split("/")[-1])),
                             text, re.M) and anchor != ".":
                raise SystemExit("anchor '{0}' not found in {1}".format(anchor, path))
            additions.append(
                nl + '[node name="MapPin" type="Node3D" parent="{0}"]'.format(anchor) + nl +
                'script = ExtResource("maploc_39_5a")' + nl +
                'LocationId = "{0}"'.format(lid) + nl)

        if additions:
            if not text.endswith(nl):
                text += nl
            text += "".join(additions)

        if text != original:
            out[path] = text
    return out


def main():
    check = "--check" in sys.argv
    changed = []

    if not os.path.isdir(TRES_DIR):
        if check:
            print("MISSING", TRES_DIR)
            return 1
        os.makedirs(TRES_DIR)

    for path, text in build_tres().items():
        old = io.open(path, encoding="utf-8", newline="").read() if os.path.exists(path) else None
        if old != text:
            changed.append(path)
            if not check:
                io.open(path, "w", encoding="utf-8", newline="").write(text)

    csv_old = io.open(CSV, encoding="utf-8", newline="").read()
    csv_new = build_locale(csv_old)
    if csv_new != csv_old:
        changed.append(CSV)
        if not check:
            io.open(CSV, "w", encoding="utf-8", newline="").write(csv_new)

    for path, text in build_scenes().items():
        changed.append(path)
        if not check:
            io.open(path, "w", encoding="utf-8", newline="").write(text)

    if check:
        for c in changed:
            print("WOULD CHANGE", os.path.relpath(c, ROOT))
        print("{0} location(s); {1} file(s) out of date".format(len(L), len(changed)))
        return 1 if changed else 0

    print("{0} location(s); wrote {1} file(s)".format(len(L), len(changed)))
    return 0


if __name__ == "__main__":
    sys.exit(main())
