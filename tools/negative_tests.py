#!/usr/bin/env python3
"""Prove Phase 38's content rules by BREAKING them (Phase 38V).

    python tools/negative_tests.py                 # the whole battery
    python tools/negative_tests.py --only haggle   # one rule, while authoring
    python tools/negative_tests.py --list          # what is covered, without running anything

Every sub-phase in the economy arc claimed its new validator rules were "negative-tested both ways".
Each claim was true when it was written and none of them was ever re-checked. That is the problem
this file exists for: `ValidateShopTrade`'s permitted spread tightened twice AFTER its original
proof — once when 38S folded the haggle into both sides (about 0.52 to 0.42), once when 38T asked it
at the shocked extremes — so the run that proved it was proving a rule the repo no longer has.
**A proof that lives in a retrospective decays silently as the data moves under it.**

So: each entry below mutates authored data, runs `--validate`, and asserts BOTH that the run fails
AND that the expected refusal is the one that fired. The exit code alone is not evidence — it proves
that *some* rule tripped, and a mutation that trips the wrong rule looks identical to one that works.

⚠️ THIS EDITS `data/` AND `scenes/` IN PLACE. Before the first mutation it snapshots the exact bytes
of every possible target, including uncommitted authoring, and every case restores from that
snapshot in a `finally`. It also compares the final git state with the initial state. Never replace
this with `git checkout --`: that would silently destroy the level work this battery is meant to
protect while validating it.

⚠️ COVERAGE IS ONE MUTATION PER VALIDATOR FUNCTION, not one per refusal. Phase 38 has roughly 110
`issues.Add` sites; this proves that each rule FIRES and RECOVERS, which is the gate's question.
`ValidateServiceKind` is the exception and gets one per service kind — its 22 refusals span eight
kinds, and a single mutation there would report far more coverage than it earned. The remainder is
named in `docs/playbook/phase-38.md` rather than left implied.
"""

import argparse
import os
import subprocess
import sys
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent

# ⚠️ `godot` is not on PATH (CLAUDE.md §2) and the plain .exe detaches without printing, so the
# console variant is the only one whose output can be read back. Override with GODOT= for a
# different install rather than editing this.
GODOT = os.environ.get(
    "GODOT",
    r"C:\Users\magnu\Downloads\Godot_v4.7.1-stable_mono_win64"
    r"\Godot_v4.7.1-stable_mono_win64\Godot_v4.7.1-stable_mono_win64_console.exe",
)

