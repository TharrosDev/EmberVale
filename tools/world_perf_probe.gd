extends SceneTree

## Deterministic rendering-cost sample for every region cell.
##
##     Godot_..._console.exe --path . --script res://tools/world_perf_probe.gd
##     ... world_perf_probe.gd -- --region ember_crown              one realm (repeatable)
##     ... world_perf_probe.gd -- --cell crossway_post              cells whose id contains this (repeatable)
##     ... world_perf_probe.gd -- --top 10                          list the ten slowest cells (default 5)
##     ... world_perf_probe.gd -- --json-file PATH                  write the report; samples go to PATH's .samples.json
##     ... world_perf_probe.gd -- --json                            print the report as one JSON line instead
##
## It prints one line per region and the slowest cells, never the whole table: the table is the JSON.
## Compare two revisions with `python tools/perf_compare.py PATH` (per-cell keys, machine-keyed
## baseline, `--update` to record one). Exit 0, 1 when a region or cell failed to stream or the report
## could not be written, 2 on a bad argument or a filter that matched nothing, 4 on a headless display.
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
## Draw calls and primitives averaged, and frame time as the median and the worst, over
## `SAMPLE_FRAMES` after a `WARMUP_FRAMES` settle so shader compilation and the first frame's uploads
## are not in it. The camera looks along the ground rather than down at it, because a top-down shot
## of a cell renders a fraction of what a player standing in it does.
##
## ⚠️ FRAME TIME IS THE WALL-CLOCK TIME BETWEEN FRAMES, NOT A MONITOR. It used to be derived from
## Performance.TIME_FPS, which the engine refreshes once a second: forty reads in half a second were
## one stale number that still held the warm-up and the previous cell. Numbers from before that fix
## are not comparable with numbers after it.
##
## ⚠️ THIS IS NOT world_shots.gd AND MUST NOT BECOME IT. That harness writes PNGs synchronously, which
## deliberately blocks frames; any frame time measured while it runs is a measurement of file I/O.

const REGIONS := [
	"res://data/regions/EmberCrown.tres",
	"res://data/regions/FrostfangReach.tres",
	"res://data/regions/AshenWilds.tres",
	"res://data/regions/Sunspire.tres",
	"res://data/regions/PaleConcord.tres",
	"res://data/regions/CelestialRealm.tres",
]
const WARMUP_FRAMES := 24
const REGION_WARMUP_FRAMES := 180
const SAMPLE_FRAMES := 40
const EYE_HEIGHT := 1.7

var _camera: Camera3D
var _content_loader: Node
var _rows: Array = []
var _json := false
var _json_path := ""
var _region_filters: Array[String] = []
var _cell_filters: Array[String] = []
var _top := 5
var _region_id := ""
var _failures: Array[String] = []
var _budget_warnings: Array[String] = []
var _regions := {}
var _streamer: Node3D


func _initialize() -> void:
	if DisplayServer.get_name() == "headless":
		printerr("world perf: a rendering-capable display is required; run without --headless")
		quit(4)
		return
	if not _parse_arguments(OS.get_cmdline_user_args()):
		quit(2)
		return
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


func _parse_arguments(args: PackedStringArray) -> bool:
	var index := 0
	while index < args.size():
		var argument := args[index]
		if argument == "--json":
			_json = true
		elif argument in ["--json-file", "--region", "--cell", "--top"]:
			if index + 1 >= args.size():
				printerr("world perf: %s requires a value" % argument)
				return false
			index += 1
			var value := args[index]
			match argument:
				"--json-file": _json_path = value
				"--region": _region_filters.append(_normalise(value))
				"--cell": _cell_filters.append(_normalise(value))
				"--top": _top = maxi(0, int(value))
		else:
			printerr("world perf: unknown argument %s" % argument)
			return false
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


## True when no filter was given, or any of `names` contains any filter once both are normalised.
func _wanted(filters: Array[String], names: Array) -> bool:
	if filters.is_empty():
		return true
	for wanted in filters:
		for candidate in names:
			if _normalise(String(candidate)).contains(wanted):
				return true
	return false


