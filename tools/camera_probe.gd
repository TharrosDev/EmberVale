# Camera probe: does the third-person camera retract for a WALL, restore afterwards, and — the whole
# point of this stage — stay put when a PERSON walks behind the player?
#
# ⚠️ THE COMPANION CASE IS THE DEFECT. CharacterEntity defaults to collision_layer 1, the same World
# layer all 36 static bodies in a cell use, so a camera sweeping World could not tell a wall from an
# ally. A companion stepping between the player and the camera yanked it in, and a `ponytail:` note
# in PlayerCameraRig recorded that and left it. Static geometry now also declares CombatLayers
# .CameraBlocker and the sweep asks for that instead; people are simply not on the layer.
#
# A gate rather than a spot check, because the fix is one mask constant and one load-time walk —
# either of which is easy to revert by accident and impossible to notice without measuring.
#
# Run:  Godot_..._console.exe --headless --path . --script res://tools/camera_probe.gd
extends SceneTree

# ⚠️ MIRRORS CombatLayers, AND THAT IS A LIABILITY. These moved once already — the world-production
# pass renumbered CameraBlocker from bit 4 to bit 9 — and a stale copy here does not fail loudly: the
# probe reports "a wall did not retract the camera" and "0 of 36 solids marked", which reads as the
# feature being broken rather than as the test being wrong. Check these against CombatLayers.cs
# before believing a failure.
const WORLD := 1          # CombatLayers.WorldStatic
const BLOCKER := 1 << 9   # CombatLayers.CameraBlocker

var _failures: Array[String] = []


func _initialize() -> void:
	root.add_child(load("res://src/Bootstrap/ContentDatabaseLoader.cs").new())
	await process_frame
	await _check_obstruction()
	await _check_dialogue_ticks()
	_check_cells_mark_their_geometry()
	print("---")
	if _failures.is_empty():
		print("PASS: walls retract the camera, people do not, it restores, and it ticks through a dialogue")
		quit(0)
	else:
		for f in _failures:
			print("FAIL: %s" % f)
		quit(1)


func _rig() -> Array:
	var body := CharacterBody3D.new()
	body.set_script(load("res://src/Entities/CharacterEntity.cs"))
	body.name = "Player"
	var pivot := Node3D.new()
	pivot.name = "CameraPivot"
	pivot.position = Vector3(0, 1.62, 0)
	body.add_child(pivot)
	var camera := Camera3D.new()
	camera.name = "Camera"
	pivot.add_child(camera)
	var queries = load("res://src/Player/PlayerPhysicsQueries.cs").new()
	queries.name = "Queries"
	body.add_child(queries)
	var rig = load("res://src/Player/PlayerCameraRig.cs").new()
	rig.name = "CameraRig"
	rig.CameraPivot = pivot
	rig.Camera = camera
	body.add_child(rig)
	root.add_child(body)
	return [body, camera, rig]


func _solid(size: Vector3, at: Vector3, layer: int) -> StaticBody3D:
	var solid := StaticBody3D.new()
	solid.collision_layer = layer
	var shape := CollisionShape3D.new()
	var box := BoxShape3D.new()
	box.size = size
	shape.shape = box
	solid.add_child(shape)
	solid.position = at
	root.add_child(solid)
	return solid


func _settle(rig, camera: Camera3D, frames: int) -> float:
	for i in frames:
		await physics_frame
		rig.Tick(1.0 / 60.0)
	return camera.position.length()


