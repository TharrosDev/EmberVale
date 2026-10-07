#!/usr/bin/env python3
"""Street plans for the realm's settlements, in WORLD coordinates (2026-09 world rebuild).

Consumed by tools/compose_district.py (scene nodes) and by the region specs (terraces as ground
areas). A settlement is authored the way it would be surveyed: frontages along its streets, a yard
behind a house, a gate where a road crosses a wall — never as a ring of buildings around a pin.

⚠️ Streets come from the region spec's roads(). A frontage is set back from its street by the
street's half-width + shoulder + half the building's depth, so a house can never stand on a road
(NOW.md invariant 21) — and `python tools/compose_district.py` reports any that does.
"""

from __future__ import annotations

import math
from dataclasses import dataclass


@dataclass(frozen=True)
class P:
    """One placed structure: kind (tools/compose_district.py catalogue), unique name, world x/z,
    yaw in degrees (0 = its front/door faces +Z, south), optional uniform scale, landmark flag."""
    kind: str
    name: str
    x: float
    z: float
    yaw: float = 0.0
    scale: float = 1.0
    landmark: bool = False
    # A standalone structure (not on a frontage terrace) that needs its own levelled pad.
    pad: bool = False
    # Metres added to the node's height above the ground. A generated landmark has no base course,
    # so a giant is sunk a little and its foot never shows daylight on the downhill side.
    y: float = 0.0
    # Multiplies the wrapper scene's own stone tint (r, g, b): one mesh, a realm's colour.
    tint: tuple[float, float, float] | None = None
    # Half-size of the pad, when it must not follow the footprint. The towers of the generated set
    # are wider than the ones they replaced; their pads keep the size they were surveyed at, so the
    # terrain inside the lattice is the terrain every route and probe was checked on.
    pad_half: float = 0.0
    # Front-to-back thinning of the model (1 = as generated). Only for a single-box piece.
    depth: float = 1.0


# Realm stone colours, as multipliers on a wrapper's own tint. Unrendered when written: tune by render.
FROST = (0.86, 0.92, 1.0)      # Frostfang: cold grey
SOOT = (0.6, 0.57, 0.56)       # burnt ground: the Ashen Wilds, the ash roosts, Stormcrown
SAND = (1.18, 1.04, 0.82)      # Sunspire sandstone
PALE = (1.06, 1.04, 1.1)       # the hidden realm's bleached marble
ASHLIGHT = (0.95, 0.88, 0.9)   # the Celestial terraces: pink-grey, as its rubble is


# Terraces: the levelled strip a frontage stands on, as (a, b, half_width) in world metres. The region
# spec turns each into a flat Ridge landform (level ground at the country's own height, following the
# street) — a rotated ground area in effect, which a Yard cannot be.
TERRACES: list[tuple[tuple[float, float], tuple[float, float], float]] = []


def facing(x: float, z: float, tx: float, tz: float) -> float:
    """Yaw that turns a building at (x, z) so its door faces the point (tx, tz)."""
    return math.degrees(math.atan2(tx - x, tz - z))


def toward(x: float, z: float, tx: float, tz: float) -> float:
    """Yaw that turns a piece at (x, z) so its +Z front looks at (tx, tz), in the sense
    compose_district.basis() actually applies (a layout yaw is a turn of minus that angle in Godot).
    The monumental layer uses this. `facing` above is its mirror image in X and is left as it is:
    every kit building in the capital was placed, padded and checked with it."""
    return -facing(x, z, tx, tz)


def frontage(prefix: str, a: tuple[float, float], b: tuple[float, float], stations: list[float],
             side: int, setback: float, kinds: list[str], jitter: list[float] | None = None) -> list[P]:
    """Buildings along the street a->b at distances `stations` (metres from a), on `side` (+1 left of
    travel, -1 right), `setback` metres from the centreline, each turned to face the street. A small
    per-building jitter keeps the frontage from reading as a ruled line."""
    ax, az = a
    bx, bz = b
    length = math.hypot(bx - ax, bz - az)
    ux, uz = (bx - ax) / length, (bz - az) / length
    nx, nz = -uz * side, ux * side
    out = []
    for i, s in enumerate(stations):
        j = (jitter or [0.0])[i % len(jitter or [0.0])]
        cx, cz = ax + ux * s + nx * (setback + j), az + uz * s + nz * (setback + j)
        sx, sz = ax + ux * s, az + uz * s
        yaw = facing(cx, cz, sx, sz) + ((i * 7) % 11 - 5)   # houses are never quite square to a lane
        kind = kinds[i % len(kinds)]
        out.append(P(kind, f"{prefix}{i}", round(cx, 2), round(cz, 2), round(yaw, 1)))
        # One terrace per building, levelled at the building's own centre. A levelling landform takes
        # its height from the ground under its first point, so a single strip along a sloping street
        # held the whole frontage at the level of its first house: 5 m of berm beside the market lane.
        TERRACES.append(((round(cx, 2), round(cz, 2)),
                         (round(cx + ux * 0.5, 2), round(cz + uz * 0.5, 2)),
                         round(max(footprint_of(kind)) * 0.5 + 3.5, 2)))
    return out


