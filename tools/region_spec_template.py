#!/usr/bin/env python3
"""THE STARTER FOR A NEW EMBERVALE REGION. Copy this file; do not import it.

    cp tools/region_spec_template.py tools/region_spec_<yourregion>.py

This is a documented skeleton, not a region: four cells, one seam pair, one pond and no content. It
is NOT registered in tools/gen_regions.py, so it produces nothing and can never be mistaken for a
shipped realm — but it is a REAL spec, and running this file directly proves it by building the whole
resource and checking the lattice, the seams and the envelopes. A starter that has never been run is
a starter that stopped working three months ago and nobody found out.

WHY IT EXISTS
-------------
The Ember Crown and Frostfang Reach were each authored twice: once, and then again after the
2026-08-29 overhaul, because the first attempt learned things the second one needed. Everything both
of them learned is either enforced by a validator or written here. The failure mode this replaces is
copying region_spec_ember.py and editing coordinates — which drags eight hundred lines of the Ember
Crown's specific history along with it, and quietly inherits its cell sizes, its noise seed and its
road widths as though they were physics.

⚠️ THE ORDER BELOW IS THE WORKFLOW, AND IT IS NOT ARBITRARY. Every one of these steps constrains the
next: routes that follow terrain need the terrain first, POIs on a road need a reason for the road,
and a settlement pad on a hillside needs the hillside. Doing them in a different order means doing
some of them twice. docs/WORLD_AUTHORING.md is the long version.

    1. macro geography    bounds, mountains, valleys, water, the barriers that make places distant
    2. POI placement      why a settlement, a dungeon or a landmark is where it is
    3. route network      roads that follow the terrain rather than cutting through it
    4. negative space     the transitional cells that make the POIs feel apart
    5. detailed landforms the gameplay geography inside each cell
    6. biome + materials  data/biomes/, and only a new one if none of the ten fits
    7. POI scenes         scenes/regions/<region>/<cell>.tscn
    8. scatter + dressing the ecology profile, then authored props as DETAIL on top of it
    9. map integration    tools/gen_map_locations.py, in the same change that adds the place
   10. the QA suite       python tools/world_quality_check.py <region>
"""

from __future__ import annotations

from gen_regions import (Cell, Mound, Ridge, Route, Seam, Water, Yard, check_envelopes,
                         check_seams, check_tiling, emit, local)

# --------------------------------------------------------------------------------------------------
# 1. MACRO GEOGRAPHY — the lattice
# --------------------------------------------------------------------------------------------------
# ⚠️ EVERY REGION GETS ITS OWN X BAND AND THEY DO NOT OVERLAP. The Ember Crown is x -190..140 and
# Frostfang Reach is x 260..600; before that they shared coordinates, and "both regions cannot be
# resident at once" was a limitation the streamer had to carry. Pick a band past the last one.
#
# ROWS are bands in z that run the full width and are split into columns. Declaring the lattice this
# way is what makes a HOLE arithmetically impossible — and a hole in the ground is something the
# player falls through that is visible from no file. check_tiling() proves the bands tile EXTENT_X
# exactly before the generator writes a byte.
#
# SCALE. Aim for 90–120 m between neighbouring POI centres and 150–300 m to anything that should feel
# remote. Under about 60 m two locations share a property line and the realm reads as a corridor of
# rooms; over about 400 m with nothing between them the player is walking to fill a progress bar.
EXTENT_X = (0.0, 300.0)
ROWS = [(-200.0, -100.0), (-100.0, 0.0)]

HEADER = '''[gd_resource type="Resource" script_class="RegionResource" load_steps=13 format=3 uid="uid://REPLACE_ME"]

[ext_resource type="Script" path="res://src/World/RegionResource.cs" id="1_region"]
[ext_resource type="Script" path="res://src/World/RegionCellResource.cs" id="2_cell"]
[ext_resource type="Script" path="res://src/World/WorldEnvironmentProfileResource.cs" id="3_environment"]
[ext_resource type="Script" path="res://src/World/WorldCellPresentationResource.cs" id="4_presentation"]
[ext_resource type="Script" path="res://src/World/WorldPerformanceBudgetResource.cs" id="5_budget"]
[ext_resource type="Script" path="res://src/World/WorldPathSegmentResource.cs" id="6_path"]
[ext_resource type="Script" path="res://src/World/WorldGroundAreaResource.cs" id="7_area"]
[ext_resource type="Script" path="res://src/World/WorldLandformResource.cs" id="8_landform"]
[ext_resource type="Script" path="res://src/World/WorldBiomeScatterResource.cs" id="9_scatter"]
[ext_resource type="Script" path="res://src/World/BiomeScatterLayerResource.cs" id="10_layer"]
[ext_resource type="Script" path="res://src/World/BiomeScatterExclusionResource.cs" id="11_exclusion"]
[ext_resource type="Script" path="res://src/World/WorldWaterResource.cs" id="12_water"]

; ⚠️ GENERATED BY tools/gen_regions.py. Edit the spec, not this file.
; ⚠️ load_steps above is REWRITTEN by the generator when it splices in the biome resources; the
; number here only has to be present, not correct.
'''

