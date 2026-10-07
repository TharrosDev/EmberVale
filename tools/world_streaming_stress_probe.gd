extends SceneTree

## Production streaming stress: rapid traversal, distant cycling, boundary oscillation and teardown,
## for every realm. It requires prepared collision/navigation before accepting each focus and checks
## that unloading returns resident cells, node counts, orphans and static memory instead of growing
## them over repeated passes.
##
##     Godot_..._console.exe --headless --path . --script res://tools/world_streaming_stress_probe.gd
##     ... -- --region ember_crown          one realm (repeatable; matches the id or the file name)
##     ... -- --cycles 3                    traversal passes per realm (default 2)
##     ... -- --max-activation-ms 1500      fail when any cell takes longer than this to become ready
##     ... -- --max-orphan-growth 0         fail when a realm leaves more orphan nodes than this behind
##     ... -- --max-static-growth-mb 64     fail when a realm leaves more static memory than this behind
##     ... -- --json-file PATH              per-realm activation p50/p95/max, slowest cells and growth
##
## Orphan and static-memory growth per realm are always measured and written to the JSON; they only
## fail the run when a limit is given, because pooled nodes are parked detached on purpose and count
## as orphans, so the right limit is a measured one.
##
## Output is one PASS line (or one FAIL line per failure) and exit 0/1; 2 on a bad argument or a
## filter that matched nothing. Compare two revisions with `python tools/perf_compare.py PATH`.
##
## ⚠️ Activation time here is headless: it is loading, instancing, collision and navigation, without
## the GPU upload a rendered session also pays, so it understates a hitch the player would feel.

const REGIONS := [
	"res://data/regions/EmberCrown.tres",
	"res://data/regions/FrostfangReach.tres",
	"res://data/regions/AshenWilds.tres",
	"res://data/regions/Sunspire.tres",
	"res://data/regions/PaleConcord.tres",
	"res://data/regions/CelestialRealm.tres",
]
const MAX_WAIT_FRAMES := 900
const NODE_GROWTH_TOLERANCE := 12
const SLOWEST_KEPT := 5

var _failures: Array[String] = []
var _streamer: Node3D
var _region_filters: Array[String] = []
var _cycles := 2
var _max_activation_ms := 0.0
var _max_orphan_growth := -1
var _max_static_growth_mb := -1.0
var _json_path := ""


func _initialize() -> void:
	if not _parse_arguments(OS.get_cmdline_user_args()):
		quit(2)
		return
	root.add_child(load("res://src/Bootstrap/ContentDatabaseLoader.cs").new())
	_run.call_deferred()


func _parse_arguments(args: PackedStringArray) -> bool:
	var index := 0
	while index < args.size():
		var argument := args[index]
		if argument not in ["--region", "--cycles", "--max-activation-ms", "--max-orphan-growth",
				"--max-static-growth-mb", "--json-file"]:
			printerr("world streaming stress: unknown argument %s" % argument)
			return false
		if index + 1 >= args.size():
			printerr("world streaming stress: %s requires a value" % argument)
			return false
		index += 1
		var value := args[index]
		match argument:
			"--region": _region_filters.append(_normalise(value))
			"--cycles": _cycles = clampi(int(value), 1, 20)
			"--max-activation-ms": _max_activation_ms = maxf(0.0, float(value))
			"--max-orphan-growth": _max_orphan_growth = maxi(0, int(value))
			"--max-static-growth-mb": _max_static_growth_mb = maxf(0.0, float(value))
			"--json-file": _json_path = value
		index += 1
	return true


## Lower case with everything but letters and digits removed, so `ember_crown`, `EmberCrown` and
## `region.ember_crown` name the same realm.
func _normalise(text: String) -> String:
	var result := ""
	for character in text.to_lower():
		if (character >= "a" and character <= "z") or (character >= "0" and character <= "9"):
			result += character
	return result


func _wanted(region: Resource, path: String) -> bool:
	if _region_filters.is_empty():
		return true
	for wanted in _region_filters:
		if _normalise(String(region.get("Id"))).contains(wanted) or _normalise(path.get_file().get_basename()).contains(wanted):
			return true
	return false


