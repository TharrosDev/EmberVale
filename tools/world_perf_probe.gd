extends SceneTree

## Deterministic rendering-cost sample for every region cell.
##
##     Godot_..._console.exe --path . --script res://tools/world_perf_probe.gd
##     Godot_..._console.exe --path . --script res://tools/world_perf_probe.gd -- --json
##
## WHY THIS EXISTS
## ---------------
## `--validate` gates AUTHORED node counts and REQUESTED scatter instances — numbers from the files.
## `cell_mesh_census.gd` counts the meshes a cell instantiates. Neither of them is a cost: a terrain
## cell is one draw call whatever its resolution, a MultiMesh of eight thousand grass tufts is one
## draw call, and a shader with six blended layers in it is free in both of those censuses and is not
## free on a GPU. Until this, the only way to find out what the world actually costs to draw was to
## play it and watch the F4 overlay, and the only way to compare two revisions was to remember.
##
## ⚠️ IT PARKS A CAMERA AT PLAYER EYE HEIGHT IN EVERY CELL AND SAMPLES THE ENGINE'S OWN COUNTERS.
## Draw calls, primitives, video memory and frame time, averaged over `SAMPLE_FRAMES` after a
## `WARMUP_FRAMES` settle so shader compilation and the first frame's uploads are not in the average.
## The camera looks along the ground rather than down at it, because a top-down shot of a cell renders
## a fraction of what a player standing in it does.
##
## ⚠️ THIS IS NOT world_shots.gd AND MUST NOT BECOME IT. That harness writes PNGs synchronously, which
## deliberately blocks frames; any frame time measured while it runs is a measurement of file I/O.

const REGIONS := [
	"res://data/regions/EmberCrown.tres",
	"res://data/regions/FrostfangReach.tres",
]
const WARMUP_FRAMES := 24
const REGION_WARMUP_FRAMES := 180
const SAMPLE_FRAMES := 40
const EYE_HEIGHT := 1.7
const BASELINE_PATH := "res://tests/performance_baselines/world_performance.json"

var _camera: Camera3D
var _content_loader: Node
var _rows: Array = []
var _json := false
var _json_path := ""
var _region_id := ""
var _failures: Array[String] = []
var _summaries: Array = []


func _initialize() -> void:
	if DisplayServer.get_name() == "headless":
		printerr("world perf: a rendering-capable display is required; run without --headless")
		quit(4)
		return
	var args := OS.get_cmdline_user_args()
	_json = "--json" in args or "--json-file" in args
	var path_index := args.find("--json-file")
	if path_index >= 0:
		if path_index + 1 >= args.size():
			printerr("world perf: --json-file requires a path")
			quit(2)
			return
		_json_path = args[path_index + 1]
	seed(0x50455246454d4245)
	DisplayServer.window_set_vsync_mode(DisplayServer.VSYNC_DISABLED)

	var loader_script: Script = load("res://src/Bootstrap/ContentDatabaseLoader.cs")
	_content_loader = loader_script.new()
	root.add_child(_content_loader)

	_build_light()
	_camera = Camera3D.new()
	_camera.fov = 70
	_camera.far = 900.0
	_camera.current = true
	root.add_child(_camera)
	_run.call_deferred()