# --------------------------------------------------------------------------------------------------
# 6. BIOME, MATERIALS AND ATMOSPHERE
# --------------------------------------------------------------------------------------------------
# The region's DEFAULT biome is the last argument to emit() at the bottom of this file; individual
# cells override it with biome="Name". Both name a file in data/biomes/. Ten profiles ship:
#
#   TemperateLowland  Pasture  Woodland  Wetland  BurnedHeath
#   Excavated         AshWaste Alpine    Snowfield Glacier
#
# ⚠️ REACH FOR AN EXISTING ONE FIRST, AND OVERRIDE IT ON THE CELL RATHER THAN FORKING IT. A biome is
# six material slots and a handful of placement rules; a region that needs a new one genuinely needs
# a ground identity none of the ten has (a desert, a salt flat, a fungal understorey). If you do add
# one, add it to data/biomes/ built from data/terrain_layers/, so the NEXT region can use it too.
# That is the whole point of the split: layers are substances, biomes are places.
#
# THE FOUR SurfaceColor/SecondaryColor/DetailColor/RoadColor fields below are the pre-biome fallback
# and are only read when Biome is null. Fill them in anyway — they cost nothing and they are what a
# region renders as if its biome fails to load.
#
# ⚠️ SunTint / SunEnergyScale / HazeColor / HazeScale ARE HOW A REGION LOOKS LIKE A DIFFERENT PLACE.
# Palette alone cannot do it: neutral-grey bedrock under a golden-hour sun IS warm tan sand. Frostfang
# reads alpine because its LIGHT is cold and its air is thick, not because its rock is bluer.
ENVIRONMENT = '''[sub_resource type="Resource" id="Environment_template"]
script = ExtResource("3_environment")
SurfaceColor = Color(0.24, 0.20, 0.15, 1)
SecondaryColor = Color(0.34, 0.29, 0.20, 1)
DetailColor = Color(0.12, 0.105, 0.09, 1)
RoadColor = Color(0.46, 0.395, 0.29, 1)
BackdropColor = Color(0.19, 0.17, 0.15, 1)
; Relief is METRES of countryside wobble. 1.4 is gentle farmland; 2.2 is upland.
Relief = 1.4
DetailScale = 2.75
; The backdrop is a picture frame of real terrain outside the lattice, built from this reach and
; height. BackdropCenter/Count are legacy fields the frame no longer reads.
BackdropRadius = 340.0
BackdropHeight = 80.0
TerrainSeed = 4100
SurfaceRoughness = 0.96
DetailRoughness = 0.88
RoadRoughness = 0.82
SlopeBlendStart = 0.24
SlopeBlendEnd = 0.62
HeightBlendStart = 7.0
HeightBlendEnd = 26.0
SunTint = Color(1.0, 0.97, 0.92, 1)
SunEnergyScale = 1.0
HazeColor = Color(0.79, 0.77, 0.73, 1)
HazeScale = 1.0

'''

# --------------------------------------------------------------------------------------------------
# PERFORMANCE BUDGET
# --------------------------------------------------------------------------------------------------
# ⚠️ READ MaxResidentScatterInstances AS A MEMORY LIMIT AND MaxDrawCalls AS A GPU LIMIT. Raising the
# first is nearly free — scatter is MultiMesh, so ten thousand instances is one draw. Raising the
# second is not, and the usual cause of needing to is authored Node3D props doing a job scatter
# should be doing. Start from the Ember Crown's numbers and tighten after a settled play capture;
# never take frame time from the screenshot harness, whose PNG writes deliberately block frames.
BUDGET = '''[sub_resource type="Resource" id="Budget_template"]
script = ExtResource("5_budget")
MaxAuthoredNodesPerCell = 700
MaxResidentAuthoredNodes = 4200
MaxResidentRuntimeNodes = 9000
MaxScatterInstancesPerCell = 2400
MaxResidentScatterInstances = 30000
MaxTerrainVerticesPerCell = 7000
MaxResidentTerrainVertices = 60000
MaxDrawCalls = 1800
MaxNodeCount = 14000
MaxStaticMemoryMb = 2048.0
MaxFrameMilliseconds = 25.0
ConsecutiveSamplesBeforeWarning = 5
BiomeCullDistance = 340.0
VisibilityUpdateInterval = 0.25
MaxConcurrentLoadRequests = 2
MaxCellInstantiationsPerFrame = 1

'''