def pads(items: list[P]) -> list[tuple[float, float, float, float]]:
    """(x, z, half_x, half_z) for every structure that asked for its own levelled pad."""
    out = []
    for item in items:
        if item.pad:
            w, d = footprint_of(item.kind)
            half = item.pad_half or max(w, d) * 0.5 * item.scale + 1.5
            out.append((item.x, item.z, round(half, 2), round(half, 2)))
    return out


def footprint_of(kind: str) -> tuple[float, float]:
    from compose_district import footprint
    return footprint(kind)


# ------------------------------------------------------------------------------------------------
# The Ember Crown's capital
# ------------------------------------------------------------------------------------------------
# Streets (world, from region_spec_ember.roads()):
#   west road    (5,-34) -> (-62,-48) -> (-138,-76) Kingsbridge
#   market lane  (24,0) -> (4,14) -> (-48,30) -> (-71,44)
#   east road    (65,-32) -> (142,-52) -> (232,-104)
#   Kingsway     (29,-60) -> (30,-104) -> (-8,-180);  citadel ramp (30,-104) -> (60,-118) -> (84,-138)
#   farm lane    (-34,73.5) -> (38,112);  Hollowreach road (-86,82) -> (-146,132)

WEST_ROAD = [(5.0, -34.0), (-62.0, -48.0), (-138.0, -76.0)]
MARKET_LANE = [(24.0, 0.0), (4.0, 14.0), (-48.0, 30.0), (-71.0, 44.0)]
EAST_ROAD = [(65.0, -32.0), (142.0, -52.0), (232.0, -104.0)]


def capital() -> dict[str, list]:
    bridgeward = []
    # Bridgeward: the west road's frontages between the Crown Square and the west gate, dense near the
    # city and thinning toward the river. North side is the older fabric (townhouses), south side
    # cottages with yards running down toward the fields.
    bridgeward += frontage("BwN", WEST_ROAD[1], WEST_ROAD[2], [6, 17, 29, 44], +1, 9.5,
                           ["townhouse", "townhouse_balcony", "house_a", "shop_awning"], [0.0, 1.2, -0.6])
    bridgeward += frontage("BwS", WEST_ROAD[1], WEST_ROAD[2], [10, 24, 40], -1, 9.0,
                           ["cottage_shuttered", "house_b", "cottage_modular", "cottage"], [0.4, -0.8, 1.5])
    bridgeward += [
        # The west gate: a tower each side of the road where it leaves the city.
        # 13 m against the old 8 m: the road passes 8 m from each, which is all the width there is.
        P("gate_tower", "WestGateN", -109.8, -74.2, -70.0, scale=0.72, landmark=True, pad=True, pad_half=3.0, y=-0.3),
        P("gate_tower", "WestGateS", -115.4, -59.2, -70.0, scale=0.72, pad=True, pad_half=3.0, y=-0.3),
        P("well", "BridgewardWell", -86.0, -40.0, scale=0.85),
        P("cart", "BridgewardCart", -79.0, -44.5, 200.0),
    ]
    town = []
    # The upper town's fabric north of the square and along the lane down to the market.
    town += frontage("LnE", MARKET_LANE[1], MARKET_LANE[2], [8, 22, 38], +1, 8.5,
                     ["townhouse_wide", "house_a", "townhouse"], [0.0, 1.0])
    town += frontage("LnW", MARKET_LANE[1], MARKET_LANE[2], [14, 30, 46], -1, 8.0,
                     ["house_b", "townhouse_balcony", "cottage_shuttered"], [0.5, -0.5])
    town += frontage("WrN", WEST_ROAD[0], WEST_ROAD[1], [16, 34, 52], +1, 9.5,
                     ["townhouse", "inn_courtyard", "townhouse_balcony"], [0.0, 2.5, 0.0])
    town += frontage("WrS", WEST_ROAD[0], WEST_ROAD[1], [22, 44, 60], -1, 9.0,
                     ["house_a", "shop_awning", "house_b"], [0.6, -0.4])
    town += [
        # The Hall of the Crown on the Kingsway below the citadel ramp; the barracks at the ramp foot.
        P("inn_courtyard", "HallOfTheCrown", 2.0, -86.0, facing(2.0, -86.0, 29.0, -80.0), landmark=True, pad=True),
        P("longhouse_stone", "Barracks", 58.0, -84.0, facing(58.0, -84.0, 30.0, -92.0), pad=True),
        # The north gate towers on the Kingsway, where the city gives way to the King's Approach.
        # The Kingsway is the realm's great road and its gate is the tall one: 18 m.
        P("gate_tower", "NorthGateW", 19.0, -94.0, 0.0, scale=1.0, landmark=True, pad=True, pad_half=3.0, y=-0.4),
        P("gate_tower", "NorthGateE", 41.0, -97.0, 0.0, scale=1.0, landmark=True, pad=True, pad_half=3.0, y=-0.4),
    ]
    eastgate = []
    eastgate += frontage("EgN", EAST_ROAD[0], EAST_ROAD[1], [14, 30, 48, 66], +1, 9.5,
                         ["workshop_open", "townhouse", "workshop_open", "farmhouse_long"], [0.0, 1.5, -1.0])
    eastgate += frontage("EgS", EAST_ROAD[0], EAST_ROAD[1], [8, 24, 40, 60], -1, 9.0,
                         ["house_b", "workshop_open", "cottage_modular", "longhouse_stone"], [0.8, -0.6])
    eastgate += [
        P("gate_tower", "EastGateN", 150.0, -66.0, 60.0, scale=0.82, landmark=True, pad=True, pad_half=3.0, y=-0.3),
        P("gate_tower", "EastGateS", 160.0, -46.0, 60.0, scale=0.9, pad=True, pad_half=3.0, y=-0.4),
        P("timber_stack", "TimberYardA", 176.0, -44.0, 80.0),
        P("timber_stack", "TimberYardB", 181.0, -38.0, 95.0),
        P("cart", "TimberCart", 171.0, -36.0, 110.0),
    ]
    # Frontage houses that stand where their terrace meets a bend in the ground get a pad of their own.
    from dataclasses import replace
    padded = {"EgN2"}
    return [replace(i, pad=True) if i.name in padded else i for i in bridgeward + town + eastgate]