func _run() -> void:
	var streamer_script: Script = load("res://src/World/RegionStreamer.cs")
	var streamer: Node3D = streamer_script.new()
	root.add_child(streamer)
	streamer.call("SetPerformanceSamplingEnabled", false)

	for region_path in REGIONS:
		var region: Resource = load(region_path)
		var configure_started := Time.get_ticks_usec()
		streamer.call("Configure", region)
		var settle := 0
		while not streamer.call("IsSettled") and settle < 900:
			await process_frame
			settle += 1
		var configure_ms := (Time.get_ticks_usec() - configure_started) / 1000.0
		if not streamer.call("IsSettled") or streamer.call("HasFailedCells"):
			_failures.append("region failed to settle: %s (%d frames)" % [region_path, settle])
			continue
		# ⚠️ A REGION-WIDE WARM-UP BEFORE THE FIRST CELL, not just a per-cell one. Every material in
		# the region compiles its pipeline on the frame it is first drawn, and the navmesh bakes on
		# worker threads for a second or two after IsSettled; without this the FIRST cell sampled
		# came back at 76 ms and the second at 88, which is a measurement of Vulkan and of Recast
		# rather than of the world. The rest of the region then sat at 10-16.
		for _settle_frame in REGION_WARMUP_FRAMES:
			await process_frame
		_region_id = String(region.get("Id"))
		var worst := {}
		var totals := {"draws": 0.0, "prims": 0.0, "ms": 0.0, "cells": 0.0}

		for authored_cell in region.get("Cells"):
			if authored_cell == null or authored_cell.get("Presentation") == null:
				continue
			var sample := await _sample_cell(authored_cell)
			_rows.append(sample)
			totals.draws += sample.draws
			totals.prims += sample.prims
			totals.ms += sample.ms
			totals.cells += 1.0
			if worst.is_empty() or sample.ms > worst.ms:
				worst = sample

		var memory := Performance.get_monitor(Performance.RENDER_VIDEO_MEM_USED) / 1048576.0
		_summaries.append({
			"region": _region_id,
			"cells": int(totals.cells),
			"configure_ms": configure_ms,
			"mean_draws": totals.draws / totals.cells,
			"mean_prims": totals.prims / totals.cells,
			"mean_ms": totals.ms / totals.cells,
			"worst_cell": worst.cell,
			"worst_ms": worst.ms,
			"video_memory_mb": memory,
		})
		if not _json:
			print("")
			print("%s  (%d cells, streamed+built in %.0f ms)" % [
				_region_id, int(totals.cells), configure_ms])
			print("  %-34s %8s %10s %8s" % ["cell", "draws", "prims", "ms/frame"])
			for row in _rows:
				if row.region == _region_id:
					print("  %-34s %8d %10d %8.2f" % [row.cell, row.draws, row.prims, row.ms])
			print("  %-34s %8.0f %10.0f %8.2f" % [
				"MEAN", totals.draws / totals.cells, totals.prims / totals.cells,
				totals.ms / totals.cells])
			print("  worst cell: %s at %.2f ms/frame" % [worst.cell, worst.ms])
			print("  resident video memory: %.0f MB" % memory)
			_check_budget(region, totals, memory)

		streamer.call("UnloadAll")
		streamer.call("Configure", null)
		await process_frame
		await process_frame

	if _json:
		var historical = null
		var comparisons: Array = []
		if FileAccess.file_exists(BASELINE_PATH):
			historical = JSON.parse_string(FileAccess.get_file_as_string(BASELINE_PATH))
			if historical is Dictionary and historical.has("regions"):
				for current in _summaries:
					var old = historical.regions.get(current.region)
					if old is Dictionary:
						comparisons.append({"region": current.region,
							"mean_draws_percent": _percent_delta(current.mean_draws, old.mean_draws),
							"mean_prims_percent": _percent_delta(current.mean_prims, old.mean_prims),
							"mean_ms_percent": _percent_delta(current.mean_ms, old.mean_ms),
							"worst_ms_percent": _percent_delta(current.worst_ms, old.worst_ms),
							"video_memory_percent": _percent_delta(current.video_memory_mb, old.video_memory_mb),
							"configure_ms_percent": _percent_delta(current.configure_ms, old.configure_ms)})
		var report := {"schema": 1, "kind": "machine-sensitive-report",
			"engine": Engine.get_version_info(), "renderer": RenderingServer.get_video_adapter_name(),
			"os": OS.get_name(), "summaries": _summaries, "samples": _rows,
			"historical_baseline": historical, "historical_comparisons": comparisons,
			"failures": _failures}
		var encoded := JSON.stringify(report, "  ")
		if _json_path.is_empty():
			print(encoded)
		else:
			var file := FileAccess.open(_json_path, FileAccess.WRITE)
			if file == null:
				_failures.append("could not write JSON report %s: %s" % [_json_path, FileAccess.get_open_error()])
			else:
				file.store_string(encoded)
	_content_loader.call("CollectManagedResources")
	await process_frame
	for failure in _failures:
		printerr("world perf: %s" % failure)
	quit(0 if _failures.is_empty() else 1)


func _percent_delta(current: float, baseline: float) -> float:
	return 0.0 if is_zero_approx(baseline) else ((current - baseline) / baseline) * 100.0


## ⚠️ A WARNING, NOT A FAILURE, AND DELIBERATELY SO. A budget overrun on this machine is a fact about
## this machine; the gate that can fail a build is --validate's authored-node and scatter budget,
## which is deterministic. This one is here to be READ.
func _check_budget(region: Resource, totals: Dictionary, memory: float) -> void:
	var budget: Resource = region.get("PerformanceBudget")
	if budget == null:
		return
	var mean_draws: float = totals.draws / totals.cells
	var max_draws: float = float(budget.get("MaxDrawCalls"))
	var max_ms: float = float(budget.get("MaxFrameMilliseconds"))
	var max_memory: float = float(budget.get("MaxStaticMemoryMb"))
	var worst_ms: float = totals.ms / totals.cells
	if mean_draws > max_draws:
		print("  ⚠️ mean draw calls %.0f over the region budget of %.0f" % [mean_draws, max_draws])
	if worst_ms > max_ms:
		print("  ⚠️ mean frame time %.2f ms over the region budget of %.2f" % [worst_ms, max_ms])
	if memory > max_memory:
		print("  ⚠️ video memory %.0f MB over the region budget of %.0f" % [memory, max_memory])