# --------------------------------------------------------------------------------------------------
# 8. SCATTER — the region's ecology
# --------------------------------------------------------------------------------------------------
# ⚠️ Count IS A DENSITY: instances per 100 x 100 m, scaled by the cell's own footprint. A flat count
# makes a 200 m transitional cell four times emptier than the 60 m POI beside it, which draws the
# cell lattice back onto the ground in vegetation after the terrain has stopped drawing it.
#
# The four fields that separate a scattered layer from a generated one:
#   MaxSlope    the steepest ground this species stands on. Trees ~0.4, scrub ~0.6, loose stone ~1.0.
#               Without it, vegetation grows sideways out of every cliff in the region.
#   HeightRange the altitude band it survives in. This is how a tree line happens.
#   Clumping    how hard it gathers into stands. Even spacing is the most recognisable pattern there
#               is; an eye finds the regularity long before it finds a repeated model.
#   Saturation  under 1 drains the source model's colour. The only way to make one shared asset read
#               as two regions' worth of material — a tint multiplies, it cannot desaturate.
SCATTER = '''[sub_resource type="Resource" id="Layer_grass"]
script = ExtResource("10_layer")
ScenePath = "res://assets/models/props/prp_grass_tall.glb"
Count = 520
MinimumScale = 0.7
MaximumScale = 1.35
MinimumSpacing = 1.6
MaxSlope = 0.55
Clumping = 0.32
ClumpScale = 22.0
Tint = Color(0.82, 0.84, 0.66, 1)
TintVariation = 0.22
VisibilityRangeEnd = 62.0
VisibilityFadeMargin = 12.0

; A tree layer is an HLOD layer or it is a draw-call storm at distance. The proxy tier is the SAME
; mesh at a fraction of the density, faded in as the detailed tier fades out.
[sub_resource type="Resource" id="Layer_tree"]
script = ExtResource("10_layer")
ScenePath = "res://assets/models/props/prp_tree_broadleaf.glb"
Count = 220
MinimumScale = 0.75
MaximumScale = 1.6
MinimumSpacing = 6.5
MaxSlope = 0.42
Clumping = 0.78
ClumpScale = 46.0
Tint = Color(0.72, 0.78, 0.66, 1)
TintVariation = 0.18
VisibilityRangeEnd = 150.0
VisibilityFadeMargin = 22.0
CastShadows = true
HlodShape = 1
HlodReduction = 3
HlodRangeBegin = 130.0
HlodRangeEnd = 320.0
HlodColor = Color(0.88, 0.9, 0.92, 1)
HlodScale = Vector3(1.15, 1.15, 1.15)

[sub_resource type="Resource" id="Scatter_template"]
script = ExtResource("9_scatter")
Seed = 6101
EdgePadding = 1.0
Layers = Array[ExtResource("10_layer")]([SubResource("Layer_grass"), SubResource("Layer_tree")])

'''

RESOURCE = '''[resource]
script = ExtResource("1_region")
Id = "region.template"
DisplayName = "The Template"
Realm = 0
SpawnPoint = Vector3(60, 1.2, -50)
Cells = Array[ExtResource("2_cell")]([@CELLS@])
EnvironmentProfile = SubResource("Environment_template")
PerformanceBudget = SubResource("Budget_template")

; ⚠️ Bounds is the lattice PLUS a margin, and its Y range must actually contain the terrain — the
; deepest pit floor to the highest summit. It is not the same rectangle the cells tile.
Bounds = AABB(-20, -60, -220, 340, 180, 260)
SafeZoneCenter = Vector3(60, 0, -50)
SafeZoneRadius = 30.0
WeavePotency = 1.0
DefaultWeatherId = "weather.cloudy"
DayPhaseBias = 2
'''