# ------------------------------------------------------------------------------------------------
# The Iron Citadel
# ------------------------------------------------------------------------------------------------
# The seat of the realm, on the levelled crown of the spur above the city (region_spec_ember
# geography: the spur and its court). The ramp comes up from the Kingsway to a gate between two towers;
# the keep stands at the back of the court where every road in the realm can see it. The court is a
# flat landform at the spur's own height, so nothing here takes a pad: a pad is relative to the
# generated ground and would dig the court back down to it.

def citadel() -> list[P]:
    # The Iron Citadel: one fortress on the spur's levelled crown, its gate facing the capital, and a
    # forecourt between the gate and the ramp head where the garrison lives. The court's level core
    # runs x 63..149, z -201..-123 (region_spec_ember.geography), which is what everything here sits on.
    return [
        # The generated citadel: 56 x 56 m and 21 m to the keep, solid. Same node, same centre. The
        # old hull was 55 x 51 and solid too, so nothing was ever inside it; the forecourt (garrison
        # hall, well, banners, guards, the stockade) starts 4 m south of its foot and is untouched.
        P("iron_citadel", "Fortress", 110.0, -170.0, 0.0, landmark=True),
        P("longhouse_stone", "GarrisonHall", 124.0, -134.0, facing(124.0, -134.0, 100.0, -134.0)),
        P("well", "CourtWell", 104.0, -128.0, 0.0, scale=0.85),
        P("banner", "GateBannerW", 101.0, -139.0, 0.0),
        P("banner", "GateBannerE", 119.0, -139.0, 0.0),
        P("brazier", "RampBrazierW", 74.0, -138.0, 0.0),
        P("brazier", "RampBrazierE", 94.0, -134.0, 0.0),
    ]


# ------------------------------------------------------------------------------------------------
# The Ember Crown's country: landmarks, secondary places and micro-interest
# ------------------------------------------------------------------------------------------------
# Nothing here is pinned on the map. Each is placed where a road, a view or the lie of the land
# earns it, and each gives the player something to steer by or something to find.