func _run() -> void:
	await process_frame
	_streamer = load("res://src/World/RegionStreamer.cs").new()
	root.add_child(_streamer)
	_streamer.call("SetPerformanceSamplingEnabled", false)
	var baseline_nodes := int(Performance.get_monitor(Performance.OBJECT_NODE_COUNT))
	var worst_activation_ms := 0.0
	var regions := {}
	var cells_visited := 0

	for region_path in REGIONS:
		var region: Resource = load(region_path)
		if not _wanted(region, String(region_path)):
			continue
		var region_id := String(region.get("Id"))
		var nodes_before := int(Performance.get_monitor(Performance.OBJECT_NODE_COUNT))
		var orphans_before := int(Performance.get_monitor(Performance.OBJECT_ORPHAN_NODE_COUNT))
		var static_before := Performance.get_monitor(Performance.MEMORY_STATIC) / 1048576.0
		var activations: Array[float] = []
		var per_cell := {}
		_streamer.call("Configure", region)
		for cycle in _cycles:
			for cell in region.get("Cells"):
				var centre: Vector3 = cell.get("Center")
				var started := Time.get_ticks_usec()
				_streamer.call("SetStreamingFocus", centre)
				if not await _wait_ready(centre):
					_failures.append("%s failed rapid-traversal activation" % cell.get("Id"))
					continue
				var activation_ms := (Time.get_ticks_usec() - started) / 1000.0
				activations.append(activation_ms)
				var cell_id := String(cell.get("Id"))
				per_cell[cell_id] = maxf(activation_ms, float(per_cell.get(cell_id, 0.0)))
				if _max_activation_ms > 0.0 and activation_ms > _max_activation_ms:
					_failures.append("%s took %.0f ms to activate (limit %.0f)" % [
						cell_id, activation_ms, _max_activation_ms])

		var cells: Array = region.get("Cells")
		if cells.size() >= 2:
			for crossing in 20:
				var focus: Vector3 = cells[crossing % 2].get("Center")
				_streamer.call("SetStreamingFocus", focus)
				if not await _wait_ready(focus):
					_failures.append("%s boundary oscillation lost collision/nav" % region_path)
					break

		# A focus far outside the production package must retire every cell and fire persistence seams.
		_streamer.call("SetStreamingFocus", Vector3(10000.0, 0.0, 10000.0))
		var unload_frames := 0
		while int(_streamer.call("ResidentCellCount")) > 0 and unload_frames < MAX_WAIT_FRAMES:
			await process_frame
			unload_frames += 1
		if int(_streamer.call("ResidentCellCount")) != 0:
			_failures.append("%s retained cells after out-of-range unload" % region_path)
		_streamer.call("UnloadAll")
		_streamer.call("Configure", null)
		for _frame in 5:
			await process_frame

		# What this realm left behind once it was fully unloaded.
		var node_delta := int(Performance.get_monitor(Performance.OBJECT_NODE_COUNT)) - nodes_before
		var orphan_delta := int(Performance.get_monitor(Performance.OBJECT_ORPHAN_NODE_COUNT)) - orphans_before
		var static_delta := Performance.get_monitor(Performance.MEMORY_STATIC) / 1048576.0 - static_before
		if _max_orphan_growth >= 0 and orphan_delta > _max_orphan_growth:
			_failures.append("%s left %d orphan node(s) after unload" % [region_id, orphan_delta])
		if _max_static_growth_mb >= 0.0 and static_delta > _max_static_growth_mb:
			_failures.append("%s left %.0f MB of static memory after unload" % [region_id, static_delta])

		activations.sort()
		var ranked: Array = per_cell.keys()
		ranked.sort_custom(func(a, b): return per_cell[a] > per_cell[b])
		var slowest: Array = []
		for slow_cell in ranked.slice(0, SLOWEST_KEPT):
			slowest.append({"cell": slow_cell, "ms": snappedf(per_cell[slow_cell], 0.1)})
		var region_worst: float = activations.back() if not activations.is_empty() else 0.0
		worst_activation_ms = maxf(worst_activation_ms, region_worst)
		cells_visited += per_cell.size()
		regions[region_id] = {
			"cell_count": per_cell.size(),
			"activation_ms": {"p50": snappedf(_percentile(activations, 0.50), 0.1),
				"p95": snappedf(_percentile(activations, 0.95), 0.1), "max": snappedf(region_worst, 0.1)},
			"slowest": slowest,
			"node_delta": node_delta, "orphan_delta": orphan_delta,
			"static_mb_delta": snappedf(static_delta, 0.1),
		}

	if regions.is_empty() and _failures.is_empty():
		printerr("world streaming stress: no region matched --region %s" % [_region_filters])
		quit(2)
		return

	var ending_nodes := int(Performance.get_monitor(Performance.OBJECT_NODE_COUNT))
	if ending_nodes > baseline_nodes + NODE_GROWTH_TOLERANCE:
		_failures.append("node growth after soak: %d -> %d" % [baseline_nodes, ending_nodes])

	if not _json_path.is_empty():
		var file := FileAccess.open(_json_path, FileAccess.WRITE)
		if file == null:
			_failures.append("could not write %s: %s" % [_json_path, FileAccess.get_open_error()])
		else:
			file.store_string(JSON.stringify({"schema": 1, "suite": "streaming-stress", "headless":
				DisplayServer.get_name() == "headless", "cycles": _cycles, "regions": regions,
				"node_growth": ending_nodes - baseline_nodes, "failures": _failures}, "  "))

	if _failures.is_empty():
		print("world streaming stress: PASS (%d region(s), %d cells x %d passes, boundary oscillation, " % [
			regions.size(), cells_visited, _cycles] +
			"collision/nav gating, unload soak; worst activation %.1f ms)" % worst_activation_ms)
		quit(0)
	else:
		for failure in _failures:
			printerr("world streaming stress: FAIL — %s" % failure)
		quit(1)


func _percentile(sorted: Array[float], fraction: float) -> float:
	return sorted[int((sorted.size() - 1) * fraction)] if not sorted.is_empty() else 0.0


func _wait_ready(position: Vector3) -> bool:
	var frames := 0
	while (not _streamer.call("IsPositionReady", position, true) or
			not _streamer.call("IsSettled")) and frames < MAX_WAIT_FRAMES:
		await physics_frame
		frames += 1
	return frames < MAX_WAIT_FRAMES and not _streamer.call("HasFailedCells")