func _sample_cell(authored_cell: Resource) -> Dictionary:
	var centre: Vector3 = authored_cell.get("Center")
	var presentation: Resource = authored_cell.get("Presentation")
	# ⚠️ PER-AXIS, NOT max(width, depth). A single reach put the camera for the 170 x 80 frost_march_w
	# nineteen metres OUTSIDE the region, standing on the backdrop looking in with nothing occluding
	# anything — which reported that cell at 250 ms while its neighbours sat at 12. A probe that can
	# leave the playable area is measuring a view no player will ever have.
	var reach_x: float = float(presentation.get("Width")) * 0.35
	var reach_z: float = float(presentation.get("Depth")) * 0.35
	# Stand back from the centre and look ACROSS the cell, which is the view a player has and the
	# one that renders the most: a camera at the centre looking down sees a fifth of the geometry.
	var eye := Vector3(centre.x - reach_x, 0.0, centre.z + reach_z)
	eye.y = _ground_at(eye.x, eye.z) + EYE_HEIGHT
	var target := Vector3(centre.x + reach_x, _ground_at(centre.x + reach_x, centre.z - reach_z) + 1.2,
		centre.z - reach_z)
	_camera.global_position = eye
	_camera.look_at(target, Vector3.UP)

	for _warm in WARMUP_FRAMES:
		await process_frame

	var draws := 0.0
	var prims := 0.0
	var frame_times: Array[float] = []
	for _frame in SAMPLE_FRAMES:
		await process_frame
		draws += Performance.get_monitor(Performance.RENDER_TOTAL_DRAW_CALLS_IN_FRAME)
		prims += Performance.get_monitor(Performance.RENDER_TOTAL_PRIMITIVES_IN_FRAME)
		# ⚠️ FROM FPS, NOT FROM TIME_PROCESS. TIME_PROCESS is the SCRIPT's slice of the frame and is
		# near zero here — this harness does nothing per frame — so averaging it measures the probe
		# rather than the world. Frame time from FPS is the whole cost, and vsync is disabled in
		# _initialize() so it is not simply the monitor's refresh rate reported back.
		var fps: float = Performance.get_monitor(Performance.TIME_FPS)
		frame_times.append(1000.0 / max(1.0, fps))

	return {
		"region": _region_id,
		"cell": String(authored_cell.get("Id")),
		"draws": draws / SAMPLE_FRAMES,
		"prims": prims / SAMPLE_FRAMES,
		"ms": _median(frame_times),
	}


## ⚠️ THE MEDIAN, NOT THE MEAN. This runs on a laptop with an integrated GPU: a background thread,
## a thermal step or the navmesh baker finishing on a worker will put one 300 ms frame in a
## forty-frame window, and a mean built from that reports a cell as twelve times its own cost. The
## median is the frame the player actually gets, and it is stable enough to compare two revisions.
func _median(values: Array[float]) -> float:
	if values.is_empty():
		return 0.0
	values.sort()
	return values[values.size() / 2]


func _ground_at(x: float, z: float) -> float:
	var space := root.world_3d.direct_space_state
	var query := PhysicsRayQueryParameters3D.create(Vector3(x, 400.0, z), Vector3(x, -200.0, z))
	query.collide_with_areas = false
	var hit := space.intersect_ray(query)
	if not hit.has("position"):
		_failures.append("no collision under sample x=%.2f z=%.2f" % [x, z])
		return 0.0
	return hit.position.y


func _build_light() -> void:
	var world_env := WorldEnvironment.new()
	var environment := Environment.new()
	var sky := Sky.new()
	var material := ProceduralSkyMaterial.new()
	sky.sky_material = material
	environment.background_mode = Environment.BG_SKY
	environment.sky = sky
	environment.ambient_light_source = Environment.AMBIENT_SOURCE_SKY
	environment.fog_enabled = true
	environment.fog_density = 0.004
	world_env.environment = environment
	root.add_child(world_env)

	var sun := DirectionalLight3D.new()
	sun.rotation_degrees = Vector3(-42, 38, 0)
	sun.light_energy = 1.2
	sun.shadow_enabled = true
	root.add_child(sun)