def ember_country() -> list[P]:
    return [
        # The old Kingsway watchtower, ruined on the watch ridge: the first landmark north of the city,
        # seen from the north gate and from the Crossway.
        P("ruin_tower", "WatchRuinTower", -22.0, -314.0, 18.0, landmark=True, pad=True, pad_half=5.02, y=-0.6),
        P("wall_ruin", "WatchRuinWallA", -6.0, -304.0, 72.0, scale=0.6, y=-0.3),
        P("wall_ruin", "WatchRuinWallB", -40.0, -318.0, 160.0, scale=0.6),
        P("rock_rubble", "WatchRuinRubble", -20.0, -297.0, 40.0, scale=0.6),
        # The broken Kingsbridge: the old bridge's piers stand in the Emberwash valley beside the
        # ford the west road uses now. History without a word of text.
        P("monolith", "KingsbridgePierA", -176.0, -90.0, 0.0, scale=1.0, landmark=True, y=-0.4),
        P("monolith", "KingsbridgePierB", -169.0, -95.0, 12.0, scale=0.85, y=-0.4),
        P("wall_ruin", "KingsbridgeAbutment", -158.0, -105.0, 115.0, scale=0.8, y=-0.2),
        # The Crown Stones: a broken crescent of standing stones on the heath above the northern fields.
        # 11 to 18 m, where they were 5 to 7: the crescent is read from the fields below it.
        P("monolith", "CrownStoneA", 162.0, -318.0, 0.0, scale=1.2, landmark=True, y=-0.4),
        P("monolith", "CrownStoneB", 171.0, -324.0, 25.0, scale=1.0, y=-0.4),
        P("monolith", "CrownStoneC", 181.0, -326.0, 50.0, scale=1.5, landmark=True, y=-0.5),
        P("monolith", "CrownStoneD", 190.0, -321.0, 80.0, scale=0.9, y=-0.3),
        P("rock_boulder", "CrownStoneFallen", 198.0, -310.0, 95.0, scale=0.4),
        # The drowned towers of the old Hollowreach, standing in the open water west of the district.
        P("ruin_tower", "DrownedTowerA", -402.0, 340.0, 12.0, landmark=True, pad=True, pad_half=5.46, y=-1.0),
        P("ruin_tower", "DrownedTowerB", -386.0, 364.0, 60.0, scale=0.8, landmark=True, pad=True, pad_half=4.58, y=-0.8),
        # The Southmarch Gate at the end of the caravan road: a border gatehouse, broken. The two
        # wall stubs stand on the gap's floor outside each tower, short of the border hills' flanks.
        P("ruin_tower", "SouthmarchTowerW", 58.0, 412.0, 0.0, scale=0.8, landmark=True, pad=True, pad_half=5.24, y=-0.6),
        P("ruin_tower", "SouthmarchTowerE", 88.0, 414.0, 0.0, scale=0.7, pad=True, pad_half=4.8, y=-0.5),
        P("wall_ruin", "SouthmarchWallW", 45.0, 410.0, 90.0, scale=0.8, y=-0.2),
        P("wall_ruin", "SouthmarchWallE", 99.0, 410.0, 92.0, scale=0.7, y=-0.2),
        P("cart", "SouthmarchAbandonedCart", 84.0, 390.0, 125.0),
        # The Ashen Breach: the burnt end of the old eastern road.
        P("wall_ruin", "BreachWallA", 502.0, -268.0, 10.0, scale=0.9),
        P("monolith", "BreachPillar", 506.0, -294.0, 0.0, scale=1.4, landmark=True, y=-0.5),
        P("rock_rubble", "BreachRubble", 488.0, -294.0, 70.0, scale=0.5),
        # The Crown Pass: marker stones where the road tops the saddle.
        P("monolith", "PassMarkerW", -162.0, -646.0, 0.0, scale=1.2, landmark=True, y=-0.4),
        P("monolith", "PassMarkerE", -137.0, -643.0, 40.0, scale=1.0, y=-0.4),
        # The frontier road's burnt waggon and a dead camp, half way across the waste.
        P("cart", "FrontierBurntCart", 60.0, -500.0, 230.0),
        P("tent", "FrontierDeadCampTent", 76.0, -476.0, 30.0, scale=0.85),
        P("campfire", "FrontierDeadCampFire", 72.0, -470.0, 0.0, scale=0.6),
        # A farm on the knoll over the farm lane south of the market.
        P("farmhouse_long", "KnollFarm", 94.0, 128.0, -60.0, pad=True),
        P("clutter_pile", "KnollFarmHay", 102.0, 138.0, 15.0),
        # An abandoned farmstead on the West Downs, above the Hollowreach road.
        P("ruin_house", "DownsFarmRuin", -196.0, 160.0, 140.0, landmark=True, pad=True),
        P("wall_ruin", "DownsFarmWall", -184.0, 148.0, 30.0, scale=0.6),
        # A roadside stone where the wilds trail leaves the Kingsway.
        P("monolith", "WildsTrailStone", -110.0, -378.0, 0.0, scale=0.5, y=-0.2),
        P("brazier", "WildsTrailBrazier", -112.0, -370.0, 0.0),
    ]


# ------------------------------------------------------------------------------------------------
# Frostfang Reach: traces of a scattered clan culture
# ------------------------------------------------------------------------------------------------
# Low density on purpose: a cairn where a road turns, a shelter where a traveller needs one, a watch
# where a watch makes sense, and substantial empty high country between them.

def frostfang_traces() -> list[P]:
    return [
        # Stonewatch: the clan's waystation half way up the vale road.
        P("longhouse_stone", "StonewatchShelter", 86.0, -1484.0, facing(86.0, -1484.0, 62.0, -1512.0), pad=True),
        P("tent", "StonewatchTent", 98.0, -1510.0, 200.0, scale=0.85),
        P("campfire", "StonewatchFire", 80.0, -1506.0, 0.0, scale=0.6),
        P("rock_rubble", "StonewatchCairn", 28.0, -1496.0, 0.0, scale=0.75, tint=FROST),
        # Cairns where the roads turn.
        P("rock_rubble", "CairnValeSouth", -54.0, -1394.0, 30.0, scale=0.65, tint=FROST),
        P("rock_rubble", "CairnHoldPass", -42.0, -1782.0, 80.0, scale=0.65, tint=FROST),
        P("rock_rubble", "CairnRoostCol", -222.0, -1742.0, 10.0, scale=0.7, tint=FROST),
        P("rock_rubble", "CairnBurntCol", 190.0, -1768.0, 50.0, scale=0.7, tint=FROST),
        P("rock_rubble", "CairnRidgeRoute", -228.0, -2016.0, 0.0, scale=0.6, tint=FROST),
        # The abandoned hunting lodge on the fell west of the vale.
        # It stands on the fell's lower flank: higher up, the path to it was too steep to navigate.
        P("ruin_house", "HuntingLodgeRuin", -264.0, -1550.0, 60.0, landmark=True, pad=True),
        P("wall_ruin", "HuntingLodgeWall", -253.0, -1559.0, 150.0, scale=0.6, y=-0.1, tint=FROST),
        P("tent", "HuntingLodgeCollapsedTent", -274.0, -1534.0, 20.0, scale=0.85),
        # The Ravenspur watch, broken on the end of its spur.
        P("ruin_tower", "RavenspurWatch", 174.0, -1648.0, 25.0, scale=0.85, landmark=True, pad=True, pad_half=5.02, y=-0.6, tint=FROST),
        # Markers where the clan's country ends and the dragons' begins.
        P("monolith", "RoostMarkerA", -262.0, -1800.0, 0.0, scale=1.2, y=-0.4, tint=FROST),
        P("monolith", "RoostMarkerB", -248.0, -1820.0, 30.0, scale=0.9, y=-0.3, tint=FROST),
        P("monolith", "AshMarker", 246.5, -1769.0, 0.0, scale=1.2, y=-0.4, tint=SOOT),
        # An old camp under Glacier Pass, left when the ice moved.
        P("tent", "GlacierOldCampTent", 60.0, -1914.0, 120.0, scale=0.85),
        P("rock_rubble", "GlacierOldCampRing", 52.0, -1904.0, 0.0, scale=0.5, tint=FROST),
        # Stormcrown: broken stones at the foot of the spire, where the track gives out.
        P("monolith", "StormcrownStoneA", 326.0, -2156.0, 0.0, scale=1.5, landmark=True, y=-0.5, tint=SOOT),
        P("monolith", "StormcrownStoneB", 348.0, -2184.0, 40.0, scale=1.2, y=-0.4, tint=SOOT),
        P("rock_boulder", "StormcrownBoulder", 322.0, -2192.0, 70.0, scale=0.65, tint=SOOT),
    ]