# (name, rule, [(file, find, replace), ...], expected substring of the refusal)
#
# The `find` text must be unique within its file — a mutation that lands twice is a different
# experiment from the one described here. Expected substrings deliberately avoid interpolated
# numbers where the prose alone identifies the rule.
CASES = [
    # ---- ValidateShops and the per-shop rules it calls -------------------------------------
    ("shop.markup_below_one", "ValidateShops",
     [("data/shops/EmberCrownGoods.tres", "BuyMarkup = 1.5", "BuyMarkup = 0.5")],
     "has a buy markup below 1"),

    ("shop.sell_beats_buy", "ValidateShops",
     [("data/shops/EmberCrownGoods.tres", "SellFraction = 0.4", "SellFraction = 2.0")],
     "pays at least as much as it charges"),

    # ⚠️ Anchored on the newlines: `RestockDays = 1` also appears in this file's header comment,
    # and a mutation that lands twice is a different experiment from the one described here. The
    # uniqueness check caught it on the first run rather than reporting a false pass.
    ("shop.negative_restock", "ValidateShopRestock",
     [("data/shops/EmberCrownGoods.tres", "\nRestockDays = 1\n", "\nRestockDays = -1\n")],
     "has a negative restock interval"),

    ("shop.hour_out_of_range", "ValidateShopHours",
     [("data/shops/EmberCrownGoods.tres", "OpenHour = 6", "OpenHour = 30")],
     "wrapped by the arithmetic"),

    ("shop.haggle_not_a_percentage", "ValidateShopHaggle",
     [("data/shops/EmberCrownGoods.tres", "HaggleChance = 50", "HaggleChance = 150")],
     "has a haggle chance of"),

    ("shop.haggle_costs_nothing", "ValidateShopHaggle",
     [("data/shops/EmberCrownGoods.tres", "HaggleDelta = -1", "HaggleDelta = 0")],
     "negotiation must cost something"),

    ("shop.free_investment_rung", "ValidateShopInvestment",
     [("data/shops/EmberCrownGoods.tres", "Cost = 1000", "Cost = 0")],
     "a free stake is not a sink"),

    ("shop.unknown_cell", "ValidateShopCell",
     [("data/shops/EmberdeepQuartermaster.tres",
       'CellId = "ember_crown.emberdeep_mine"', 'CellId = "ember_crown.nowhere"')],
     "stands in unknown cell"),

    ("shop.unknown_accepted_tag", "ValidateShopTrade",
     [("data/shops/EmberdeepFactor.tres",
       'AcceptedTags = Array[String](["ore", "metal"])',
       'AcceptedTags = Array[String](["ore", "metal", "bogus"])')],
     "accepts unknown trade tag"),

    ("shop.unknown_specialty_tag", "ValidateShopTrade",
     [("data/shops/EmberdeepFactor.tres",
       'Specialties = Array[String](["ore"])', 'Specialties = Array[String](["bogus"])')],
     "specialises in unknown trade tag"),

    # ⚠️ THE ONE THAT MOVED. 38F authored this band, 38S tightened it and 38T re-asked it at the
    # shocked extremes. Raising the mine's sell fraction is the cheapest way to walk into it.
    ("shop.spread_too_thin", "ValidateShopTrade",
     [("data/shops/EmberdeepQuartermaster.tres", "SellFraction = 0.62", "SellFraction = 0.78")],
     "too thin a spread"),

    ("shop.consign_never_matures", "ValidateShopConsignment",
     [("data/shops/EmbermarketConsignment.tres", "ConsignDays = 3", "ConsignDays = 0")],
     "never matures"),

    ("shop.consign_commission_out_of_range", "ValidateShopConsignment",
     [("data/shops/EmbermarketConsignment.tres",
       "ConsignCommission = 0.18", "ConsignCommission = 1.5")],
     "takes a commission of"),

    ("shop.fence_moves_nothing", "ValidateShopContraband",
     [("data/shops/HollowreachHull.tres", "ContrabandDelta = 5", "ContrabandDelta = 0")],
     "but moves it by 0"),

    # ⚠️ Two files, because the rule is about the REALM rather than a shop: one fence still taking
    # contraband is enough to keep it quiet, which is exactly the point of the rule.
    ("realm.contraband_unsellable", "ValidateContrabandReachability",
     [("data/shops/HollowreachHull.tres",
       'AcceptedTags = Array[String](["contraband", "luxury", "gem", "jewelry"])',
       'AcceptedTags = Array[String](["luxury", "gem", "jewelry"])'),
      ("data/shops/HollowreachLocker.tres",
       'AcceptedTags = Array[String](["contraband", "pelt", "textile", "fuel", "metal"])',
       'AcceptedTags = Array[String](["pelt", "textile", "fuel", "metal"])')],
     "it can never be sold to anyone"),

    # ⚠️ THE APOTHECARY DOES NOT WORK HERE AND THAT IS THE RULE BEING PRECISE, NOT LOOSE. Sending
    # Mirela on the road proves nothing: every consumable she stocks is also on a resident shelf, so
    # the realm still has supplies. The rule is per ITEM, not per shop — it needs a consumable with
    # exactly one seller, and `item.food.bread` at the Provisions stall is the realm's only one.
    # The first draft used the apothecary and reported NOT CAUGHT, which is the substring assertion
    # doing its job: on the exit code alone this would have looked like a rule that does not fire.
    ("realm.consumables_behind_a_traveller", "ValidateEssentialsAreResident",
     [("data/shops/EmbermarketProvisions.tres",
       "\nCloseHour = 19\n", "\nCloseHour = 19\nVisitEveryDays = 3\n")],
     "cannot wait out a merchant who is not in town"),

    # ---- The settlements ---------------------------------------------------------------------
    ("cell.unknown_surplus_tag", "ValidateCellTrade",
     [("data/regions/EmberCrown.tres",
       'Surplus = Array[String](["ore", "metal", "fuel"])',
       'Surplus = Array[String](["ore", "metal", "fuel", "bogus"])')],
     "surplus in unknown trade tag"),

    ("cell.awash_and_short_at_once", "ValidateCellTrade",
     [("data/regions/EmberCrown.tres",
       'Demand = Array[String](["food", "fish", "textile"])',
       'Demand = Array[String](["food", "fish", "textile", "ore"])')],
     "is both awash in and short of"),

    ("cell.unknown_shock_tag", "ValidateCellShocks",
     [("data/regions/EmberCrown.tres",
       'ShockTags = Array[String](["ore", "food"])',
       'ShockTags = Array[String](["bogus", "food"])')],
     "can be shocked in unknown trade tag"),

    ("cell.duplicate_shock_candidate", "ValidateCellShocks",
     [("data/regions/EmberCrown.tres",
       'ShockTags = Array[String](["ore", "food"])',
       'ShockTags = Array[String](["ore", "ore"])')],
     "twice as a shock candidate"),

    ("region.toll_flag_nobody_sells", "ValidateTolls",
     [("data/regions/EmberCrown.tres",
       'TollPermitFlagId = "flag.crossway.permit"', 'TollPermitFlagId = "flag.crossway.nope"')],
     "granted by no passage service"),

    # ---- World infrastructure budgets -------------------------------------------------------
    ("world.loading_budget_zero", "ValidateRegions",
     [("data/regions/EmberCrown.tres",
       "MaxConcurrentLoadRequests = 2", "MaxConcurrentLoadRequests = 0")],
     "invalid visibility or staged-loading budget"),

    ("world.terrain_vertex_budget_exceeded", "ValidateRegions",
     [("data/regions/EmberCrown.tres",
       "MaxTerrainVerticesPerCell = 7000", "MaxTerrainVerticesPerCell = 2000")],
     "terrain vertices, over its per-cell budget"),

    ("world.terrain_blend_inverted", "ValidateRegions",
     [("data/regions/EmberCrown.tres", "SlopeBlendEnd = 0.62", "SlopeBlendEnd = 0.04")],
     "invalid terrain material-blending thresholds"),

    ("world.scatter_source_missing", "ValidateRegions",
     [("data/regions/FrostfangReach.tres",
       # ⚠️ prp_pine_dead is in TWO layers since the 2026-08-29 overhaul (the roosts' and the
       # bare high-altitude one) and apply_case refuses an ambiguous find. The snow tuft is unique.
       'ScenePath = "res://assets/models/props/prp_grass_short.glb"',
       'ScenePath = "res://assets/models/props/prp_missing.glb"')],
     "scatter source 'res://assets/models/props/prp_missing.glb' does not exist"),

    ("world.hlod_reduction_invalid", "ValidateRegions",
     [("data/regions/FrostfangReach.tres", "HlodReduction = 4", "HlodReduction = 1")],
     "invalid HLOD scatter tier"),

    # ---- Geography (the 2026-08-29 overhaul) ----------------------------------------------------
    # ⚠️ These two mutate a GENERATED file. data/regions/*.tres comes out of tools/gen_regions.py,
    # so a real fix goes in tools/region_spec_<region>.py — the battery only needs the .tres to be
    # briefly wrong, and it restores from its own byte snapshot either way.
    #
    # A road nobody can climb is emergent between the landform file and the route file and invisible
    # in both, which is the entire reason ValidateRouteGrades exists: this raises the rise north out
    # of the town square from three metres to sixty, under the Kingsway.
    ("world.route_grade_unwalkable", "ValidateRegions",
     [("data/regions/EmberCrown.tres",
       """[sub_resource type="Resource" id="Land_town_hub_4"]
script = ExtResource("8_landform")
Shape = 0
Center = Vector2(-6, -46)
Extent = Vector2(32, 17)
Height = 3.0
Falloff = 0.9""",
       """[sub_resource type="Resource" id="Land_town_hub_4"]
script = ExtResource("8_landform")
Shape = 0
Center = Vector2(-6, -46)
Extent = Vector2(32, 17)
Height = 60.0
Falloff = 0.9""")],
     "a walking player can hold"),

    # A landform whose falloff is outside (0, 1] produces a mask that is either a step or nothing,
    # and neither reads as a defect from the .tres.
    ("world.landform_falloff_invalid", "ValidateRegions",
     [("data/regions/EmberCrown.tres",
       """[sub_resource type="Resource" id="Land_town_hub_4"]
script = ExtResource("8_landform")
Shape = 0
Center = Vector2(-6, -46)
Extent = Vector2(32, 17)
Height = 3.0
Falloff = 0.9""",
       """[sub_resource type="Resource" id="Land_town_hub_4"]
script = ExtResource("8_landform")
Shape = 0
Center = Vector2(-6, -46)
Extent = Vector2(32, 17)
Height = 3.0
Falloff = 1.8""")],
     "invalid authored landform"),

    # ---- The contract board ------------------------------------------------------------------
    ("contract.nothing_to_deliver", "ValidateContracts",
     [("data/contracts/Charcoal.tres", "Quantity = 25", "Quantity = 0")],
     "nothing to deliver"),

    ("contract.pays_nothing", "ValidateContracts",
     [("data/contracts/Charcoal.tres", "RewardGold = 110", "RewardGold = 0")],
     "a posting nobody is paid for"),

    ("board.more_slots_than_postings", "ValidateBoardsHaveEnoughPostings",
     [("data/services/CrosswayContracts.tres", "BoardSlots = 3", "BoardSlots = 99")],
     "show the same posting twice"),

    # ---- Services: the shared rules, then one per kind ----------------------------------------
    ("service.negative_price", "ValidateServices",
     [("data/services/HollowreachWager.tres", "PriceGold = 50", "PriceGold = -5")],
     "has a negative price"),

    ("kind.trainer_unknown_recipe", "ValidateServiceKind/Trainer",
     [("data/services/EmberCrownTrainer.tres",
       'TaughtRecipeIds = Array[String](["recipe.drakescale_mail"])',
       'TaughtRecipeIds = Array[String](["recipe.nope"])')],
     "teaches unknown recipe"),

    ("kind.inn_with_a_flag", "ValidateServiceKind/Inn",
     [("data/services/EmberCrownInn.tres",
       'UnlockFlagId = ""', 'UnlockFlagId = "flag.inn.bought"')],
     "is an inn with an unlock flag"),

    ("kind.search_is_sold", "ValidateServiceKind/Search",
     [("data/services/CrosswaySearch.tres", "PriceGold = 0", "PriceGold = 10")],
     "a search is not sold"),

    ("kind.redeem_is_free", "ValidateServiceKind/Redeem",
     [("data/services/CrosswayImpound.tres", "PriceGold = 12", "PriceGold = 0")],
     "redeems impounded goods for nothing"),

    ("kind.collect_charges_a_fee", "ValidateServiceKind/Collect",
     [("data/services/EmbermarketCollect.tres", "PriceGold = 0", "PriceGold = 10")],
     "to collect consignment"),

    ("kind.appraisal_charges_a_fee", "ValidateServiceKind/Appraise",
     [("data/services/EmbermarketAppraisal.tres", "PriceGold = 0", "PriceGold = 10")],
     "gold to appraise"),

    ("kind.passage_grants_no_flag", "ValidateServiceKind/Passage",
     [("data/services/CrosswayPermit.tres",
       'UnlockFlagId = "flag.crossway.permit"', 'UnlockFlagId = ""')],
     "sells passage but grants no flag"),

    ("kind.free_mercenary", "ValidateServiceKind/Mercenary",
     [("data/services/CrosswayMercenary.tres", "PriceGold = 500", "PriceGold = 0")],
     "hires a sword for"),

    ("kind.empty_contract_board", "ValidateServiceKind/Contracts",
     [("data/services/CrosswayContracts.tres", "BoardSlots = 3", "BoardSlots = 0")],
     "its board would be empty"),

    ("kind.wager_pays_less_than_the_stake", "ValidateWager",
     [("data/services/HollowreachWager.tres", "PayoutGold = 150", "PayoutGold = 10")],
     "a loss with congratulations on it"),

    ("kind.wager_table_is_shut", "ValidateWager",
     [("data/services/HollowreachWager.tres", "PlaysPerDay = 3", "PlaysPerDay = 0")],
     "the table would be shut"),

    ("kind.commission_without_labour", "ValidateCommission",
     [("data/services/EmberCrownCommission.tres", "PriceGold = 60", "PriceGold = 0")],
     "a master who charges no"),

    ("kind.commission_unknown_materials_shop", "ValidateCommission",
     [("data/services/EmberCrownCommission.tres",
       'MaterialsShopId = "shop.ember_crown.smith"', 'MaterialsShopId = "shop.nope"')],
     "prices its materials from unknown shop"),

    # ⚠️ These two are broken by RETYPING a counter rather than by breaking one of its own fields:
    # both rules are about a counter EXISTING at all, so the only way in is to make it something
    # else. Kind 8 is Appraise, which is why an appraiser's rules may also fire alongside.
    ("realm.seizure_is_permanent", "ValidateConfiscationIsRecoverable",
     [("data/services/CrosswayImpound.tres", "Kind = 6", "Kind = 8")],
     "a seizure would be permanent"),

    ("realm.consignment_unpayable", "ValidateConsignmentIsPayable",
     [("data/services/EmbermarketCollect.tres", "Kind = 7", "Kind = 8")],
     "pays the earnings out"),

    # ---- Items and the locale catalogue --------------------------------------------------------
    ("item.unknown_trade_tag", "ValidateItemTags",
     [("data/items/IronOre.tres",
       'TradeTags = Array[String](["ore", "metal"])',
       'TradeTags = Array[String](["ore", "metal", "bogus"])')],
     "carries unknown trade tag"),

    # ⚠️ `shop.line.glut` on purpose: no authored shop can emit it at boot, so a rule that walked
    # the shops and checked whatever keys came out would pass while this tooltip showed a raw key.
    ("locale.breakdown_key_missing", "ValidateBreakdownKeys",
     [("data/locale/strings.csv",
       'shop.line.glut,"{0} of them, their appetite falling — {1}g"\n', "")],
     "price breakdown line"),

    # ---- ValidateStepUp (Phase 39C) -------------------------------------------------------
    # ⚠️ The first case in this battery that mutates a SCENE rather than authored data, which is why
    # the clean-tree guard below had to grow a second directory. The rule pins every cell's
    # agent_max_climb to what a body can actually step (StepUp.MaxHeight): raise one for a new piece
    # of terrain and the navmesh silently goes back to routing NPCs onto ground the player cannot
    # follow them onto — the exact mismatch that made embermarket.tscn delete its dais.
    # ⚠️ Anchored on the newlines, and the uniqueness check is what made that necessary: this cell's
    # header DISCUSSES agent_max_climb in prose as well as setting it, so the bare string lands twice.
    # The same run proved the validator regex had the mirror-image bug and was reading those comments
    # as settings — a rule that fails a cell over a sentence. Both are anchored to a line start now.
    # ⚠️ The mutation is 0.3 -> 0.6, not the old 0.5 -> 0.8, and both numbers moved for a reason.
    # The cells author the FLOORED climb since the 2026-08-31 quantisation pass (0.3 on this cell's
    # 0.3 grid), so the old `find` no longer lands. And 0.6 is two whole voxels, which keeps this
    # case about the rule it is named for: 0.8 would also trip the voxel-grid rule below, and a
    # mutation that fires two refusals cannot prove which one is doing the work.
    ("stepup.navmesh_out_of_reach", "ValidateStepUp",
     [("scenes/regions/ember_crown/embermarket.tscn",
       "\nagent_max_climb = 0.3\n", "\nagent_max_climb = 0.6\n")],
     "above the"),

    # The other half of the same seam (the 2026-08-31 pass): Recast quantises the agent dims to the
    # voxel grid — height and radius CEIL, climb FLOORS — and says so only in a bake warning. 1.75 on
    # a 0.3 cell_height is what every cell in the realm carried for two months while three headers
    # claimed the dims were on the grid; it bakes as 1.8 and the file reads as 1.75.
    ("stepup.agent_off_voxel_grid", "ValidateStepUp",
     [("scenes/regions/ember_crown/embermarket.tscn",
       "\nagent_height = 1.8\n", "\nagent_height = 1.75\n")],
     "is not a whole number of voxels"),

    # ---- ValidateBreakdownKeys, the 39B travel line ---------------------------------------
    # 38U's rule is that the EXPLANATION IS THE CHARGE: a price the player is shown has to say why.
    # A new factor with no authored line prints a raw key where a sentence belongs, and the map
    # screen is the one surface where nobody would notice quickly — a free jump just looks free.
    # PriceBreakdown.AllKeys exists so the validator proves the whole set resolves rather than only
    # the ones today's data happens to reach, and this is that guard being made to fire.
    ("travel.mounted_line_unauthored", "ValidateBreakdownKeys",
     [("data/locale/strings.csv",
       'shop.line.travel_mounted,"you ride it yourself — no charge"\n', "")],
     "price breakdown line"),

    # ---- ValidateMount (Phase 39A) --------------------------------------------------------
    # ⚠️ The one that matters. Ownership of the mount is a single string held in two files that
    # never meet: the stablemaster's UnlockFlagId is what 400 gold buys, MountComponent.OwnedFlagId
    # is what the whistle key reads. Rename either and BOTH halves stay individually correct — the
    # service still charges, the component still checks a flag — while the horse never comes.
    # No test in the suite touches both sides, so this rule and this mutation are the only thing
    # standing between that rename and a silently dead 400-gold purchase.
    ("mount.flag_unreachable", "ValidateMount",
     [("data/services/EmberCrownStable.tres",
       'UnlockFlagId = "flag.stable.mount_owned"',
       'UnlockFlagId = "flag.stable.mount_bought"')],
     "which is the flag"),

    # ---- ValidateMapLocations (Phase 39.5A) -----------------------------------------------
    # A map location is half resource and half scene: the .tres says what a place IS, the
    # MapLocationComponent in the cell scene says WHERE. Every failure below is silent in play —
    # a marker that never appears is indistinguishable from one the player has not discovered yet,
    # which is precisely the state the feature is supposed to represent.
    ("maploc.name_unauthored", "ValidateMapLocations",
     [("data/map_locations/EmberCrownSmith.tres",
       'NameKey = "location.ember_crown.smith.name"',
       'NameKey = "location.ember_crown.anvil.name"')],
     "has no name in the locale catalogue"),

    ("maploc.shop_missing", "ValidateMapLocations",
     [("data/map_locations/EmberCrownSmith.tres",
       'ShopId = "shop.ember_crown.smith"', 'ShopId = "shop.ember_crown.smithy"')],
     "which does not exist"),

    ("maploc.cell_missing", "ValidateMapLocations",
     [("data/map_locations/EmberCrownSmith.tres",
       'CellId = "ember_crown.town_hub"', 'CellId = "ember_crown.town_square"')],
     "which no region declares"),

    # ⚠️ The two directions of the scene seam, and the pair IDS.md says is missing for shop.*
    # and service.*: a scene naming an id nothing declares, and an authored location nothing places.
    ("maploc.scene_names_unknown_id", "ValidateMapMarkersArePlaced",
     [("scenes/regions/ember_crown/town_hub.tscn",
       'LocationId = "location.ember_crown.smith"',
       'LocationId = "location.ember_crown.blacksmith"')],
     "which no map location declares"),

    # ⚠️ The rule that keeps the map from rotting: every shop and service must be ON it. Coverage is
    # 23/23 and 15/15, so this can only be broken by adding something new — which is the point.
    ("maploc.shop_not_on_the_map", "ValidateEverythingIsOnTheMap",
     [("data/map_locations/EmberCrownSmith.tres",
       'ShopId = "shop.ember_crown.smith"', 'ShopId = ""')],
     "is not on the world map"),

    ("maploc.service_not_on_the_map", "ValidateEverythingIsOnTheMap",
     [("data/map_locations/EmberCrownInn.tres",
       'ServiceId = "service.ember_crown.inn"', 'ServiceId = ""')],
     "is not on the world map"),

    # ⚠️ Properties joined the coverage rule after a continuity audit found the realm's only holding
    # — the player's own cottage — missing from the map, while the rule meant to prevent exactly that
    # covered shops and services and nothing else.
    ("maploc.property_not_on_the_map", "ValidateEverythingIsOnTheMap",
     [("data/map_locations/AshfallCottage.tres",
       'PropertyId = "property.ember_crown.cottage"', 'PropertyId = ""')],
     "is not on the world map"),

    # ⚠️ A category name is COMPUTED from the enum member, so adding a member adds a key reference
    # that no resource mentions and no other rule can see. Crafting shipped without its key in 39.5A.
    ("maploc.category_unnamed", "ValidateMapTaxonomyIsNamed",
     [("data/locale/strings.csv", "\nmap.category.smith,Smith\n", "\n")],
     "has no locale key"),

    # ⚠️ 39.5B: the same computed-key hole one screen over. The quest tracker picks a compass point
    # from a bearing and a unit from a magnitude, so neither key is named by any .tres — a missing one
    # prints the raw key under the objective. Two cases, because the two key sets fail independently.
    ("hud.compass_point_unnamed", "ValidateHudComputedKeys",
     [("data/locale/strings.csv", "\nhud.compass.nw,NW\n", "\n")],
     "has no locale key"),

    ("hud.distance_unit_unnamed", "ValidateHudComputedKeys",
     [("data/locale/strings.csv", "\nhud.unit.kilometres,km\n", "\n")],
     "HUD distance readout has no locale key"),

    # ⚠️ 39.5B: the clock's phase name. It was a hard-coded English literal in DayPhases.Label,
    # on screen beside the time since Phase 18, and localizing it created a third computed key set.
    ("hud.day_phase_unnamed", "ValidateHudComputedKeys",
     [("data/locale/strings.csv", "\ntime.phase.dusk,Dusk\n", "\n")],
     "has no locale key"),

    # ⚠️ 39.5C: the quest arm of "IF THE PLAYER CAN GO THERE, IT GOES ON THE MAP", promised in
    # CLAUDE.md §1 as the exemption that ends when quest-to-location linking lands. It has landed.
    ("quest.objective_unknown_location", "ValidateQuests",
     [("data/quests/AncientKin.tres",
       'LocationId = "location.frostfang.ash_roost"',
       'LocationId = "location.frostfang.nowhere"')],
     "points at unknown map location"),

    # ---- 41A: the two new objective types, and the locale rule that caught two live quests --------
    # Reach and Talk each name a row in a database rather than a spawnable template, so a typo is an
    # objective that can never advance — and it fails SILENTLY, because nothing looks the id up until
    # the player is standing in the right place talking to the right person.
    ("quest.reach_unknown_location", "ValidateQuests",
     [("data/quests/WordToTheWharf.tres",
       'TargetId = "location.hollowreach.reach"',
       'TargetId = "location.hollowreach.nowhere"')],
     "references unknown map location"),

    ("quest.talk_unknown_dialogue", "ValidateQuests",
     [("data/quests/WordToTheWharf.tres",
       'TargetId = "dialogue.sedge"', 'TargetId = "dialogue.nobody"')],
     "references unknown dialogue"),

    # ⚠️ A reach objective's target IS its destination, so a LocationId beside it is a second answer
    # to a question that has one — and the map and the compass would read the two different fields.
    ("quest.reach_with_redundant_location", "ValidateQuests",
     [("data/quests/WordToTheWharf.tres",
       'TargetId = "location.hollowreach.reach"\nRequiredCount = 1',
       'TargetId = "location.hollowreach.reach"\nLocationId = "location.hollowreach.hull"\n'
       'RequiredCount = 1')],
     "already its destination"),

    # ⚠️ 41A's most valuable rule, because it found two defects that had shipped. Quest text must be a
    # KEY: twelve quests authored keys and two authored literal English, and nothing noticed for
    # twenty-nine phases because Loc.T returns the key on a miss — so the English rendered perfectly
    # and would have broken on the first non-English locale. quest.gather_iron was live.
    ("quest.title_is_literal_text", "ValidateQuestStringsAreKeys",
     [("data/quests/GatherIron.tres",
       'Title = "quest.gather_iron.title"', 'Title = "Gather Iron"')],
     "is not a key in data/locale/strings.csv"),

    ("quest.objective_description_is_literal_text", "ValidateQuestStringsAreKeys",
     [("data/quests/WordToTheWharf.tres",
       'Description = "quest.hollowreach.word.obj_talk"',
       'Description = "Ask Sedge Marrow about the barrels"')],
     "is not a key in data/locale/strings.csv"),

    # ---- 41B: escort and defend, and the two fields whose meaning changes with the type ----------
    # An escort names a person AND a destination. Either half missing is an objective that can never
    # advance, and the missing-destination case is the one nothing at runtime would ever explain.
    ("quest.escort_unknown_companion", "ValidateQuests",
     [("data/quests/LedgerRun.tres",
       'TargetId = "companion.tessa"', 'TargetId = "companion.nobody"')],
     "names unknown companion"),

    ("quest.escort_without_destination", "ValidateQuests",
     [("data/quests/LedgerRun.tres",
       'LocationId = "location.embermarket.market"', 'LocationId = ""')],
     "sets no LocationId"),

    ("quest.defend_unknown_location", "ValidateQuests",
     [("data/quests/HoldTheNorthRoad.tres",
       'TargetId = "location.wilds.north"', 'TargetId = "location.wilds.nowhere"')],
     "references unknown map location"),

    # ⚠️ RequiredCount is SECONDS on a Defend objective and a tally on every other type, so the
    # authoring default of 1 means a quarter-second hold that completes before the player stops
    # walking. This is the rule that catches an author who copied a Kill objective.
    ("quest.defend_hold_too_short", "ValidateQuests",
     [("data/quests/HoldTheNorthRoad.tres", "RequiredCount = 60", "RequiredCount = 1")],
     "RequiredCount is seconds"),

    # ---- 41C: the two remaining objective types, and the deadline ---------------------------------
    # An Interact target is a scene-authored id with NO database behind it (the second of its kind
    # after LocationId), so both directions of the scan are rules: an id nothing authors, and an id
    # two nodes author.
    ("quest.interact_unplaced_id", "ValidateInteractIdsArePlaced",
     [("data/quests/TheSealedTally.tres",
       'TargetId = "interact.emberdeep.waystone"',
       'TargetId = "interact.emberdeep.nowhere"')],
     "which no scene authors on an interactable"),

    ("scene_id.interact_id_authored_twice", "ValidateInteractIdsArePlaced",
     [("scenes/regions/ember_crown/hollowreach.tscn",
       'Id = "travel.ember_crown.hollowreach"',
       'Id = "travel.ember_crown.hollowreach"\nInteractId = "interact.emberdeep.waystone"')],
     "is authored twice"),

    # ⚠️ A stealth objective is a CONDITION and targets nothing. An authored target reads as a scope
    # ("undetected by goblins") that the rule does not keep - any enemy engaging blows it.
    ("quest.stealth_with_a_target", "ValidateQuests",
     [("data/quests/TheSealedTally.tres",
       'Type = 7\nTargetId = ""', 'Type = 7\nTargetId = "enemy.goblin"')],
     "a stealth condition targets nothing"),

    # ⚠️ Stealth objectives start ALREADY MET, so a quest made only of them completes on the frame it
    # is accepted - silently, with rewards.
    ("quest.only_stealth_objectives", "ValidateQuestCompletability",
     [("data/quests/TheSealedTally.tres",
       'Objectives = Array[Resource]([SubResource("Obj_touch_waystone"), SubResource("Obj_unseen")])',
       'Objectives = Array[Resource]([SubResource("Obj_unseen")])')],
     "has only stealth objectives"),

    ("quest.deadline_too_short", "ValidateQuestCompletability",
     [("data/quests/TheSealedTally.tres", "TimeLimitSeconds = 180.0", "TimeLimitSeconds = 5.0")],
     "time limit"),

    ("maploc.location_never_placed", "ValidateMapMarkersArePlaced",
     [("scenes/regions/ember_crown/town_hub.tscn",
       '\n[node name="MapPin" type="Node3D" parent="VendorSmith"]\n'
       'script = ExtResource("maploc_39_5a")\n'
       'LocationId = "location.ember_crown.smith"\n',
       "\n")],
     "no cell scene places a MapLocationComponent"),

    # ⚠️ The audit's rule (2026-08-15), and the hole it closes was named in the code for phases
    # before it was closed: ServiceComponent's own header said a mistyped ServiceId "yields NO PROMPT
    # AT ALL rather than an error", because no rule scanned .tscn for it. A database walk cannot
    # reach these — the id is a string in a scene file, not a field on any resource — so the failure
    # was a keeper who silently offers nothing, which reads as unfinished content, not as a typo.
    #
    # Three cases, not one: the rule dispatches per property name through a table, so proving
    # ServiceId fires does not prove ShopId's row is wired to the shop database rather than a
    # copy-pasted service lookup. One per database that a scene can name today.
    ("scene_id.service_unknown", "ValidateSceneAuthoredIds",
     [("scenes/regions/ember_crown/town_hub.tscn",
       'ServiceId = "service.ember_crown.bank"', 'ServiceId = "service.no_such_keeper"')],
     "which no service declares"),

    ("scene_id.shop_unknown", "ValidateSceneAuthoredIds",
     [("scenes/regions/ember_crown/town_hub.tscn",
       'ShopId = "shop.ember_crown.traveller"', 'ShopId = "shop.no_such_stall"')],
     "which no shop declares"),

    ("scene_id.dialogue_unknown", "ValidateSceneAuthoredIds",
     [("scenes/regions/ember_crown/town_hub.tscn",
       'DialogueId = "dialogue.elder"', 'DialogueId = "dialogue.no_such_conversation"')],
     "which no dialogue declares"),

    # ---- 41D: branch gates and objective ordering -------------------------------------------
    #
    # ⚠️ A branch gate is a story flag, and story flags are the ONE id family with no database
    # behind them - so the only instrument that can catch a typo is the reader/writer
    # cross-reference. A gate on a flag nothing sets is a path the player can never be on: the
    # objective is inert forever, the journal never draws it, and nothing at runtime says why.
    ("quest.branch_flag_never_set", "ValidateStoryFlags",
     [("data/quests/WhatThePostTook.tres",
       'Description = "quest.hollowreach.barrels.obj_post"\nRequiredFlagId = "flag.hollowreach.barrels_declared"',
       'Description = "quest.hollowreach.barrels.obj_post"\nRequiredFlagId = "flag.hollowreach.barrels_declaredd"')],
     "which nothing ever sets"),

    # ⚠️ A gate that is its own opposite can never open, so the objective silently does not exist -
    # the quest is shorter than it reads and nothing anywhere reports it.
    ("quest.gate_contradicts_itself", "ValidateQuests",
     [("data/quests/WhatThePostTook.tres",
       'Description = "quest.hollowreach.barrels.obj_wren"\nRequiredFlagId = "flag.hollowreach.barrels_declared"',
       'Description = "quest.hollowreach.barrels.obj_wren"\nRequiredFlagId = "flag.hollowreach.barrels_declared"\nForbiddenFlagId = "flag.hollowreach.barrels_declared"')],
     "could never be active"),

    # ⚠️ A Stealth objective is seeded ALREADY MET and can only be lost (41C), so gating one off
    # makes it a condition that cannot be broken - a rule that ships as a no-op through every gate.
    ("quest.stealth_objective_gated", "ValidateQuests",
     [("data/quests/TheSealedTally.tres",
       'Description = "quest.emberdeep.tally.obj_unseen"',
       'Description = "quest.emberdeep.tally.obj_unseen"\nRequiredFlagId = "flag.slice.completed"')],
     "starts already met and can only be lost"),

    # ⚠️ A knob you validate is a claim the knob works (invariant 37). Ordering one objective orders
    # nothing, and the author who set the bool believes their quest is sequenced.
    ("quest.sequential_with_one_objective", "ValidateQuestCompletability",
     [("data/quests/GatherIron.tres",
       "\nPrerequisiteQuestId", "\nSequentialObjectives = true\nPrerequisiteQuestId")],
     "ordering needs at least two"),

    # ⚠️ Every objective behind ONE flag is availability wearing a branch's hat - the quest sits in
    # the log with nothing in it at all, which is the state QuestProgress refuses to complete and
    # therefore the state that hangs forever.
    ("quest.whole_quest_gated_on_one_flag", "ValidateQuestCompletability",
     [("data/quests/WhatThePostTook.tres",
       'Description = "quest.hollowreach.barrels.obj_landing"\nRequiredFlagId = "flag.hollowreach.barrels_hushed"',
       'Description = "quest.hollowreach.barrels.obj_landing"\nRequiredFlagId = "flag.hollowreach.barrels_declared"'),
     ("data/quests/WhatThePostTook.tres",
       'Description = "quest.hollowreach.barrels.obj_odger"\nRequiredFlagId = "flag.hollowreach.barrels_hushed"',
       'Description = "quest.hollowreach.barrels.obj_odger"\nRequiredFlagId = "flag.hollowreach.barrels_declared"')],
     "that is availability, not a branch"),
    # Phase 41E: completion flags are world-state writers, so an id outside the flag family would
    # silently produce a consumer nothing can re-derive from. The scene reader is checked too: a
    # typo there leaves a departed NPC standing forever and looks like ordinary content.
    ("quest.completion_flag_wrong_family", "ValidateStoryFlags",
     [("data/quests/WarbandHeart.tres", 'CompletionFlagId = "flag.frostfang.passage_open"',
       'CompletionFlagId = "quest.frostfang.passage_open"')],
     "completion flag 'quest.frostfang.passage_open' must start with 'flag.'"),

    # 41F: both ids are individually valid, but a completion flag lands only after this quest's
    # live objectives are done. Making an objective wait on it is a self-locked branch that passes
    # every ordinary cross-reference check and can never be reached in play.
    ("quest.completion_flag_self_gates_objective", "ValidateQuestCompletionFlagsDoNotSelfGate",
     [("data/quests/WarbandHeart.tres",
       'Description = "quest.warband.heart.obj"',
       'Description = "quest.warband.heart.obj"\nRequiredFlagId = "flag.frostfang.passage_open"')],
     "requires its own completion flag"),

    ("world.visibility_flag_never_written", "ValidateStoryFlags",
     [("scenes/regions/ember_crown/hollowreach.tscn", 'HiddenWhenFlagId = "flag.emberdeep.tally_delivered"',
       'HiddenWhenFlagId = "flag.emberdeep.tally_delivereed"')],
     "world actor hides after flag 'flag.emberdeep.tally_delivereed', which nothing ever sets"),

    # 41.5A: a shrine whose modifier is zero looks complete in the world but grants no blessing.
    # The resource still loads and the interaction still fires, so only the authored-data gate sees it.
    ("shrine.grants_no_bonus", "ValidateShrines",
     [("data/shrines/Solaryn.tres", "Value = 10.0", "Value = 0.0")],
     "grants no passive bonus"),

    # 41.5B: the six dead-god blessings are a closed, map-linked set. A renamed resource still
    # loads and might leave a plausible shrine body in the world, so the data gate must name the
    # missing canonical shrine rather than relying on an interaction to discover it.
    ("shrine.required_six", "ValidateShrines",
     [("data/shrines/Elyndra.tres", 'Id = "shrine.elyndra"', 'Id = "shrine.elyndra_missing"')],
     "missing required shrine 'shrine.elyndra'"),

    # The reverse seam: an otherwise valid blessing with no body has no caller and is dead content.
    ("shrine.body_required", "ValidateShrineWorldBodies",
     [("scenes/regions/ember_crown/embermarket.tscn", 'ShrineId = "shrine.nyth"', 'ShrineId = ""')],
     "shrine 'shrine.nyth' has no in-world shrine body"),

    # 41.5C: the corruption gate is authored at both ends, and both ends fail silently in-world.
    # A threshold at the corruption floor refuses a clean player, so the blessing is unreachable...
    ("shrine.refusal_always", "ValidateShrines",
     [("data/shrines/Solaryn.tres", "RefusalCorruption = 40", "RefusalCorruption = 0")],
     "refusal corruption 0 is outside 1..100"),

    # ...and a threshold past the ceiling can never be reached, so the refusal branch is dead
    # content that looks authored. The gate must catch the useless value in both directions.
    ("shrine.refusal_never", "ValidateShrines",
     [("data/shrines/Solaryn.tres", "RefusalCorruption = 40", "RefusalCorruption = 101")],
     "refusal corruption 101 is outside 1..100"),

    # And a world body must not silently name a resource that does not exist.
    ("shrine.body_names_resource", "ValidateSceneAuthoredIds",
     [("scenes/regions/ember_crown/embermarket.tscn", 'ShrineId = "shrine.nyth"',
       'ShrineId = "shrine.nyth.misspelled"')],
     "authors ShrineId = 'shrine.nyth.misspelled', which no shrine declares"),

    # 42A: a guild IS a faction with ranks, so the closed set fails in BOTH directions and neither
    # direction shows up anywhere else. Drop the ranks and a listed guild quietly becomes an
    # ordinary faction: it still loads, still draws its standing line, and every quest gated on
    # membership goes dead with nothing to point at.
    ("guild.declares_no_ranks", "ValidateGuilds",
     [("data/factions/Dawnwardens.tres",
       'RankNameKeys = Array[String](["guild.dawnwardens.rank1", "guild.dawnwardens.rank2", "guild.dawnwardens.rank3"])',
       'RankNameKeys = Array[String]([])')],
     "declares no ranks, so nothing treats it as a guild"),

    # ...and the reverse: an ordinary faction that starts declaring ranks becomes a sixth guild the
    # rank vocabulary, GameIds.Guilds and the character screen know nothing about.
    ("guild.unlisted_faction_declares_ranks", "ValidateGuilds",
     [("data/factions/Outlaws.tres", 'Allies = Array[String]([])',
       'Allies = Array[String]([])\nRankNameKeys = Array[String]([\"guild.dawnwardens.rank1\"])')],
     "declares ranks but is not one of the five authored guilds"),

    # The rank count is an authored range (invariant 8). Above GuildRules.MaxRanks the flag
    # vocabulary and the UI cannot express the ranks, and the .tres loads perfectly.
    ("guild.too_many_ranks", "ValidateGuilds",
     [("data/factions/Dawnwardens.tres",
       '"guild.dawnwardens.rank3"])',
       '"guild.dawnwardens.rank3", "guild.dawnwardens.rank4", "guild.dawnwardens.rank5", "guild.dawnwardens.rank6"])')],
     "above the 5 the flag vocabulary and UI support"),

    # A rank name key that is not in the catalogue renders the raw key as the player's rank —
    # Loc.T returns the key on a miss, so the screen looks authored and reads "guild.x.rank2".
    ("guild.rank_key_missing", "ValidateGuilds",
     [("data/factions/AshHunters.tres", '"guild.ash_hunters.rank2"', '"guild.ash_hunters.rank2_missing"')],
     "rank key 'guild.ash_hunters.rank2_missing' is missing from the locale catalogue"),
]