def cells() -> list[Cell]:
    """Four cells: a POI, a transitional neighbour, and two fillers that make the row band tile.

    ⚠️ EVERY ROW BAND MUST BE FILLED EDGE TO EDGE. That is what check_tiling proves, and it is why a
    region always has more cells than it has places.
    """
    return [
        # ------------------------------------------------------------------ 2. A POI CELL
        Cell(
            key="hold", cell_id="template.hold",
            scene="res://scenes/regions/template/hold.tscn",
            center=(60.0, -50.0), size=(120.0, 100.0), resolution=56, seed=601,
            safe_radius=28.0, scatter="Scatter_template", biome="Pasture",
            note="""
            THE EXAMPLE HOLD — a settlement in the shelter of a ridge, on the one crossing of the
            valley. ⚠️ Say WHY IT IS HERE in this note, in a sentence a stranger could check. "It is
            at the crossing" and "it is behind the ridge" are reasons; "it is the starting area" is a
            role, not a reason, and a place with a role and no reason reads as a diagram.
            """,
            # 5. DETAILED LANDFORMS. Geography FIRST, then circulation, then the pads buildings
            # stand on. ⚠️ Landforms may — and at a seam SHOULD — overhang the cell envelope: a ridge
            # that stops dead at a boundary re-draws the rectangle the world-space field removes.
            landforms=(
                # The sheltering ridge. Falloff 0.55 over a 22 m half-width is about a 1.6 grade:
                # comfortably past CharacterBody3D's 45-degree floor limit, so it is an honest wall
                # with no collider on it at all.
                Ridge(a=(-60, -44), b=(60, -40), half=22, h=17.0, fall=0.55),
                # A soft swell that gives the approach something to crest, so the hold is a reveal.
                Mound(at=(-8, 26), ext=(38, 26), h=5.0, fall=0.95),
                # ⚠️ THE PAD IS FLATTENED (flat=0.9) AND THEREFORE GETS NO irr. The generator's rule:
                # natural geography is warped out of its ellipse, MADE ground is not, because a
                # market square with a wobbly edge reads as a mistake rather than as a place.
                Mound(at=(0, 0), ext=(30, 26), h=1.0, fall=0.5, flat=0.9),
            ),
            # 3. ROUTES. A road is a CUT: at full path mask its centreline is exactly the graded line
            # between its own endpoints, so its gradient is arithmetic you can predict. --validate
            # refuses anything over a 0.80 grade.
            routes=(Route((-46, 34), (-10, 6), 5.0, 2.5),
                    Route((-10, 6), (34, 2), 5.0, 2.5)),
            # 4. GROUND AREAS. Every building cluster wants one; Elevation is an ABSOLUTE world Y,
            # because a yard is a place props and colliders are built against.
            yards=(Yard(at=(0, 0), ext=(22, 18), feather=3.0, blend=0.85, elevation=1.0),),
            # WATER. Declaring it here is what puts it under WorldWater's non-swimming recovery
            # contract; a surface authored as a mesh in the .tscn is invisible to the system whose
            # job is keeping the player out of it. Draw it LARGER than the basin — the shoreline is
            # the terrain's own contour, and the mesh fades out where the ground rises through it.
            waters=(Water(at=(44, 30), ext=(24, 20), y=0.05, ident="MillPond", opaque=1.6),),
            new_scene="template_hold",
        ),

        # ------------------------------------------------------------------ 4. A TRANSITIONAL CELL
        Cell(
            key="march", cell_id="template.march",
            scene="res://scenes/regions/template/march.tscn",
            center=(210.0, -50.0), size=(180.0, 100.0), resolution=36, seed=602,
            scatter="Scatter_template", biome="Woodland",
            note="""
            THE EXAMPLE MARCH — a hundred and eighty metres of country with a road across it and no
            gameplay beat at all. ⚠️ THIS IS A FEATURE AND IT IS THE HARDEST ONE TO KEEP. A realm
            where every thirty metres has a purpose advertises on every step that it was designed.
            Resolution 36 over 180 m is a vertex every five metres, which is what an empty cell
            should cost; the POI next door is 56 over 120, a vertex every two.
            """,
            landforms=(
                Mound(at=(-50, 10), ext=(52, 30), h=9.0, fall=0.95),
                Mound(at=(40, -14), ext=(44, 28), h=-4.0, fall=0.9),
            ),
            new_scene="template_march",
        ),

        # ------------------------------------------------------------------ ROW FILLERS
        Cell(
            key="north_a", cell_id="template.north_a",
            scene="res://scenes/regions/template/north_a.tscn",
            center=(75.0, -150.0), size=(150.0, 100.0), resolution=32, seed=603,
            scatter="Scatter_template", biome="Woodland",
            note="Upper country. Fills the northern row band; see cells()'s docstring.",
            landforms=(Ridge(a=(-70, 20), b=(70, 8), half=24, h=22.0, fall=0.75),),
            new_scene="template_north_a",
        ),
        Cell(
            key="north_b", cell_id="template.north_b",
            scene="res://scenes/regions/template/north_b.tscn",
            center=(225.0, -150.0), size=(150.0, 100.0), resolution=32, seed=604,
            scatter="Scatter_template", biome="Woodland",
            note="Upper country, east half.",
            landforms=(Mound(at=(0, -20), ext=(60, 34), h=15.0, fall=0.9),),
            new_scene="template_north_b",
        ),
    ]


