# Region-transition lifecycle probe. Streams the Ember Crown, hard-swaps to Frostfang Reach and back,
# and asserts what a transition must leave behind: nothing.
#
# It exists because the streamer stops its own _Process once a region settles (the 2026-08-30
# debugging pass), so "does it wake up again on the next Configure/UnloadAll" is now a real question
# that only a run can answer — and because an orphaned cell after a swap is invisible in the log.
#
# Run:  Godot_..._console.exe --headless --path . --script res://tools/region_transition_probe.gd
extends SceneTree

const EMBER := "res://data/regions/EmberCrown.tres"
const FROST := "res://data/regions/FrostfangReach.tres"

var _streamer: Node3D
var _failures: Array[String] = []


func _initialize() -> void:
	root.add_child(load("res://src/Bootstrap/ContentDatabaseLoader.cs").new())
	await process_frame
	_streamer = load("res://src/World/RegionStreamer.cs").new()
	root.add_child(_streamer)
	_streamer.call("SetPerformanceSamplingEnabled", false)

	await _enter(EMBER, "ember_crown")
	await _enter(FROST, "frostfang_reach")
	await _enter(EMBER, "ember_crown")

	# And the teardown a shutdown performs.
	_streamer.call("UnloadAll")
	_streamer.call("Configure", null)
	for _f in 4:
		await process_frame
	var left := _cell_children()
	if not left.is_empty():
		_failures.append("cells still parented after UnloadAll+Configure(null): %s" % [left])

	if _failures.is_empty():
		print("PASS: three region swaps, no orphaned cells, no duplicates, every region settled")
		quit(0)
	else:
		for f in _failures:
			print("FAIL: %s" % f)
		quit(1)


func _enter(region_path: String, expected_id: String) -> void:
	# The real order WorldSessionDirector.PerformRegionLoad uses.
	_streamer.call("UnloadAll")
	var region: Resource = load(region_path)
	_streamer.call("Configure", region)
	var frames := 0
	while not _streamer.call("IsSettled") and frames < 900:
		await process_frame
		frames += 1
	if not _streamer.call("IsSettled"):
		_failures.append("%s never settled (%d frames)" % [expected_id, frames])
		return
	# Let the previous region's queue_free()s actually run.
	for _f in 4:
		await process_frame

	if not String(_streamer.get("ActiveRegionId")).ends_with(expected_id):
		_failures.append("ActiveRegionId is '%s', expected '%s'"
			% [_streamer.get("ActiveRegionId"), expected_id])

	var got := _cell_children()
	var sorted_got := got.duplicate()
	sorted_got.sort()
	if got.is_empty() or int(_streamer.call("ActiveCellCount")) < 1:
		_failures.append("%s has no active landing cell" % expected_id)
	var unique := {}
	for cell_name in got:
		unique[cell_name] = true
	if unique.size() != got.size():
		_failures.append("%s has duplicate cell nodes" % expected_id)
	for cell_name in got:
		if not cell_name.begins_with(expected_id + "_"):
			_failures.append("%s retained foreign cell %s" % [expected_id, cell_name])
	print("%s: %d cells resident after %d frames" % [expected_id, got.size(), frames])


## ⚠️ A NODE NAME IS NOT THE CELL ID. Godot rejects "." in a node name and rewrites it, so the cell
## root named `cell.Id` lands under a sanitised name — compare against the same sanitisation, never
## the raw id, or every cell reads as missing.
func _node_name(cell_id: String) -> String:
	var probe := Node.new()
	probe.name = cell_id
	var sanitised := String(probe.name)
	probe.free()
	return sanitised


## Cell roots are the streamer children named for a cell id — i.e. prefixed with a region id. The
## rest are the streamer's own services (recovery, performance, visibility) and the region backdrop,
## which is a bare MultiMeshInstance3D or a named landscape depending on the region profile.
func _cell_children() -> Array[String]:
	var names: Array[String] = []
	for child in _streamer.get_children():
		var n := String(child.name)
		if n.begins_with("ember_crown_") or n.begins_with("frostfang_reach_"):
			names.append(n)
	return names
