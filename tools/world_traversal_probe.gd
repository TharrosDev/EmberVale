extends SceneTree

## In-engine traversal gate for every authored route in both active regions.
##
##     Godot_..._console.exe --headless --path . --script res://tools/world_traversal_probe.gd
##
## RegionStreamer loads the production scenes, CellNavBaker builds their real navigation geometry,
## NavigationServer finds the NPC-valid route, and a player-sized capsule then follows every path
## corner through the real collision world. This catches blocked roads, snagging props, disconnected
## navmesh islands and seams that static coordinate checks cannot.

const REGIONS := [
	"res://data/regions/EmberCrown.tres",
	"res://data/regions/FrostfangReach.tres",
]
## The agent_max_climb every cell's NavigationMesh authors, so the probe steps exactly as high as
## the navmesh promises an NPC can.
const MAX_STEP := 0.5
const FALL_ALLOWANCE := 3.0
const ENDPOINT_TOLERANCE := 2.0
const CAPSULE_RADIUS := 0.4
const CAPSULE_HEIGHT := 1.8

var _failures: Array[String] = []
var _content_loader: Node


func _initialize() -> void:
	var loader_script: Script = load("res://src/Bootstrap/ContentDatabaseLoader.cs")
	_content_loader = loader_script.new()
	root.add_child(_content_loader)
	_run.call_deferred()


func _run() -> void:
	await process_frame
	await process_frame

	var streamer_script: Script = load("res://src/World/RegionStreamer.cs")
	var streamer: Node3D = streamer_script.new()
	root.add_child(streamer)
	streamer.call("SetPerformanceSamplingEnabled", false)

	var route_count := 0
	for region_path in REGIONS:
		var region: Resource = load(region_path)
		streamer.call("Configure", region)
		var settle_frames := 0
		while not streamer.call("IsSettled") and settle_frames < 1800:
			await process_frame
			settle_frames += 1
		if not streamer.call("IsSettled"):
			_fail("%s did not settle" % region_path)
			continue

		# CellNavBaker is asynchronous. Wait for every resident NavigationRegion3D to contain baked
		# vertices instead of assuming a fixed frame count that passes on a fast machine only.
		var bake_frames := 0
		while not _navigation_ready(streamer) and bake_frames < 3600:
			await physics_frame
			bake_frames += 1
		if not _navigation_ready(streamer):
			_fail("%s navigation did not finish baking" % region_path)
			continue
		_print_navigation(streamer)
		for _frame in 3:
			await physics_frame
		NavigationServer3D.map_force_update(root.world_3d.navigation_map)

		for cell in region.get("Cells"):
			var presentation: Resource = cell.get("Presentation")
			if presentation == null:
				continue
			var centre: Vector3 = cell.get("Center")
			for route in presentation.get("Paths"):
				if route == null:
					continue
				route_count += 1
				await _probe_route(
					String(cell.get("Id")), centre,
					route.get("Start"), route.get("End"))
		streamer.call("UnloadAll")
		streamer.call("Configure", null)
		await process_frame
		await process_frame

	_content_loader.call("CollectManagedResources")
	await process_frame
	if _failures.is_empty():
		print("world traversal: PASS (%d authored route segments)" % route_count)
		quit(0)
	else:
		for failure in _failures:
			printerr("world traversal: FAIL — %s" % failure)
		print("world traversal: FAIL (%d issue(s), %d routes)" % [_failures.size(), route_count])
		quit(1)


## The ground under a world point, from the real terrain collider.
func _on_ground(point: Vector3) -> Vector3:
	var space := root.world_3d.direct_space_state
	var query := PhysicsRayQueryParameters3D.create(
		Vector3(point.x, 400.0, point.z), Vector3(point.x, -200.0, point.z))
	query.collide_with_areas = false
	var hit := space.intersect_ray(query)
	return Vector3(point.x, hit.position.y if hit.has("position") else 0.0, point.z)


