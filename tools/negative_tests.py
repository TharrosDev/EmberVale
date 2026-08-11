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

⚠️ THIS EDITS `data/` AND `scenes/` IN PLACE, and that makes it the most dangerous script in the
repo. Three guards, all required and none of them optional:
  1. it refuses to start unless git reports `data/` AND `scenes/` clean, so it can never eat
     uncommitted authoring or level work (39C added the second directory with the first scene case —
     the restore is a `git checkout --`, so anything this can mutate it can also destroy);
  2. every mutation is undone in a `finally`, so Ctrl-C and a crash both restore;
  3. it re-asserts a clean tree before exiting, and shouts if it cannot.

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
    ("stepup.navmesh_out_of_reach", "ValidateStepUp",
     [("scenes/regions/ember_crown/embermarket.tscn",
       "\nagent_max_climb = 0.5\n", "\nagent_max_climb = 0.8\n")],
     "above the"),

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

    ("maploc.location_never_placed", "ValidateMapMarkersArePlaced",
     [("scenes/regions/ember_crown/town_hub.tscn",
       '\n[node name="MapPin" type="Node3D" parent="VendorSmith"]\n'
       'script = ExtResource("maploc_39_5a")\n'
       'LocationId = "location.ember_crown.smith"\n',
       "\n")],
     "no cell scene places a MapLocationComponent"),
]


def run(cmd, **kwargs):
    return subprocess.run(cmd, cwd=REPO, capture_output=True, text=True, **kwargs)


def tree_is_clean():
    """⚠️ `scenes/` joined `data/` here in 39C, and the reason is worth the line: the restore is a
    `git checkout --`, so any directory this script MUTATES it can also DESTROY. A case that edits a
    cell scene while the guard watches only data/ would silently discard uncommitted level work."""
    out = run(["git", "status", "--porcelain", "data/", "scenes/"]).stdout.strip()
    return out == "", out


def validate():
    """Run the content gate. Returns (exit_code, combined output)."""
    result = run([GODOT, "--headless", "--path", ".", "--", "--validate"])
    return result.returncode, result.stdout + result.stderr


def restore(paths):
    run(["git", "checkout", "--"] + sorted(paths))


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

    clean, dirty = tree_is_clean()
    if not clean:
        print("REFUSING TO RUN: data/ or scenes/ has uncommitted changes.\n"
              "This script edits authored data in place and restores with `git checkout`, which "
              "would discard the work below.\n" + dirty)
        return 2

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
            restore(touched)
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
            restore(touched)

    print("\nrestoring and re-checking the tree ... ", end="", flush=True)
    restore({rel for _, _, edits, _ in CASES for rel, _, _ in edits})
    clean, dirty = tree_is_clean()
    print("clean" if clean else f"DIRTY\n{dirty}")

    print("proving the battery recovers: --validate on restored data ... ", end="", flush=True)
    code, _ = validate()
    print("exit 0" if code == 0 else f"EXIT {code} — the restore did not restore")

    if failures or not clean or code != 0:
        print(f"\nFAILED: {len(failures)} case(s): {', '.join(failures) or '-'}")
        return 1

    print(f"\nOK: {len(cases)} rule(s) broken and restored, each caught by its own refusal.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