# ------------------------------------------------------------------------------------------------
# The monumental layer (2026-10 world overhaul): a vast world and a small hero
# ------------------------------------------------------------------------------------------------
# The map is not wider and the ground is not higher: the terrain's relief is modest, so scale is carried
# by solid pieces 12 to 54 m tall, the dead gods and the dragons the lore turns on. Rules every row
# here keeps (tools/compose_district.py checks the first; the others are surveyed by hand):
#   * never on a road or its shoulder, a portal, a waystone landing, a spawn or a fight floor;
#   * never a pad: a giant stands on ground that is already level (a terrace, a plateau top, a road's
#     calmed margin) and is sunk a little (`y`) so its foot is buried, not levelled. A yard also
#     calms the generated relief for the realm's RouteCalm around itself, so a piece on a slope is
#     moved to level ground first. The Roost and Aerie bones are the two exceptions: no ground near
#     their roads is level along a skeleton's length, so each has the smallest pad that levels it;
#   * `landmark=True` (drawn to the Backdrop radius) only at about 20 m and up;
#   * a site the player can walk to has a map location (tools/gen_map_locations.py, same change).
# The model heights at scale 1: monolith 12, column 8, ruin tower 24, arch 20, colossus 36, colossus
# head 14, god hall fragment 28, dragon skeleton 10 (12.7 x 17.8 on the ground), outcrop 12.

def ember_monuments() -> list[P]:
    return [
        # The Crown Pass: two dead gods flank the road where it climbs to the saddle, 20 m off it on
        # the pass floor between the two ridge ends. Seen from the Crossway, 90 m south, and they
        # stand over the portal for the whole climb. A rubble fall at each one's foot keeps the
        # player a few metres off the stone.
        P("colossus", "CrownPassColossusW", -160.0, -628.0, -12.0, landmark=True, y=-1.0),
        P("colossus", "CrownPassColossusE", -130.5, -630.0, 12.0, scale=0.92, landmark=True, y=-1.0),
        P("rock_rubble", "CrownPassRubbleW", -166.0, -617.0, 30.0, scale=1.2),
        P("rock_rubble", "CrownPassRubbleE", -126.0, -619.0, 110.0, scale=1.1),
        # The Emberspire's foot: a dragon that came down on the heath below the massif, beside the
        # track that climbs to the arena.
        P("dragon_skeleton", "EmberspireBones", 367.0, -498.0, -22.0, scale=2.0, landmark=True, y=-1.2),
        # The southern fens: a god's head, half sunk, in country no road crosses.
        P("colossus_head", "FenColossusHead", -110.0, 345.0, -30.0, scale=1.2, y=-1.6),
        # The Southmarch Gate: the gate arch itself, between the two broken towers and 22 m behind
        # the portal the caravan road ends at.
        P("arch", "SouthmarchArch", 73.0, 425.0, 0.0, landmark=True, y=-0.8),
        # The King's Stones: the Kingsway's last half kilometre into the capital, a stone each side
        # by turns, 13 m off the centreline.
        P("monolith", "KingswayStone1", 28.2, -136.6, 20.0, scale=1.1, y=-0.4),
        P("monolith", "KingswayStone2", -8.4, -151.8, 75.0, scale=1.25, y=-0.4),
        P("monolith", "KingswayStone3", -5.4, -199.7, 140.0, scale=1.0, y=-0.3),
        P("monolith", "KingswayStone4", -46.4, -214.6, 200.0, scale=1.3, y=-0.4),
        P("monolith", "KingswayStone5", -43.9, -258.2, 260.0, scale=1.15, y=-0.4),
        # A watch tower on the frontier road, long fallen in: the only built thing between the
        # Crossway and the arena.
        P("ruin_tower", "FrontierRuinTower", 150.0, -476.0, 40.0, landmark=True, y=-0.6),
        # Tors: the cliff masses the uplands lacked.
        P("rock_outcrop", "HeathTor", 135.0, -430.0, 20.0, scale=1.7, landmark=True, y=-1.2),
        P("rock_outcrop", "UplandTor", 440.0, -422.0, -40.0, scale=1.6, landmark=True, y=-1.2),
        # A second stone at the Breach.
        P("monolith", "BreachStoneN", 491.0, -305.0, 60.0, scale=1.0, y=-0.4),
    ]