func _run() -> void:
	var streamer_script: Script = load("res://src/World/RegionStreamer.cs")
	_streamer = streamer_script.new()
	root.add_child(_streamer)
	_streamer.call("SetPerformanceSamplingEnabled", false)

	for region_path in REGIONS:
		var region: Resource = load(region_path)
		if not _wanted(_region_filters, [String(region.get("Id")), String(region_path).get_file().get_basename()]):
			continue
		var configure_started := Time.get_ticks_usec()
		_streamer.call("Configure", region)
		var settle := 0
		while not _streamer.call("IsSettled") and settle < 900:
			await process_frame
			settle += 1
		var configure_ms := (Time.get_ticks_usec() - configure_started) / 1000.0
		if not _streamer.call("IsSettled") or _streamer.call("HasFailedCells"):
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
		var cell_ms := {}
		var cell_draws := {}
		var cell_prims := {}

		for authored_cell in region.get("Cells"):
			if authored_cell == null or authored_cell.get("Presentation") == null:
				continue
			if not _wanted(_cell_filters, [String(authored_cell.get("Id"))]):
				continue
			var sample := await _sample_cell(authored_cell)
			_rows.append(sample)
			cell_ms[sample.cell] = snappedf(sample.ms, 0.01)
			cell_draws[sample.cell] = roundf(sample.draws)
			cell_prims[sample.cell] = roundf(sample.prims)
			totals.draws += sample.draws
			totals.prims += sample.prims
			totals.ms += sample.ms
			totals.cells += 1.0
			if worst.is_empty() or sample.ms > worst.ms:
				worst = sample

		if totals.cells > 0.0:
			var memory := Performance.get_monitor(Performance.RENDER_VIDEO_MEM_USED) / 1048576.0
			# The split of that total, read once per region beside it (three counter reads, no frame
			# is sampled around them): textures are what an asset change moves, buffers are meshes,
			# multimeshes and skeletons. The remainder of the total is the renderer's own targets.
			var texture_memory := RenderingServer.get_rendering_info(
				RenderingServer.RENDERING_INFO_TEXTURE_MEM_USED) / 1048576.0
			var buffer_memory := RenderingServer.get_rendering_info(
				RenderingServer.RENDERING_INFO_BUFFER_MEM_USED) / 1048576.0
			_regions[_region_id] = {
				"cell_count": int(totals.cells),
				"configure_ms": snappedf(configure_ms, 0.1),
				"mean_draws": snappedf(totals.draws / totals.cells, 0.1),
				"mean_prims": roundf(totals.prims / totals.cells),
				"mean_ms": snappedf(totals.ms / totals.cells, 0.01),
				"worst_cell": worst.cell,
				"worst_ms": snappedf(worst.ms, 0.01),
				"video_memory_mb": snappedf(memory, 0.1),
				"texture_memory_mb": snappedf(texture_memory, 0.1),
				"buffer_memory_mb": snappedf(buffer_memory, 0.1),
				"cell_ms": cell_ms,
				"cell_draws": cell_draws,
				"cell_prims": cell_prims,
			}
			if not _json:
				print("world perf: %s %d cells, mean %.2f ms, %.0f draws, worst %s %.2f ms, video %.0f MB (tex %.0f, buf %.0f), streamed in %.0f ms" % [
					_region_id, int(totals.cells), totals.ms / totals.cells, totals.draws / totals.cells,
					worst.cell, worst.ms, memory, texture_memory, buffer_memory, configure_ms])
			_check_budget(region, totals)

		_streamer.call("UnloadAll")
		_streamer.call("Configure", null)
		await process_frame
		await process_frame

	if _rows.is_empty() and _failures.is_empty():
		printerr("world perf: no cell matched --region %s --cell %s" % [_region_filters, _cell_filters])
		_content_loader.call("CollectManagedResources")
		await process_frame
		quit(2)
		return

	_rows.sort_custom(func(a, b): return a.ms > b.ms)
	var slowest: Array = []
	for row in _rows.slice(0, _top):
		slowest.append({"region": row.region, "cell": row.cell, "ms": snappedf(row.ms, 0.01),
			"max_ms": snappedf(row.max_ms, 0.01), "draws": roundf(row.draws), "prims": roundf(row.prims)})
	var size := DisplayServer.window_get_size()
	var report := {"schema": 2, "suite": "world-perf", "kind": "machine-sensitive-report",
		"engine": String(Engine.get_version_info().string),
		"adapter": RenderingServer.get_video_adapter_name(),
		"renderer": RenderingServer.get_current_rendering_method(),
		"os": OS.get_name(), "resolution": [size.x, size.y],
		"sample_frames": SAMPLE_FRAMES, "timing": "tick-delta",
		"filters": {"region": _region_filters, "cell": _cell_filters},
		"regions": _regions, "slowest": slowest,
		"budget_warnings": _budget_warnings, "failures": _failures}
	if _json:
		print(JSON.stringify(report))
	else:
		for row in slowest:
			print("  slow: %s/%s %.2f ms (max %.2f), %d draws, %d prims" % [
				row.region, row.cell, row.ms, row.max_ms, int(row.draws), int(row.prims)])
		for warning in _budget_warnings:
			print("  budget: %s" % warning)
	if not _json_path.is_empty():
		if _store(_json_path, JSON.stringify(report, "  ")):
			_store(_json_path.get_basename() + ".samples.json", JSON.stringify({"samples": _rows}))
			if not _json:
				print("world perf: wrote %s" % _json_path)
	_content_loader.call("CollectManagedResources")
	await process_frame
	for failure in _failures:
		printerr("world perf: %s" % failure)
	quit(0 if _failures.is_empty() else 1)


