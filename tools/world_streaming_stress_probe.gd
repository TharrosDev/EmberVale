extends SceneTree

## Production streaming stress: rapid traversal, distant cycling, boundary oscillation and teardown.
## It requires prepared collision/navigation before accepting each focus and checks that unloading
## returns resident cells and node counts instead of growing them over repeated passes.

const REGIONS := [
	"res://data/regions/EmberCrown.tres",
	"res://data/regions/FrostfangReach.tres",
]
const MAX_WAIT_FRAMES := 900

var _failures: Array[String] = []
var _streamer: Node3D


func _initialize() -> void:
	root.add_child(load("res://src/Bootstrap/ContentDatabaseLoader.cs").new())
	_run.call_deferred()


func _run() -> void:
	await process_frame
	_streamer = load("res://src/World/RegionStreamer.cs").new()
	root.add_child(_streamer)
	_streamer.call("SetPerformanceSamplingEnabled", false)
	var baseline_nodes := int(Performance.get_monitor(Performance.OBJECT_NODE_COUNT))
	var worst_activation_ms := 0.0

	for region_path in REGIONS:
		var region: Resource = load(region_path)
		_streamer.call("Configure", region)
		for cycle in 2:
			for cell in region.get("Cells"):
				var centre: Vector3 = cell.get("Center")
				var started := Time.get_ticks_usec()
				_streamer.call("SetStreamingFocus", centre)
				if not await _wait_ready(centre):
					_failures.append("%s failed rapid-traversal activation" % cell.get("Id"))
					continue
				worst_activation_ms = max(worst_activation_ms,
					(Time.get_ticks_usec() - started) / 1000.0)

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

	var ending_nodes := int(Performance.get_monitor(Performance.OBJECT_NODE_COUNT))
	if ending_nodes > baseline_nodes + 12:
		_failures.append("node growth after soak: %d -> %d" % [baseline_nodes, ending_nodes])

	if _failures.is_empty():
		print("world streaming stress: PASS (rapid traversal, teleport cycling, boundary oscillation, " +
			"collision/nav gating, unload soak; worst activation %.1f ms)" % worst_activation_ms)
		quit(0)
	else:
		for failure in _failures:
			printerr("world streaming stress: FAIL — %s" % failure)
		quit(1)


func _wait_ready(position: Vector3) -> bool:
	var frames := 0
	while (not _streamer.call("IsPositionReady", position, true) or
			not _streamer.call("IsSettled")) and frames < MAX_WAIT_FRAMES:
		await physics_frame
		frames += 1
	return frames < MAX_WAIT_FRAMES and not _streamer.call("HasFailedCells")