func _probe_route(cell_id: String, centre: Vector3, local_start: Vector2, local_end: Vector2) -> void:
	var nav_region := _find_navigation_region(cell_id)
	if nav_region == null:
		_fail("%s has no resident NavigationRegion3D" % cell_id)
		return
	var navigation_mesh := nav_region.navigation_mesh
	# ⚠️ THE ROUTE'S HEIGHT COMES FROM THE GROUND, NOT FROM THE CELL CENTRE (the 2026-08-29 geography
	# overhaul). A route endpoint used to be centre.y + 0.2 because every cell floor's top face was
	# exactly y = 0; the realm has real elevation now and a literal Y puts the endpoint inside a hill.
	var wanted_start := _on_ground(centre + Vector3(local_start.x, 0.0, local_start.y))
	var wanted_end := _on_ground(centre + Vector3(local_end.x, 0.0, local_end.y))
	# Inspect the baked polygons directly. NavigationServer closest-point queries are order-dependent
	# when many disconnected streamed regions share one map; the baked mesh is the authoritative,
	# deterministic geometry an NPC actually receives for this cell.
	var start_info := _closest_navigation_polygon(navigation_mesh, local_start)
	var finish_info := _closest_navigation_polygon(navigation_mesh, local_end)
	if start_info[0] > ENDPOINT_TOLERANCE:
		_fail("%s route start %s is %.2f m from navigation" % [
			cell_id, local_start, start_info[0]])
		return
	if finish_info[0] > ENDPOINT_TOLERANCE:
		_fail("%s route end %s is %.2f m from navigation" % [
			cell_id, local_end, finish_info[0]])
		return
	if not _navigation_polygons_connected(navigation_mesh, start_info[1], finish_info[1]):
		_fail("%s route %s -> %s has no NPC navigation path" % [cell_id, local_start, local_end])
		return

	var body := CharacterBody3D.new()
	var collision_shape := CollisionShape3D.new()
	var capsule := CapsuleShape3D.new()
	capsule.radius = CAPSULE_RADIUS
	capsule.height = CAPSULE_HEIGHT
	collision_shape.shape = capsule
	collision_shape.position = Vector3(0, CAPSULE_HEIGHT * 0.5, 0)
	body.add_child(collision_shape)
	root.add_child(body)
	var direct := wanted_end - wanted_start
	direct.y = 0.0
	var direct_length := direct.length()
	var direction := direct.normalized()
	# Interaction anchors often sit exactly at a route endpoint; test the traversable span between
	# the approach points, not the final reach into a chest, hoard or dragon nest.
	var probe_start := wanted_start + direction * minf(0.75, direct_length * 0.2)
	var probe_end := wanted_end - direction * minf(0.75, direct_length * 0.2)
	# ⚠️ THIS IS A WALK, NOT A FLAT SWEEP. It used to pick one Y for the whole route and slide the
	# capsule along it, which was a faithful test of a world whose floors were all at y = 0 and is a
	# test of tunnelling through hillsides in one that is not: fifty-seven "snags" on the first run
	# after the overhaul were all the probe walking into the ground it was standing on. Each step now
	# lifts by the agent's climb allowance, moves, and drops back onto whatever is under it — which
	# is what MoveAndSlide does for the player, so a failure here is a failure there.
	body.position = _on_ground(Vector3(probe_start.x, 0.0, probe_start.z)) + Vector3(0, 0.05, 0)
	await physics_frame

	var remaining := Vector2(body.position.x - probe_end.x, body.position.z - probe_end.z).length()
	while remaining > 0.05:
		var motion := direction * minf(0.5, remaining)
		body.move_and_collide(Vector3(0, MAX_STEP, 0))
		var collision := body.move_and_collide(motion)
		if collision != null:
			var collider: Object = collision.get_collider()
			# Encounter occupants move and use avoidance at runtime; they are not authored terrain
			# blockers and a dragon standing in its nest must not make the approach fail permanently.
			if collider is CharacterBody3D:
				body.add_collision_exception_with(collider)
				continue
			var collider_name := str(collider.get_path()) if collider is Node else str(collider)
			_fail("%s player capsule snagged on authored route at %s on %s" % [
				cell_id, body.position, collider_name])
			break
		body.move_and_collide(Vector3(0, -(MAX_STEP + FALL_ALLOWANCE), 0))
		var next_remaining := Vector2(body.position.x - probe_end.x, body.position.z - probe_end.z).length()
		if next_remaining >= remaining - 0.01:
			_fail("%s player capsule stopped advancing on authored route at %s" % [cell_id, body.position])
			break
		remaining = next_remaining
	body.free()
	await process_frame


