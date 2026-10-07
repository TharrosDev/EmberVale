# Counts the RENDERED meshes each region cell actually instantiates, deterministically.
#
#     godot --headless --path . --script res://tools/cell_mesh_census.gd
#     godot --headless --path . --script res://tools/cell_mesh_census.gd -- --region ember_crown --table
#     godot --headless --path . --script res://tools/cell_mesh_census.gd -- --update-baseline
#     godot --headless --path . --script res://tools/cell_mesh_census.gd -- --strict
#
# Why this exists
# ---------------
# `--validate` counts AUTHORED nodes — the `[node ...]` blocks in the `.tscn`. That is the number
# the per-cell budget is written against, and it is not what the GPU sees: one authored node that
# instances a modular building brings a whole subtree of MeshInstance3Ds with it, so a cell can shed
# authored nodes and gain draw calls at the same time. The `--play` draw-call warning is real but it
# moves 1,000 either way between runs depending on where the camera happens to look and what the
# EncounterDirector spawned, which makes it useless for answering "did this change make it worse".
#
# This instantiates every cell of every region (all of data/regions, or `--region a,b`) and counts
# the MeshInstance3D and MultiMeshInstance3D descendants, which is stable across runs and directly
# comparable between two commits.
#
# Output
# ------
# One line per region, then `grand total N meshes`, then the shared `PROBE {json}` line. With a
# baseline (tests/performance_baselines/cell_mesh_census.json, or `--baseline PATH`) it also prints
# one line for every cell whose mesh count differs from it:
#     ember_crown.tarn_east 412 -> 455 (+43)
# `--table` prints every cell, as this tool always used to. `--json-file PATH` writes the whole
# census {region: {cell: {meshes, multimeshes, instances}}}; under the SDK it is written to
# $EMBERVALE_ARTIFACTS/cell_mesh_census.json without being asked.
#
# Exit code
# ---------
# 0 unless a region or scene fails to load. That makes it a report, which is what it has always
# been. `--strict` turns it into a gate: exit 1 when any cell has more than baseline x (1 +
# `--tolerance`, default 0.25) meshes. There is no absolute per-cell mesh budget in the validator to
# borrow, so the budget is relative to a committed baseline: write one with `--update-baseline`
# after a change that is meant to add meshes, and review its diff like any other file.
extends "res://tools/probe_base.gd"

const DEFAULT_BASELINE := "res://tests/performance_baselines/cell_mesh_census.json"


func _initialize() -> void:
	var census := {}
	var totals := {}
	for region_path in regions():
		var region: Resource = load(region_path)
		if region == null:
			printerr("cell mesh census: could not load %s" % region_path)
			fail("could not load %s" % region_path)
			continue
		var cells := {}
		var region_total := 0
		for cell in region.Cells:
			var packed: PackedScene = load(cell.ScenePath)
			if packed == null:
				printerr("  %s: scene did not load" % cell.Id)
				fail("%s: scene did not load" % cell.Id)
				continue
			var instance: Node = packed.instantiate()
			var counts := {"meshes": 0, "multimeshes": 0, "instances": 0}
			_count(instance, counts)
			instance.free()
			cells[str(cell.Id)] = counts
			region_total += int(counts["meshes"])
		census[str(region.Id)] = cells
		totals[str(region.Id)] = region_total

	var baseline_path := arg("--baseline", DEFAULT_BASELINE)
	var baseline := _read_json(baseline_path)
	var tolerance := arg("--tolerance", "0.25").to_float()
	var strict := has_arg("--strict")
	var changed := 0
	var over: Array[String] = []
	var grand_total := 0
	for region_id in census:
		var cells: Dictionary = census[region_id]
		var known: Dictionary = baseline.get(region_id, {})
		var baseline_total := 0
		var lines: Array[String] = []
		for cell_id in cells:
			var meshes := int(cells[cell_id]["meshes"])
			if has_arg("--table"):
				lines.append("  %-38s %5d meshes" % [cell_id, meshes])
			if not known.has(cell_id):
				if not baseline.is_empty():
					changed += 1
					lines.append("  %s NEW %d" % [cell_id, meshes])
				continue
			var before := int(known[cell_id]["meshes"])
			baseline_total += before
			if meshes != before:
				changed += 1
				lines.append("  %s %d -> %d (%+d)" % [cell_id, before, meshes, meshes - before])
			var allowed := before * (1.0 + tolerance)
			if meshes > allowed:
				over.append("%s has %d meshes, over its baseline %d by more than %d%%"
					% [cell_id, meshes, before, roundi(tolerance * 100.0)])
			if strict:
				check(meshes <= allowed, over[-1] if meshes > allowed else "")
		for cell_id in known:
			if not cells.has(cell_id):
				changed += 1
				lines.append("  %s REMOVED (was %d)" % [cell_id, int(known[cell_id]["meshes"])])
		var against := ""
		if not known.is_empty():
			against = "  (baseline %d, %+d)" % [baseline_total, int(totals[region_id]) - baseline_total]
		print("%s  %d cells  %d meshes%s" % [region_id, cells.size(), int(totals[region_id]), against])
		for line in lines:
			print(line)
		grand_total += int(totals[region_id])
	print("grand total %d meshes" % grand_total)
	if baseline.is_empty():
		print("no baseline at %s: nothing to compare (write one with -- --update-baseline)" % baseline_path)
		if strict and not has_arg("--update-baseline"):
			fail("--strict needs a baseline at %s" % baseline_path)
	elif not strict:
		for line in over:
			print("OVER: %s" % line)

	metric("grand_total", grand_total)
	metric("regions", totals)
	metric("changed_cells", changed)
	metric("over_budget", over)
	metric("baseline", not baseline.is_empty())

	var json_file := arg("--json-file")
	var artifacts := OS.get_environment("EMBERVALE_ARTIFACTS")
	if json_file.is_empty() and not artifacts.is_empty():
		json_file = artifacts.path_join("cell_mesh_census.json")
	if not json_file.is_empty():
		write_text(json_file, JSON.stringify(census, "  ") + "\n")
	if has_arg("--update-baseline") and failure_count() == 0:
		# A run narrowed by --region replaces only the regions it measured.
		for region_id in census:
			baseline[region_id] = census[region_id]
		if write_text(baseline_path, JSON.stringify(baseline, "  ") + "\n"):
			print("baseline written: %s" % baseline_path)
	finish("", strict)


func _read_json(path: String) -> Dictionary:
	var target := resolve_path(path)
	if not FileAccess.file_exists(target):
		return {}
	var parsed: Variant = JSON.parse_string(FileAccess.get_file_as_string(target))
	if parsed is Dictionary:
		return parsed
	printerr("cell mesh census: %s is not a JSON object; ignoring it" % path)
	return {}


func _count(node: Node, counts: Dictionary) -> void:
	if node is MeshInstance3D:
		counts["meshes"] += 1
	elif node is MultiMeshInstance3D:
		counts["multimeshes"] += 1
		var multimesh: MultiMesh = (node as MultiMeshInstance3D).multimesh
		if multimesh != null:
			counts["instances"] += multimesh.instance_count
	for child in node.get_children():
		_count(child, counts)