func _check_obstruction() -> void:
	var parts := _rig()
	var body: CharacterBody3D = parts[0]
	var camera: Camera3D = parts[1]
	var rig = parts[2]
	rig.SetFirstPerson(false, true)

	var free_distance: float = await _settle(rig, camera, 90)
	print("open ground:      camera sits %.2f m out" % free_distance)
	if free_distance < 1.0:
		_failures.append("the camera never extended on open ground (%.2f m); the rest of this proves nothing"
			% free_distance)
		return

	# A wall right behind the player, on the blocker layer as cell geometry now is.
	var wall := _solid(Vector3(8, 6, 0.5), Vector3(0, 2, 1.2), WORLD | BLOCKER)
	var blocked: float = await _settle(rig, camera, 60)
	print("wall behind:      camera pulled in to %.2f m" % blocked)
	if blocked >= free_distance - 0.2:
		_failures.append("a wall did not retract the camera (%.2f m vs %.2f m open)"
			% [blocked, free_distance])

	wall.queue_free()
	await process_frame
	var restored: float = await _settle(rig, camera, 120)
	print("wall removed:     camera restored to %.2f m" % restored)
	if restored < free_distance - 0.2:
		_failures.append("the camera did not ease back out after the wall went (%.2f m vs %.2f m)"
			% [restored, free_distance])

	# ⚠️ THE CASE THAT MATTERS. An actor body, on the World layer exactly as CharacterEntity puts it,
	# standing precisely where the wall was.
	var ally := CharacterBody3D.new()
	ally.set_script(load("res://src/Entities/CharacterEntity.cs"))
	ally.name = "Companion"
	var shape := CollisionShape3D.new()
	var capsule := CapsuleShape3D.new()
	capsule.radius = 0.5
	capsule.height = 1.8
	shape.shape = capsule
	shape.position = Vector3(0, 0.9, 0)
	ally.add_child(shape)
	root.add_child(ally)
	ally.global_position = Vector3(0, 0, 1.2)
	var with_ally: float = await _settle(rig, camera, 60)
	print("companion behind: camera sits %.2f m out (layer %d)" % [with_ally, ally.collision_layer])

	if with_ally < free_distance - 0.2:
		_failures.append("a companion standing behind the player pulled the camera in to %.2f m from %.2f m — the sweep is still hitting actors"
			% [with_ally, free_distance])

	ally.queue_free()
	body.queue_free()
	await process_frame


# A dialogue pauses the world, which stops the input router, which is the rig's only caller. Without
# the rig ticking itself, the special-view layer's dialogue push-in never steps. The tree is paused by
# hand (UiState is static C#, unreachable from here), which is the same flag the dialogue sets.
#
# The observable is the third-person swap: asked for while paused, the camera can only leave the eye
# if the rig ticked. It must stay put when no dialogue is open, so the paused tick is not a general
# "the camera runs while paused".
func _check_dialogue_ticks() -> void:
	var manager := root.get_node_or_null("GameManager")
	if manager == null:
		_failures.append("no GameManager autoload; cannot probe the paused-dialogue tick")
		return
	manager.ChangeState(3)  # GameState.Playing

	var parts := _rig()
	var body: CharacterBody3D = parts[0]
	var camera: Camera3D = parts[1]
	var rig = parts[2]
	await process_frame

	paused = true
	rig.SetFirstPerson(false, false)
	await create_timer(1.5, true, false, true).timeout
	var idle: float = camera.position.length()
	print("paused, no dialogue: camera sits %.2f m out" % idle)
	if idle > 0.05:
		_failures.append("the rig ticked while the world was paused with no dialogue open (%.2f m)" % idle)

	rig.DialogueOpen = true
	await create_timer(1.5, true, false, true).timeout
	var talking: float = camera.position.length()
	print("paused, dialogue:    camera sits %.2f m out" % talking)
	if talking < 1.0:
		_failures.append("the rig did not tick during a dialogue (%.2f m); the push-in would never play" % talking)

	paused = false
	manager.ChangeState(0)  # GameState.Boot, as found
	body.queue_free()
	await process_frame


# Cell geometry has to actually carry the blocker bit, or the sweep above finds nothing in the real
# world and the camera clips through every wall in the game.
func _check_cells_mark_their_geometry() -> void:
	var cell := (load("res://scenes/regions/ember_crown/town_hub.tscn") as PackedScene).instantiate()
	root.add_child(cell)

	# The same call RegionStreamer.Instantiate makes on every cell it loads. Running it here proves
	# the marking itself; the streamer calling it is one line at the top of that method.
	load("res://src/World/RegionStreamer.cs").MarkCameraBlockers(cell)

	var total := 0
	var marked := 0
	var stack: Array[Node] = [cell]
	while not stack.is_empty():
		var n: Node = stack.pop_back()
		if n is StaticBody3D and (n.collision_layer & WORLD) != 0:
			total += 1
			if (n.collision_layer & BLOCKER) != 0:
				marked += 1
		for c in n.get_children():
			stack.append(c)

	print("town_hub solids: %d on World, %d marked CameraBlocker after the load-time pass" % [total, marked])
	if total == 0:
		_failures.append("town_hub has no World-layer static bodies; this check proves nothing")
	elif marked != total:
		_failures.append("only %d of %d cell solids were marked as camera blockers — the camera would clip through the rest"
			% [marked, total])
	cell.queue_free()
