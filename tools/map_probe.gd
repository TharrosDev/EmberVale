extends SceneTree

## Phase 39.5A — the map-location placement gate.
##
##     godot --headless --path . --script res://tools/map_probe.gd     # exit 0 pass / 1 fail
##
## `--validate` proves every LocationId a scene names exists, and that every authored location is
## placed somewhere. It cannot prove the thing that actually matters about a marker: THAT IT SITS
## SOMEWHERE SENSIBLE. A MapLocationComponent parented to the wrong node, or left at its parent's
## origin when the parent is the cell root, resolves perfectly and validates clean — and puts the
## blacksmith's pin on top of the town's. That is invisible in every static check and, on a map the
## player has not discovered yet, invisible in play too.
##
## So this instantiates each cell scene for real, reads each marker's actual transform, offsets it by
## its cell's authored Center to get the world position the running game will register, and asserts:
##
##   1. every marker names a location that exists in data/map_locations/
##   2. no two markers in a cell resolve to the same spot (the copy-paste failure)
##   3. every marker except a cell-root one is off its cell's centre (the wrong-parent failure)
##
## ⚠️ IT AWAITS TWO FRAMES BEFORE READING ANY TRANSFORM. `global_transform` returns IDENTITY, with an
## error, for a node added during `_initialize` — and it reports the raw bind-pose confidently enough
## that the run looks like it worked (invariant 19, learned the expensive way in 39A).

const LOCATIONS_DIR := "res://data/map_locations"
const REGIONS_DIR := "res://data/regions"
const SCENES_DIR := "res://scenes/regions"

var _failures: Array[String] = []


func _initialize() -> void:
	_run.call_deferred()


func _run() -> void:
	await process_frame
	await process_frame

	var known := _authored_location_ids()
	print("map_probe: %d authored location(s) in %s" % [known.size(), LOCATIONS_DIR])

	var centres := _cell_centres()
	print("map_probe: %d cell centre(s) from %s" % [centres.size(), REGIONS_DIR])

	var placed := {}
	var total := 0

	for scene_path in _scene_paths(SCENES_DIR):
		var cell_id: String = _cell_id_for(scene_path, centres)
		var centre: Vector3 = centres.get(cell_id, Vector3.ZERO)

		var packed: PackedScene = load(scene_path)
		if packed == null:
			_fail("could not load %s" % scene_path)
			continue

		var root: Node = packed.instantiate()
		if root is Node3D:
			# Exactly what RegionStreamer does before AddChild, so the positions read below are the
			# ones the running game registers rather than cell-local ones.
			(root as Node3D).position = centre
		root_node().add_child(root)

		await process_frame
		await process_frame

		var found := _collect(root)
		var seen := {}
		for entry in found:
			total += 1
			var id: String = entry["id"]
			var at: Vector3 = entry["at"]
			var path: String = entry["path"]

			if id.is_empty():
				_fail("%s: a MapLocationComponent at '%s' has no LocationId" % [cell_id, path])
			elif not known.has(id):
				_fail("%s: '%s' is not an authored map location" % [cell_id, id])

			var key := "%.2f,%.2f" % [at.x, at.z]
			if seen.has(key):
				_fail("%s: '%s' and '%s' both resolve to (%.2f, %.2f) — one of them is parented to "
					% [cell_id, id, seen[key], at.x, at.z]
					+ "the wrong node")
			seen[key] = id

			# A cell-root marker IS the cell centre; anything else sitting exactly on it means the
			# component was parented to the cell root by mistake rather than to the thing it names.
			if path != "." and at.distance_to(centre) < 0.01:
				_fail("%s: '%s' sits exactly on the cell centre but is parented to '%s' — expected "
					% [cell_id, id, path] + "an offset from the thing it marks")

			placed[id] = true
			print("    %-40s %-26s (%7.1f, %7.1f)" % [id, path, at.x, at.z])

		root.queue_free()

	for id in known:
		if not placed.has(id):
			_fail("'%s' is authored but no cell scene places it" % id)

	print("\nmap_probe: %d marker(s) across %d cell(s)" % [total, centres.size()])
	if _failures.is_empty():
		print("map_probe: PASS")
		quit(0)
	else:
		for f in _failures:
			printerr("map_probe: FAIL — %s" % f)
		print("map_probe: FAIL (%d)" % _failures.size())
		quit(1)


