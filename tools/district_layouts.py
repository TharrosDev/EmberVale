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


# Terraces: the levelled strip a frontage stands on, as (a, b, half_width) in world metres. The region
# spec turns each into a flat Ridge landform (level ground at the country's own height, following the
# street) — a rotated ground area in effect, which a Yard cannot be.
TERRACES: list[tuple[tuple[float, float], tuple[float, float], float]] = []


def facing(x: float, z: float, tx: float, tz: float) -> float:
    """Yaw that turns a building at (x, z) so its door faces the point (tx, tz)."""
    return math.degrees(math.atan2(tx - x, tz - z))


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
            half = max(w, d) * 0.5 * item.scale + 1.5
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
        P("watch_tower", "WestGateN", -109.8, -74.2, 70.0, landmark=True, pad=True),
        P("watch_tower", "WestGateS", -115.4, -59.2, 70.0, pad=True),
        P("well", "BridgewardWell", -86.0, -40.0),
        P("cart", "BridgewardCart", -79.0, -44.5, 110.0),
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
        P("watch_tower", "NorthGateW", 19.0, -94.0, 0.0, landmark=True, pad=True),
        P("watch_tower", "NorthGateE", 41.0, -97.0, 0.0, pad=True),
    ]
    eastgate = []
    eastgate += frontage("EgN", EAST_ROAD[0], EAST_ROAD[1], [14, 30, 48, 66], +1, 9.5,
                         ["workshop_open", "townhouse", "workshop_open", "farmhouse_long"], [0.0, 1.5, -1.0])
    eastgate += frontage("EgS", EAST_ROAD[0], EAST_ROAD[1], [8, 24, 40, 60], -1, 9.0,
                         ["house_b", "workshop_open", "cottage_modular", "longhouse_stone"], [0.8, -0.6])
    eastgate += [
        P("watch_tower", "EastGateN", 150.0, -66.0, 0.0, landmark=True, pad=True),
        P("watch_tower", "EastGateS", 160.0, -46.0, 0.0, pad=True),
        P("timber_stack", "TimberYardA", 176.0, -44.0, 80.0),
        P("timber_stack", "TimberYardB", 181.0, -38.0, 95.0),
        P("cart", "TimberCart", 171.0, -36.0, 20.0),
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
        P("castle_fortress", "Fortress", 110.0, -170.0, 0.0, landmark=True),
        P("longhouse_stone", "GarrisonHall", 124.0, -134.0, facing(124.0, -134.0, 100.0, -134.0)),
        P("well", "CourtWell", 104.0, -128.0, 0.0),
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
        P("ruin_tower", "WatchRuinTower", -22.0, -314.0, 18.0, scale=1.6, landmark=True, pad=True),
        P("ruin_wall", "WatchRuinWallA", -12.0, -305.0, 72.0),
        P("ruin_wall", "WatchRuinWallB", -33.0, -320.0, 160.0),
        P("rock_cluster", "WatchRuinRubble", -16.0, -298.0, 40.0),
        # The broken Kingsbridge: the old bridge's piers stand in the Emberwash valley beside the
        # ford the west road uses now. History without a word of text.
        P("ruin_pillar", "KingsbridgePierA", -176.0, -90.0, 0.0, scale=1.8, landmark=True),
        P("ruin_pillar", "KingsbridgePierB", -169.0, -95.0, 12.0, scale=1.6),
        P("ruin_wall", "KingsbridgeAbutment", -160.0, -99.0, 115.0, scale=1.3),
        # The Crown Stones: a broken crescent of standing stones on the heath above the northern fields.
        P("ruin_pillar", "CrownStoneA", 162.0, -318.0, 0.0, scale=2.2, landmark=True),
        P("ruin_pillar", "CrownStoneB", 171.0, -324.0, 25.0, scale=1.9),
        P("ruin_pillar", "CrownStoneC", 181.0, -326.0, 50.0, scale=2.4),
        P("ruin_pillar", "CrownStoneD", 190.0, -321.0, 80.0, scale=1.7),
        P("boulder", "CrownStoneFallen", 197.0, -311.0, 95.0, scale=0.6),
        # The drowned towers of the old Hollowreach, standing in the open water west of the district.
        P("ruin_tower", "DrownedTowerA", -402.0, 340.0, 12.0, scale=1.8, landmark=True, pad=True),
        P("ruin_tower", "DrownedTowerB", -386.0, 364.0, 60.0, scale=1.4, pad=True),
        # The Southmarch Gate at the end of the caravan road: a border gatehouse, broken.
        P("ruin_tower", "SouthmarchTowerW", 58.0, 412.0, 0.0, scale=1.7, landmark=True, pad=True),
        P("ruin_tower", "SouthmarchTowerE", 88.0, 414.0, 0.0, scale=1.5, pad=True),
        P("ruin_wall", "SouthmarchWallW", 44.0, 418.0, 90.0, scale=1.4),
        P("ruin_wall", "SouthmarchWallE", 102.0, 419.0, 92.0, scale=1.2),
        P("cart", "SouthmarchAbandonedCart", 84.0, 390.0, 35.0),
        # The Ashen Breach: the burnt end of the old eastern road.
        P("ruin_wall", "BreachWallA", 502.0, -270.0, 10.0, scale=1.5),
        P("ruin_pillar", "BreachPillar", 506.0, -294.0, 0.0, scale=1.6, landmark=True),
        P("rock_cluster", "BreachRubble", 488.0, -292.0, 70.0),
        # The Crown Pass: marker stones where the road tops the saddle.
        P("ruin_pillar", "PassMarkerW", -162.0, -646.0, 0.0, scale=2.0, landmark=True),
        P("ruin_pillar", "PassMarkerE", -137.0, -643.0, 0.0, scale=1.8),
        # The frontier road's burnt waggon and a dead camp, half way across the waste.
        P("cart", "FrontierBurntCart", 60.0, -500.0, 140.0),
        P("tent", "FrontierDeadCampTent", 76.0, -476.0, 30.0),
        P("campfire", "FrontierDeadCampFire", 72.0, -470.0, 0.0),
        # A farm on the knoll over the farm lane south of the market.
        P("farmhouse_long", "KnollFarm", 94.0, 128.0, -60.0, pad=True),
        P("hay", "KnollFarmHay", 102.0, 138.0, 15.0),
        # An abandoned farmstead on the West Downs, above the Hollowreach road.
        P("ruin_house", "DownsFarmRuin", -196.0, 160.0, 140.0, landmark=True, pad=True),
        P("ruin_wall", "DownsFarmWall", -186.0, 150.0, 30.0),
        # A roadside stone where the wilds trail leaves the Kingsway.
        P("ruin_pillar", "WildsTrailStone", -109.0, -378.0, 0.0, scale=1.3),
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
        P("tent", "StonewatchTent", 98.0, -1510.0, 200.0),
        P("campfire", "StonewatchFire", 80.0, -1506.0, 0.0),
        P("rock_cluster", "StonewatchCairn", 28.0, -1496.0, 0.0, scale=1.6),
        # Cairns where the roads turn.
        P("rock_cluster", "CairnValeSouth", -54.0, -1394.0, 30.0, scale=1.4),
        P("rock_cluster", "CairnHoldPass", -42.0, -1782.0, 80.0, scale=1.4),
        P("rock_cluster", "CairnRoostCol", -222.0, -1742.0, 10.0, scale=1.5),
        P("rock_cluster", "CairnBurntCol", 190.0, -1768.0, 50.0, scale=1.5),
        P("rock_cluster", "CairnRidgeRoute", -228.0, -2016.0, 0.0, scale=1.3),
        # The abandoned hunting lodge on the fell west of the vale.
        # It stands on the fell's lower flank: higher up, the path to it was too steep to navigate.
        P("ruin_house", "HuntingLodgeRuin", -264.0, -1550.0, 60.0, landmark=True, pad=True),
        P("ruin_wall", "HuntingLodgeWall", -250.0, -1562.0, 150.0),
        P("tent", "HuntingLodgeCollapsedTent", -274.0, -1534.0, 20.0),
        # The Ravenspur watch, broken on the end of its spur.
        P("ruin_tower", "RavenspurWatch", 174.0, -1648.0, 25.0, scale=1.6, landmark=True, pad=True),
        # Markers where the clan's country ends and the dragons' begins.
        P("ruin_pillar", "RoostMarkerA", -262.0, -1800.0, 0.0, scale=1.5),
        P("ruin_pillar", "RoostMarkerB", -248.0, -1820.0, 30.0, scale=1.2),
        P("ruin_pillar", "AshMarker", 244.0, -1774.0, 0.0, scale=1.5),
        # An old camp under Glacier Pass, left when the ice moved.
        P("tent", "GlacierOldCampTent", 60.0, -1914.0, 120.0),
        P("rock_cluster", "GlacierOldCampRing", 52.0, -1904.0, 0.0),
        # Stormcrown: broken stones at the foot of the spire, where the track gives out.
        P("ruin_pillar", "StormcrownStoneA", 326.0, -2156.0, 0.0, scale=2.0, landmark=True),
        P("ruin_pillar", "StormcrownStoneB", 348.0, -2184.0, 40.0, scale=1.6),
        P("boulder", "StormcrownBoulder", 322.0, -2192.0, 70.0),
    ]


# Every placed structure in the realm, in world space. compose_district.py files each one into the
# cell that contains it, so a street can run across a cell edge without anyone deciding which cell
# owns it.
ITEMS: list[P] = capital() + citadel() + ember_country() + frostfang_traces()