func _store(path: String, text: String) -> bool:
	var file := FileAccess.open(path, FileAccess.WRITE)
	if file == null:
		_failures.append("could not write %s: %s" % [path, FileAccess.get_open_error()])
		return false
	file.store_string(text)
	return true


## ⚠️ A WARNING, NOT A FAILURE, AND DELIBERATELY SO. A budget overrun on this machine is a fact about
## this machine; the gate that can fail a build is --validate's authored-node and scatter budget,
## which is deterministic. This one is here to be READ, and it is in the report in every mode.
## (Video memory is not compared: the budget's memory figure is static memory, a different quantity.)
func _check_budget(region: Resource, totals: Dictionary) -> void:
	var budget: Resource = region.get("PerformanceBudget")
	if budget == null:
		return
	var mean_draws: float = totals.draws / totals.cells
	var mean_ms: float = totals.ms / totals.cells
	var max_draws: float = float(budget.get("MaxDrawCalls"))
	var max_ms: float = float(budget.get("MaxFrameMilliseconds"))
	if mean_draws > max_draws:
		_budget_warnings.append("%s mean draw calls %.0f over the region budget of %.0f" % [
			_region_id, mean_draws, max_draws])
	if mean_ms > max_ms:
		_budget_warnings.append("%s mean frame time %.2f ms over the region budget of %.2f" % [
			_region_id, mean_ms, max_ms])


func _sample_cell(authored_cell: Resource) -> Dictionary:
	var centre: Vector3 = authored_cell.get("Center")
	_streamer.call("SetStreamingFocus", centre)
	var settle := 0
	while (not _streamer.call("IsPositionReady", centre, false) or
			not _streamer.call("IsSettled")) and settle < 900:
		await process_frame
		settle += 1
	if settle >= 900:
		_failures.append("cell failed to activate for sample: %s" % authored_cell.get("Id"))
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
	var before := Time.get_ticks_usec()
	for _frame in SAMPLE_FRAMES:
		await process_frame
		# ⚠️ THE TIME BETWEEN TWO FRAMES, measured here. Not TIME_PROCESS (the script's slice, near
		# zero in a probe that does nothing per frame) and not TIME_FPS (refreshed once a second).
		# Vsync is disabled in _initialize() so this is not the monitor's refresh interval.
		var now := Time.get_ticks_usec()
		frame_times.append((now - before) / 1000.0)
		before = now
		draws += Performance.get_monitor(Performance.RENDER_TOTAL_DRAW_CALLS_IN_FRAME)
		prims += Performance.get_monitor(Performance.RENDER_TOTAL_PRIMITIVES_IN_FRAME)

	return {
		"region": _region_id,
		"cell": String(authored_cell.get("Id")),
		"draws": draws / SAMPLE_FRAMES,
		"prims": prims / SAMPLE_FRAMES,
		"ms": _median(frame_times),
		"max_ms": frame_times.max(),
	}


## ⚠️ THE MEDIAN, NOT THE MEAN. This runs on a laptop with an integrated GPU: a background thread,
## a thermal step or the navmesh baker finishing on a worker will put one 300 ms frame in a
## forty-frame window, and a mean built from that reports a cell as twelve times its own cost. The
## median is the frame the player actually gets, and it is stable enough to compare two revisions.
## (The worst frame of the window is reported beside it as max_ms.)
func _median(values: Array[float]) -> float:
	if values.is_empty():
		return 0.0
	var ordered := values.duplicate()
	ordered.sort()
	return ordered[ordered.size() / 2]


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