func root_node() -> Node:
	return get_root()


func _fail(message: String) -> void:
	_failures.append(message)


## Every MapLocationComponent under a cell root, as {id, at, path}. `path` is the parent's name so a
## failure names the node an author has to go and look at.
func _collect(node: Node, cell_root: Node = null, out: Array = []) -> Array:
	var root: Node = cell_root if cell_root != null else node
	for child in node.get_children():
		var id: Variant = child.get("LocationId")
		if id != null and child is Node3D:
			var parent: Node = child.get_parent()
			var label: String = "." if parent == root else String(parent.name)
			out.append({
				"id": String(id),
				"at": (child as Node3D).global_position,
				"path": label,
			})
		_collect(child, root, out)
	return out


func _authored_location_ids() -> Dictionary:
	var ids := {}
	var dir := DirAccess.open(LOCATIONS_DIR)
	if dir == null:
		_fail("cannot open %s" % LOCATIONS_DIR)
		return ids
	for file in dir.get_files():
		var name := file.trim_suffix(".remap")
		if not name.ends_with(".tres"):
			continue
		var text := FileAccess.get_file_as_string("%s/%s" % [LOCATIONS_DIR, name])
		var m := RegEx.create_from_string('Id = "([^"]+)"').search(text)
		if m:
			ids[m.get_string(1)] = true
	return ids


## Cell id -> Center, parsed straight out of the region .tres so this cannot drift from the data the
## streamer reads.
func _cell_centres() -> Dictionary:
	var centres := {}
	var dir := DirAccess.open(REGIONS_DIR)
	if dir == null:
		_fail("cannot open %s" % REGIONS_DIR)
		return centres
	# ⚠️ Excludes region.* deliberately: a RegionResource carries an Id in the same shape as its
	# cells, so a bare match counts the two regions as cells and reports 17 where there are 15.
	var id_rx := RegEx.create_from_string('^Id = "(?!region\\.)([a-z_]+\\.[a-z_]+)"')
	# ⚠️ Anchored, because a REGION carries SafeZoneCenter — an unanchored "Center =" matches inside
	# it and pairs a bogus centre with whatever cell id was last seen, reporting 16 cells where the
	# realm has 15.
	var centre_rx := RegEx.create_from_string("^Center = Vector3\\(([^)]*)\\)")
	for file in dir.get_files():
		var name := file.trim_suffix(".remap")
		if not name.ends_with(".tres"):
			continue
		var text := FileAccess.get_file_as_string("%s/%s" % [REGIONS_DIR, name])
		# Sub-resource blocks are emitted in order, each with its Id then its Center.
		var pending := ""
		for line in text.split("\n"):
			var im := id_rx.search(line)
			if im:
				pending = im.get_string(1)
				continue
			var cm := centre_rx.search(line)
			if cm and not pending.is_empty():
				var parts := cm.get_string(1).split(",")
				if parts.size() == 3:
					centres[pending] = Vector3(
						float(parts[0]), float(parts[1]), float(parts[2]))
				pending = ""
	return centres


func _cell_id_for(scene_path: String, centres: Dictionary) -> String:
	var stem := scene_path.get_file().trim_suffix(".tscn")
	for id in centres:
		if String(id).ends_with("." + stem):
			return id
	return stem


func _scene_paths(directory: String) -> Array:
	var out := []
	var dir := DirAccess.open(directory)
	if dir == null:
		return out
	for file in dir.get_files():
		if file.ends_with(".tscn"):
			out.append("%s/%s" % [directory, file])
	for sub in dir.get_directories():
		out.append_array(_scene_paths("%s/%s" % [directory, sub]))
	out.sort()
	return out