def frostfang_monuments() -> list[P]:
    return [
        # Hold Pass: the god that stands in the gap in the wall behind the Clan Hold, seen over the
        # wall from the hold's yard.
        P("colossus", "HoldPassColossus", -62.0, -1750.0, toward(-62.0, -1750.0, -40.0, -1700.0), landmark=True, y=-1.0, tint=FROST),
        P("rock_outcrop", "HoldPassPlinth", -78.0, -1757.0, 60.0, scale=0.8, y=-1.5, tint=FROST),
        # Dragon bones on the three roost approaches: what the roosts' owners left of the last ones.
        # The Burnt Col bones lie on the dry shelf west of the meltwater channel below the road.
        P("dragon_skeleton", "RoostBones", -292.0, -1796.0, 60.0, scale=1.6, y=-1.0, pad=True, pad_half=9.0),
        P("dragon_skeleton", "BurntColBones", 219.0, -1805.0, 63.0, scale=1.5, y=-1.0, tint=SOOT),
        P("dragon_skeleton", "AerieBones", -72.0, -2165.0, 90.0, scale=1.4, y=-0.9, pad=True, pad_half=9.0),
        # Stormcrown: the broken tower behind the duelling ground, under the spire.
        P("ruin_tower", "StormcrownSpire", 436.0, -2234.0, 70.0, scale=1.4, landmark=True, y=-1.0, tint=SOOT),
        # The vale road's stones, up to the hold.
        P("monolith", "ValeRoadStone1", 52.1, -1552.8, 15.0, scale=1.0, y=-0.3, tint=FROST),
        P("monolith", "ValeRoadStone2", 15.4, -1607.8, 100.0, scale=1.2, y=-0.4, tint=FROST),
        P("monolith", "ValeRoadStone3", -26.4, -1620.2, 190.0, scale=1.05, y=-0.3, tint=FROST),
    ]


def ashen_monuments() -> list[P]:
    return [
        # The Ash Plateau: a god on the flat top of the plateau north of the Hunters' station.
        P("colossus", "AshPlateauColossus", 1122.0, -568.0, toward(1122.0, -568.0, 1180.0, -430.0), scale=0.9, landmark=True, y=-0.8, tint=SOOT),
        # The Crater Field: the bones along the foot of the crater's western slope (laid across it,
        # their tail stood 9 m inside the hillside), the head on its east rim.
        P("dragon_skeleton", "CraterBones", 1237.0, -418.0, 20.0, scale=1.8, y=-1.1, tint=SOOT),
        P("colossus_head", "CraterHead", 1352.0, -396.0, 200.0, y=-1.2, tint=SOOT),
        # The scar road's old gate, north of the road where it comes in from the Breach.
        P("arch", "ScarArch", 964.0, -331.0, 80.0, landmark=True, y=-0.8, tint=SOOT),
        # Tusk arches on the last stretch of the plateau track.
        P("bone_arch", "LairBoneArchN", 1408.0, -572.0, 90.0, scale=1.4, y=-0.3),
        P("bone_arch", "LairBoneArchS", 1408.0, -548.0, 90.0, scale=1.25, y=-0.3),
        # The east rim's crown.
        P("rock_outcrop", "EastRimTor", 1450.0, -300.0, 90.0, scale=1.3, landmark=True, y=-1.0, tint=SOOT),
    ]


def sunspire_monuments() -> list[P]:
    return [
        # The library road: a god each side, 24 m off it, 60 m short of the forecourt.
        P("colossus", "LibraryColossusN", -160.0, 1438.0, toward(-160.0, 1438.0, -150.0, 1392.0), scale=0.8, landmark=True, y=-0.8, tint=SAND),
        P("colossus", "LibraryColossusS", -196.0, 1404.0, toward(-196.0, 1404.0, -150.0, 1392.0), scale=0.8, landmark=True, y=-0.8, tint=SAND),
        # The sand sea: a god on the lone butte, 54 m over the desert, and a fallen head below it.
        P("colossus", "SandSeaColossus", -40.0, 1555.0, toward(-40.0, 1555.0, 10.0, 1290.0), landmark=True, y=-1.0, tint=SAND),
        P("colossus_head", "SandSeaHead", -128.0, 1540.0, 40.0, scale=1.3, y=-2.0, tint=SAND),
        # The salt basin's bones.
        P("dragon_skeleton", "SaltBasinBones", -70.0, 1165.0, -25.0, scale=2.0, landmark=True, y=-1.2),
        # The Caravan Gap: a stone each side of the gap, and the old gate arch a hundred metres on.
        P("monolith", "GapStoneW", 45.0, 925.0, 20.0, scale=1.3, y=-0.5, tint=SAND),
        P("monolith", "GapStoneE", 79.0, 929.0, -30.0, scale=1.35, y=-0.5, tint=SAND),
        P("arch", "CaravanArch", 78.0, 1042.0, 15.0, landmark=True, y=-0.8, tint=SAND),
        # The pilgrim track: stones by turns, to the Mission.
        P("monolith", "PilgrimStone1", -93.8, 1644.7, 10.0, scale=1.0, y=-0.3, tint=SAND),
        P("monolith", "PilgrimStone2", -59.6, 1636.8, 80.0, scale=1.2, y=-0.4, tint=SAND),
        P("monolith", "PilgrimStone3", -36.6, 1663.2, 150.0, scale=0.9, y=-0.3, tint=SAND),
        P("monolith", "PilgrimStone4", -2.5, 1655.2, 220.0, scale=1.3, y=-0.4, tint=SAND),
        P("monolith", "PilgrimStone5", 46.0, 1677.6, 290.0, scale=1.1, y=-0.4, tint=SAND),
        P("monolith", "PilgrimStone6", 78.3, 1655.2, 350.0, scale=1.25, y=-0.4, tint=SAND),
        # Mesa-country cliff masses.
        P("rock_outcrop", "RedFlatsTor", 56.0, 1550.0, 35.0, scale=1.6, landmark=True, y=-1.2, tint=SAND),
        P("rock_outcrop", "ScarpTor", 330.0, 1012.0, -20.0, scale=1.4, y=-1.0, tint=SAND),
    ]