def seams() -> list[Seam]:
    """Road crossings, authored ONCE as a world point both cells derive their endpoint from.

    ⚠️ IT IS IMPOSSIBLE TO AUTHOR HALF A SEAM IN THIS FILE, AND THAT IS THE WHOLE REASON THE REGION
    RESOURCE IS GENERATED. Three shipped seam defects were all somebody doing this arithmetic in
    their head. check_seams() proves the point lies on both cells' shared edge before anything is
    written.
    """
    return [
        # The hold's east road meets the march's west road at world (120, -46).
        Seam(a="hold", b="march", at=(120.0, -46.0),
             reach_a=(34.0, 2.0), reach_b=(-60.0, 8.0)),
        # And the march climbs north into the upper country.
        Seam(a="march", b="north_b", at=(225.0, -100.0),
             reach_a=(30.0, 40.0), reach_b=(0.0, 34.0)),
    ]


def build_template(legacy: dict[str, str]) -> tuple[str, list[str]]:
    """The generator entry point. Register it in gen_regions.main() when the region is real.

    ⚠️ `legacy` is the previous revision's sub-resources, used only when MIGRATING an existing region
    whose interior circulation must survive verbatim. A new region passes it and ignores it.
    """
    spec = cells()
    link = seams()
    by_key = {c.key: c for c in spec}

    issues = check_tiling("Template", spec, ROWS, EXTENT_X)
    issues += check_seams("Template", by_key, link)

    routed = {c.key: list(c.routes) for c in spec}
    for seam in link:
        routed[seam.a].append(Route(seam.reach_a, local(by_key[seam.a], seam.at)))
        routed[seam.b].append(Route(local(by_key[seam.b], seam.at), seam.reach_b))
    issues += check_envelopes("Template", spec, routed)

    return emit("template", HEADER, spec, link, legacy,
                ENVIRONMENT, BUDGET, RESOURCE, SCATTER, "TemperateLowland"), issues


# --------------------------------------------------------------------------------------------------
# 10. THE QA SUITE — the definition of "the region works"
# --------------------------------------------------------------------------------------------------
#     python tools/world_quality_check.py <region>
#
# Fourteen gates in one command: generation, build, unit tests, content (references, route grades and
# off-route traps), the negative battery, seams, layout, map markers, step-up, the mesh census, a
# real capsule walking every route, and the screenshot regression.
#
# ⚠️ THE SCREENSHOT GATE IS THE ONE THAT NEEDS A HUMAN. It fails whenever the world changes, which is
# correct and is not the same as the world being wrong. LOOK AT tools/shots/world/ BEFORE approving a
# new baseline; a baseline regenerated without inspection is a gate that has been switched off.
if __name__ == "__main__":
    # ⚠️ SELF-CHECK, NOT A GENERATOR RUN. It builds the resource in memory and throws the text away.
    # The point is that the skeleton is EXERCISED — the four cells really do tile both row bands, the
    # two seams really are on the shared edges, every route point really is inside its envelope — so
    # this file cannot rot into an example that stopped working.
    _text, _issues = build_template({})
    for _issue in _issues:
        print(f"LATTICE ERROR: {_issue}")
    print("self-check: FAILED" if _issues else
          f"self-check: the template builds ({len(_text)} bytes) and its lattice is sound")
    print()
    print("region_spec_template.py is a starter to COPY, not a region to generate.")
    print("  cp tools/region_spec_template.py tools/region_spec_<yourregion>.py")
    print("then register build_<yourregion> in tools/gen_regions.py's main().")
    raise SystemExit(1 if _issues else 0)