func _fail(message: String) -> void:
	_failures.append(message)


func _navigation_ready(streamer: Node) -> bool:
	var regions := streamer.find_children("*", "NavigationRegion3D", true, false)
	if regions.is_empty():
		return false
	for entry in regions:
		var nav := entry as NavigationRegion3D
		if nav == null or nav.navigation_mesh == null or nav.navigation_mesh.get_vertices().is_empty():
			return false
	return true


func _find_navigation_region(cell_id: String) -> NavigationRegion3D:
	var expected_name := cell_id.replace(".", "_")
	for entry in root.find_children("*", "NavigationRegion3D", true, false):
		var nav := entry as NavigationRegion3D
		if nav != null and nav.get_parent() != null and str(nav.get_parent().name) == expected_name:
			return nav
	return null


func _closest_navigation_polygon(mesh: NavigationMesh, point: Vector2) -> Array:
	var vertices := mesh.get_vertices()
	var best_distance := INF
	var best_polygon := -1
	for polygon_index in range(mesh.get_polygon_count()):
		var indices := mesh.get_polygon(polygon_index)
		var polygon := PackedVector2Array()
		for vertex_index in indices:
			var vertex: Vector3 = vertices[vertex_index]
			polygon.append(Vector2(vertex.x, vertex.z))
		if Geometry2D.is_point_in_polygon(point, polygon):
			return [0.0, polygon_index]
		for index in range(polygon.size()):
			var distance := _point_segment_distance(
				point, polygon[index], polygon[(index + 1) % polygon.size()])
			if distance < best_distance:
				best_distance = distance
				best_polygon = polygon_index
	return [best_distance, best_polygon]


func _point_segment_distance(point: Vector2, start: Vector2, finish: Vector2) -> float:
	var segment := finish - start
	var length_squared := segment.length_squared()
	if length_squared <= 0.000001:
		return point.distance_to(start)
	var amount := clampf((point - start).dot(segment) / length_squared, 0.0, 1.0)
	return point.distance_to(start + segment * amount)


func _navigation_polygons_connected(mesh: NavigationMesh, start_polygon: int, finish_polygon: int) -> bool:
	if start_polygon < 0 or finish_polygon < 0:
		return false
	if start_polygon == finish_polygon:
		return true
	var count := mesh.get_polygon_count()
	var visited := PackedByteArray()
	visited.resize(count)
	var pending: Array[int] = [start_polygon]
	visited[start_polygon] = 1
	while not pending.is_empty():
		var current: int = pending.pop_front()
		var current_indices: PackedInt32Array = mesh.get_polygon(current)
		for candidate in range(count):
			if visited[candidate] != 0:
				continue
			var shared := 0
			for vertex_index in mesh.get_polygon(candidate):
				if current_indices.has(vertex_index):
					shared += 1
			if shared < 2:
				continue
			if candidate == finish_polygon:
				return true
			visited[candidate] = 1
			pending.append(candidate)
	return false


func _print_navigation(streamer: Node) -> void:
	for entry in streamer.find_children("*", "NavigationRegion3D", true, false):
		var nav := entry as NavigationRegion3D
		if nav != null and nav.navigation_mesh != null:
			var vertices := nav.navigation_mesh.get_vertices()
			var bounds := AABB(vertices[0], Vector3.ZERO)
			for vertex in vertices:
				bounds = bounds.expand(vertex)
			print("  nav %-42s %5d vertices  x %.1f..%.1f z %.1f..%.1f" % [
				str(nav.get_parent().name), vertices.size(), bounds.position.x, bounds.end.x,
				bounds.position.z, bounds.end.z])