def pale_monuments() -> list[P]:
    return [
        # The Processional Way: a god each side of the road, and a fallen hall east of it.
        P("colossus", "ProcessionalColossusE", -2028.0, 146.0, toward(-2028.0, 146.0, -2034.0, 190.0), scale=0.9, landmark=True, y=-0.9, tint=PALE),
        P("colossus", "ProcessionalColossusW", -2080.0, 134.0, toward(-2080.0, 134.0, -2034.0, 190.0), scale=0.9, landmark=True, y=-0.9, tint=PALE),
        P("godhall_fragment", "ProcessionalHall", -1992.0, 96.0, 20.0, scale=0.8, landmark=True, y=-0.8, tint=PALE),
        # The east canal: the arch the city's east road ends under.
        P("arch", "CanalArch", -1846.0, 338.0, -40.0, landmark=True, y=-0.8, tint=PALE),
    ]


def celestial_monuments() -> list[P]:
    # The realm has 6 m of relief, so its props carry all of its scale. Each piece stands on the
    # level core of one of the broken terraces the spec raises in the empty cells.
    return [
        P("godhall_fragment", "GodfallHall", 3000.0, -2232.0, 12.0, landmark=True, y=-0.8, tint=ASHLIGHT),
        P("godhall_fragment", "ChoirHall", 2600.0, -2030.0, -20.0, scale=0.75, landmark=True, y=-0.6, tint=ASHLIGHT),
        P("colossus", "RimColossus", 2600.0, -2200.0, toward(2600.0, -2200.0, 2776.0, -2262.0), landmark=True, y=-1.0, tint=ASHLIGHT),
        P("colossus_head", "VoidEdgeHead", 3000.0, -1760.0, -60.0, y=-1.2, tint=ASHLIGHT),
        P("arch", "SunderedArch", 3010.0, -2010.0, 80.0, landmark=True, y=-0.8, tint=ASHLIGHT),
    ]


MONUMENTS: list[P] = (ember_monuments() + frostfang_monuments() + ashen_monuments() + sunspire_monuments()
                      + pale_monuments() + celestial_monuments())


# ------------------------------------------------------------------------------------------------
# Enclosure rings (2026-10): the three boss grounds whose walls were kit panels
# ------------------------------------------------------------------------------------------------
# A rampart segment is 14.7 m long, 5 m high and 8 m through at scale 1. A ring wants the length and
# the height but not the thickness, so each row carries `depth`: the segment is thinned front to back
# only, which leaves the masonry on its two long faces unstretched. The inner face stands on the line
# the old panels stood on, so the fight floor is the size it was; the invisible wall bodies that
# bound each floor (WallN/S/E/W in the cell scenes) are untouched and remain the real boundary.
# Neighbours overlap by 5 to 15% and alternate between facing in and facing out, so no two joints
# show the same end; every corner is capped by a monolith.

RAMPART_LENGTH, RAMPART_THROUGH = 14.74, 8.01


def rampart_run(prefix: str, a: tuple[float, float], b: tuple[float, float], outward: tuple[float, float],
                count: int, scale: float, depth: float, tint=None, first: int = 0) -> list[P]:
    """`count` segments covering the wall line a->b (world), thinned to `depth`, their inner face on the
    line and their body on the `outward` side of it."""
    ax, az = a
    bx, bz = b
    length = math.hypot(bx - ax, bz - az)
    ux, uz = (bx - ax) / length, (bz - az) / length
    half = RAMPART_THROUGH * scale * depth * 0.5
    seg = RAMPART_LENGTH * scale
    # Centres spread so the run covers the line end to end and shares the overlap between joints.
    span = max(0.0, length - seg)
    out = []
    for i in range(count):
        along = seg * 0.5 + (span * i / (count - 1) if count > 1 else span * 0.5)
        x, z = ax + ux * along + outward[0] * half, az + uz * along + outward[1] * half
        yaw = math.degrees(math.atan2(uz, ux)) + (180.0 if (first + i) % 2 else 0.0)   # local X along the run
        out.append(P("wall_rampart", f"{prefix}{first + i}", round(x, 2), round(z, 2), round(yaw, 1),
                     scale=scale + (0.03 if (first + i) % 2 else 0.0), depth=depth, y=-0.5, tint=tint))
    return out