def run(cmd, **kwargs):
    return subprocess.run(cmd, cwd=REPO, capture_output=True, text=True, **kwargs)


def tree_state():
    """Exact relevant git state, used to prove dirty authoring survives the battery unchanged."""
    return run(["git", "status", "--porcelain", "data/", "scenes/"]).stdout.strip()


def validate():
    """Run the content gate. Returns (exit_code, combined output)."""
    result = run([GODOT, "--headless", "--path", ".", "--", "--validate"])
    return result.returncode, result.stdout + result.stderr


def snapshot(paths):
    return {rel: (REPO / rel).read_bytes() for rel in paths}


def restore(saved, paths=None):
    for rel in (paths if paths is not None else saved):
        (REPO / rel).write_bytes(saved[rel])


def apply_case(edits):
    """Apply every edit, refusing any whose `find` is absent or ambiguous — a mutation that did not
    land is the failure mode that would otherwise report a rule as broken-and-recovered while never
    having broken it at all."""
    touched = set()
    for rel, find, replace in edits:
        path = REPO / rel
        text = path.read_text(encoding="utf-8")
        hits = text.count(find)
        if hits != 1:
            raise LookupError(f"{rel}: found {hits} occurrences of {find!r}, expected exactly 1")
        path.write_text(text.replace(find, replace), encoding="utf-8")
        touched.add(rel)
    return touched


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--only", help="run cases whose name or rule contains this text")
    parser.add_argument("--list", action="store_true", help="print the battery and exit")
    args = parser.parse_args()

    cases = CASES
    if args.only:
        needle = args.only.lower()
        cases = [c for c in CASES if needle in c[0].lower() or needle in c[1].lower()]
        if not cases:
            print(f"no case matches {args.only!r}")
            return 2

    if args.list:
        for name, rule, _, expect in CASES:
            print(f"{name:44} {rule:40} expects: {expect}")
        print(f"\n{len(CASES)} cases.")
        return 0

    all_targets = {rel for _, _, edits, _ in CASES for rel, _, _ in edits}
    initial_state = tree_state()
    saved = snapshot(all_targets)

    print(f"{len(cases)} case(s). Each one breaks a rule, proves the right rule fired, "
          "and puts the data back.\n")

    failures = []
    touched = set()
    try:
        for index, (name, rule, edits, expect) in enumerate(cases, start=1):
            print(f"[{index}/{len(cases)}] {name} ({rule}) ... ", end="", flush=True)
            try:
                touched = apply_case(edits)
            except LookupError as error:
                print(f"MUTATION DID NOT LAND\n    {error}")
                failures.append(name)
                continue

            code, output = validate()
            restore(saved, touched)
            touched = set()

            if code == 0:
                print("NOT CAUGHT — the validator passed on broken data")
                failures.append(name)
            elif expect not in output:
                print(f"WRONG RULE FIRED — no {expect!r} in the report")
                failures.append(name)
            else:
                print("caught")
    finally:
        if touched:
            restore(saved, touched)

    print("\nrestoring and re-checking the tree ... ", end="", flush=True)
    restore(saved)
    final_state = tree_state()
    preserved = final_state == initial_state
    print("preserved" if preserved else f"CHANGED\nBefore:\n{initial_state}\nAfter:\n{final_state}")

    print("proving the battery recovers: --validate on restored data ... ", end="", flush=True)
    code, _ = validate()
    print("exit 0" if code == 0 else f"EXIT {code} — the restore did not restore")

    if failures or not preserved or code != 0:
        print(f"\nFAILED: {len(failures)} case(s): {', '.join(failures) or '-'}")
        return 1

    print(f"\nOK: {len(cases)} rule(s) broken and restored, each caught by its own refusal.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
