extends SceneTree

# 38T scratch harness: the SupplyShockService save round trip, run headlessly.
#   godot --headless --path . --script res://tools/shock_roundtrip.gd
# Not shipped content — the same throwaway shape 38R2 and 38S used for their ledgers.
#
# ⚠️ GDScript can only call the methods whose signatures are Variant-compatible: Force, Clear, Save
# and Load. At/ActiveOn/Deliver/TagsFor take or return C# types (SupplyShock, IReadOnlyList) and are
# invisible from here, so what this proves is the save contract and the §7 replace-never-merge rule.

func _initialize() -> void:
	var script := load("res://src/Economy/SupplyShockService.cs")
	if script == null:
		print("shock round trip: FAIL (no script)")
		quit(1)
		return

	var svc = script.new()
	var fails := 0

	# 1. A forced shock is in the save, with its window intact.
	svc.Force("ember_crown.emberdeep_mine", "ore", 0, 10, 3)
	var saved: Dictionary = svc.Save()
	fails += _check(saved.has("shocks"), "the save has a shocks array")
	fails += _check(saved["shocks"].size() == 1, "one row saved")
	var row: Dictionary = saved["shocks"][0]
	fails += _check(row["cell"] == "ember_crown.emberdeep_mine", "cell round trips")
	fails += _check(row["tag"] == "ore", "tag round trips")
	fails += _check(row["kind"] == 0, "kind round trips as an int")
	fails += _check(row["start"] == 10 and row["days"] == 3, "window round trips")
	fails += _check(row["hauled"] == 0, "nothing hauled yet")
	fails += _check(saved["rolled"] == 10, "the roll cursor is saved (got %s)" % saved["rolled"])

	# 2. Two shocks, two rows — the list is not a single-slot field.
	svc.Force("ember_crown.tarn_landing", "fish", 0, 10, 2)
	fails += _check(svc.Save()["shocks"].size() == 2, "two rows saved")

	# 3. Clear removes one and leaves the other.
	fails += _check(svc.Clear("ember_crown.tarn_landing"), "clear reports what it removed")
	fails += _check(svc.Save()["shocks"].size() == 1, "one row left")
	fails += _check(not svc.Clear("ember_crown.tarn_landing"), "clearing nothing reports nothing")

	# 4. ⚠️ Load REPLACES, never merges (§7). A shock live in the timeline being abandoned must not
	#    survive into the one being restored — the quickload case that leaves prices moved for an event
	#    that never happened in this save.
	svc.Force("ember_crown.tarn_landing", "fish", 0, 10, 2)
	svc.Load(saved)
	var after: Array = svc.Save()["shocks"]
	fails += _check(after.size() == 1, "the live shock did not survive the load")
	fails += _check(after[0]["cell"] == "ember_crown.emberdeep_mine", "the saved shock came back")

	# 5. The empty-save case — the one that catches a merge, and the one a pre-38T save produces.
	svc.Load({})
	fails += _check(svc.Save()["shocks"].size() == 0, "an empty save clears every shock")
	fails += _check(svc.Save()["rolled"] == -2147483648, "and rewinds the roll cursor to int.MinValue")

	# 6. A garbage row is skipped rather than crashing the load.
	svc.Load({"shocks": [{"tag": "ore"}, {"cell": "c", "tag": "ore", "kind": 1, "start": 3, "days": 2}]})
	fails += _check(svc.Save()["shocks"].size() == 1, "a row with no cell id is dropped")

	print("shock round trip: %s" % ("PASS" if fails == 0 else "FAIL (%d)" % fails))
	svc.free()
	quit(1 if fails > 0 else 0)


func _check(ok: bool, what: String) -> int:
	print("  %s  %s" % ["ok  " if ok else "FAIL", what])
	return 0 if ok else 1