def arena_ring() -> list[P]:
    """The Iron King's ring (ember_crown.arena, cell centre 345, -555). The old ring line is the square
    x 347.4..382.6, z -582.6..-547.4: a gate in the south side, a breach in the north."""
    w, e, n, s = 347.4, 382.6, -582.6, -547.4
    cap = dict(scale=0.75, y=-0.4)
    return (
        rampart_run("ArenaRampartW", (w, n), (w, s), (-1.0, 0.0), 2, 1.25, 0.35)
        + rampart_run("ArenaRampartE", (e, n), (e, s), (1.0, 0.0), 2, 1.25, 0.35, first=1)
        # North: one run from the west corner to the breach (x 368.6..376.4), a ruined stub beyond it.
        + rampart_run("ArenaRampartN", (w, n), (368.6, n), (0.0, -1.0), 1, 1.4, 0.33)
        + [P("wall_ruin", "ArenaBreachStub", 380.3, n - 1.9, 0.0, scale=0.75)]
        # South: lower walls each side of the gate (x 361.4..368.6), under the two gate stones. Thinner
        # than the other sides: the road from the south-west meets the gate on a diagonal, and at 0.4
        # the west wall's outer corner stood 10 cm off its centre line (a player capsule is 40 cm).
        + rampart_run("ArenaRampartSW", (w - 1.6, s), (360.2, s), (0.0, 1.0), 1, 1.0, 0.3)
        + rampart_run("ArenaRampartSE", (369.8, s), (e + 1.6, s), (0.0, 1.0), 1, 1.0, 0.3, first=1)
        + [P("monolith", "ArenaCapNW", w - 1.2, n - 1.2, 15.0, **cap), P("monolith", "ArenaCapNE", e + 1.2, n - 1.2, 105.0, **cap),
           P("monolith", "ArenaCapSE", e + 1.2, s + 1.2, 195.0, **cap), P("monolith", "ArenaCapSW", w - 1.2, s + 1.2, 285.0, **cap)]
    )


def mission_ring() -> list[P]:
    """The Crimson Mission's sanctum (sunspire.temple, cell centre 145, 1700). Ring line x 176..204,
    z 1666..1694; the gate is in the north side (x 182.8..197.2) and the sanctum door hangs on the
    north-west stub, so the two stubs are thin enough to leave the door proud of the wall."""
    w, e, n, s = 176.0, 204.0, 1666.0, 1694.0
    cap = dict(scale=0.75, y=-0.4, tint=SAND)
    return (
        rampart_run("MissionRampartS", (w - 2.0, s), (e + 2.0, s), (0.0, 1.0), 2, 1.15, 0.35, tint=SAND)
        + rampart_run("MissionRampartW", (w, n - 0.5), (w, s + 0.5), (-1.0, 0.0), 2, 1.05, 0.38, tint=SAND, first=1)
        + rampart_run("MissionRampartE", (e, n - 0.5), (e, s + 0.5), (1.0, 0.0), 2, 1.05, 0.38, tint=SAND)
        + [P("wall_rampart", "MissionRampartNW", 176.9, n - 0.45, 0.0, scale=0.8, depth=0.15, y=-0.3, tint=SAND),
           P("wall_rampart", "MissionRampartNE", 203.1, n - 0.45, 180.0, scale=0.8, depth=0.15, y=-0.3, tint=SAND),
           P("monolith", "MissionCapSW", w - 1.2, s + 1.2, 20.0, **cap), P("monolith", "MissionCapSE", e + 1.2, s + 1.2, 110.0, **cap)]
    )


def knight_ring() -> list[P]:
    """The Knight's Gate (celestial.knight_gate): ten ruined wall lengths on a 21 m ring about the gate
    floor (2842, -2032), open at the south and north where the assault road passes, and with a
    walkable gap between each pair as the kit panels had. Each stands with its return wall outward."""
    cx, cz, radius = 2842.0, -2032.0, 22.3
    out = []
    for angle in (0, 30, 60, 120, 150, 180, 210, 240, 300, 330):
        a = math.radians(angle)
        yaw = math.degrees(math.atan2(math.cos(a), -math.sin(a)))   # +Z (the wall face) inward, the return outward
        out.append(P("wall_ruin", f"KnightRingWall{angle}", round(cx + radius * math.cos(a), 2),
                     round(cz + radius * math.sin(a), 2), round(yaw, 1), scale=0.8, tint=ASHLIGHT))
    return out


RINGS: list[P] = arena_ring() + mission_ring() + knight_ring()


# Every placed structure in the realm, in world space. compose_district.py files each one into the
# cell that contains it, so a street can run across a cell edge without anyone deciding which cell
# owns it.
ITEMS: list[P] = capital() + citadel() + ember_country() + frostfang_traces() + RINGS + MONUMENTS
