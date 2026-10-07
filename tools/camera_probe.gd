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
# It also holds the FIRST-PERSON eye to account (the 2026-10 camera pass). The eye used to ride the
# animated head bone, so a sprint bobbed and lurched the view and let the skull into frame. It is
# now anchored to where the head rests and takes a fraction of the head's travel, and the head is cut
# out of the body by its shader. Measured on the real body: the eye barely moves from frame to frame
# at a sprint; level, looking fully down and looking fully up, the whole head is inside the cut-out
# round the head, the camera's near plane is inside the cut-out round the eye, and the shoulders,
# hips, hands and feet are outside both (there is still a body to look down at); looking down, the
# eye is out in front of the chest rather than in it; and the cut-outs come on in first person and
# go off again in third.
#
# ⚠️ WHAT THIS USED TO ASK WAS THE WRONG QUESTION. It required the eye to stay inside the sphere round
# the HEAD, on the reasoning that an eye outside it would see the face. The face is inside that
# sphere wherever the eye is, so it is never drawn; and an eye held inside a sphere round the head is
# an eye held over the neck, which looking down is an eye in the chest (the first-person frame was
# solid black). The eye now leans out ahead of the body as the look goes down, and what keeps the
# body off the near plane is a second cut-out that is centred on the eye itself.
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

const BODY := "res://assets/models/characters/chr_player_base.glb"
const GAIT := "parameters/StateMachine/locomotion/blend_position"

# The body built for the eye checks has no LocomotionComponent, so its gaits are measured against
# LocomotionBlend.FallbackRunSpeed; a sprint is 1.6 of that. Mirrors those constants.
const RUN_SPEED := 3.6
const SPRINT_GAIT := 1.6

# The most the eye may move between two frames at a sprint, in metres at 60 frames a second.
const MAX_EYE_STEP := 0.03

# How far the look is pitched for the cut-out check, in degrees either way.
const LOOK_DEGREES := 80.0

# Looking fully down, the eye must be at least this far ahead of the upper chest bone, along the
# body's facing (metres). The chest's surface stands about 0.14 m ahead of that bone on
# chr_player_base and the near plane is 0.08 m, so less than this is a frame full of chest.
const MIN_EYE_AHEAD_OF_CHEST := 0.2

# Points of the head, from the head bone in the body's axes (up, forward), in metres: the crown, the
# face, the back of the skull and the chin on chr_player_base. All of them must be cut out.
const HEAD_POINTS := [[0.18, 0.0], [0.05, 0.10], [0.05, -0.11], [-0.05, 0.04]]

# Bones that must NOT be cut out at any pitch: the body the player looks down at.
const BODY_BONES := ["LeftUpperArm", "RightUpperArm", "Hips", "LeftHand", "RightHand", "LeftFoot", "RightFoot"]

var _failures: Array[String] = []


func _initialize() -> void:
	root.add_child(load("res://src/Bootstrap/ContentDatabaseLoader.cs").new())
	await process_frame
	await _check_obstruction()
	await _check_dialogue_ticks()
	_check_cells_mark_their_geometry()
	await _check_first_person_eye()
	print("---")
	if _failures.is_empty():
		print("PASS: walls retract the camera, people do not, it restores, it ticks through a dialogue, and the first-person eye is steady, clear of the body and never shows the head")
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


# The player's body and camera chain, built the way PlayerFactory builds them, with the real
# animation component driving the real model. Returns [body, pivot, camera, rig, tree].
func _first_person_rig() -> Array:
	var body := CharacterBody3D.new()
	body.set_script(load("res://src/Entities/CharacterEntity.cs"))
	body.name = "Player"
	var shape := CollisionShape3D.new()
	var capsule := CapsuleShape3D.new()
	capsule.radius = 0.4
	capsule.height = 1.8
	shape.shape = capsule
	shape.position = Vector3(0, 0.9, 0)
	body.add_child(shape)

	var visual: Node3D = (load(BODY) as PackedScene).instantiate()
	visual.name = "BodyMesh"
	visual.rotate_y(PI)
	# The body wears the player-body shader in the game (PlayerAppearance puts it on at creation).
	# Put on here by hand so the rig finds the surfaces it sets the head cut-out on.
	var body_shader: Shader = load("res://assets/shaders/player_body.gdshader")
	for found in visual.find_children("*", "MeshInstance3D", true, false):
		var mesh_instance := found as MeshInstance3D
		if mesh_instance == null or mesh_instance.mesh == null or body_shader == null:
			continue
		for surface_index in mesh_instance.mesh.get_surface_count():
			var material := ShaderMaterial.new()
			material.shader = body_shader
			mesh_instance.set_surface_override_material(surface_index, material)
	body.add_child(visual)

	var stats = load("res://src/Stats/StatsComponent.cs").new()
	stats.name = "Stats"
	body.add_child(stats)
	var combat = load("res://src/Combat/CombatComponent.cs").new()
	combat.name = "Combat"
	body.add_child(combat)

	var pivot := Node3D.new()
	pivot.name = "CameraPivot"
	pivot.position = Vector3(0, 1.62, 0)
	body.add_child(pivot)
	var camera := Camera3D.new()
	camera.name = "Camera"
	camera.near = 0.08
	pivot.add_child(camera)

	var animation = load("res://src/Animation/CharacterAnimationComponent.cs").new()
	animation.name = "Animation"
	body.add_child(animation)
	var queries = load("res://src/Player/PlayerPhysicsQueries.cs").new()
	queries.name = "Queries"
	body.add_child(queries)
	var rig = load("res://src/Player/PlayerCameraRig.cs").new()
	rig.name = "CameraRig"
	rig.CameraPivot = pivot
	rig.Camera = camera
	body.add_child(rig)

	# Well away from the wall and the cell the earlier checks left about.
	body.position = Vector3(400, 0, 400)
	root.add_child(body)
	await process_frame
	await process_frame

	var tree: AnimationTree = null
	for child in animation.get_children():
		if child is AnimationTree:
			tree = child
	return [body, pivot, camera, rig, tree]


# Ticks the rig by hand for a span of REAL time and returns the fastest the eye moved, in metres per
# second. The eye is the camera's position under its pivot; the body, the pivot and the pitch all
# stay put here, so this is the eye's own motion and nothing else. The rig is ticked with the real
# frame time: headless Godot runs its frames uncapped, the animation advances on real delta, and an
# eye smoothed with a made-up 1/60 would not match it.
func _watch_eye(rig, camera: Camera3D, seconds: float) -> float:
	var fastest := 0.0
	var started := Time.get_ticks_usec()
	var last_tick := started
	var sample_time := started
	var sample: Vector3 = camera.position
	while (Time.get_ticks_usec() - started) / 1000000.0 < seconds:
		await process_frame
		var now := Time.get_ticks_usec()
		var dt := (now - last_tick) / 1000000.0
		last_tick = now
		if dt > 0.0:
			rig.Tick(dt)
		# Sampled no more often than every 4 ms: over a shorter span the division is mostly noise.
		var span := (now - sample_time) / 1000000.0
		if span >= 0.004:
			fastest = maxf(fastest, camera.position.distance_to(sample) / span)
			sample = camera.position
			sample_time = now
	return fastest


# Pitches the look to an angle (degrees, positive up) and lets the rig settle on it.
func _look(rig, degrees: float) -> void:
	# ApplyPitchStep SUBTRACTS its step (a mouse moving down looks down), so the step is the
	# difference the other way round. Asked twice because the first step is clamped to the limit the
	# rig had before it ticked.
	for attempt in 2:
		rig.ApplyPitchStep(rig.Pitch - deg_to_rad(degrees), false)
		for i in 20:
			await process_frame
			rig.Tick(1.0 / 60.0)


func _check_first_person_eye() -> void:
	var parts: Array = await _first_person_rig()
	var body: CharacterBody3D = parts[0]
	var camera: Camera3D = parts[2]
	var rig = parts[3]
	var tree: AnimationTree = parts[4]
	if tree == null:
		_failures.append("no AnimationTree was built on chr_player_base; the eye checks need the real animation")
		body.queue_free()
		await process_frame
		return

	rig.SetFirstPerson(true, true)
	for i in 30:
		await process_frame
		rig.Tick(1.0 / 60.0)

	# --- steady at a sprint ---------------------------------------------------------------------
	# -Z is the body's forward. The animation component turns the velocity into the sprint gait; the
	# capsule itself stays put, so everything the eye does here is the animation's doing.
	body.velocity = -body.global_basis.z * (SPRINT_GAIT * RUN_SPEED)
	await _watch_eye(rig, camera, 0.6)   # let the blend and the eye's smoothing settle
	var sent: Vector2 = tree.get(GAIT)
	if absf(sent.y - SPRINT_GAIT) > 0.05:
		_failures.append("the body was not put into a sprint (forward gait %.2f, wanted %.2f); the eye check measured something else"
			% [sent.y, SPRINT_GAIT])
	var fastest: float = await _watch_eye(rig, camera, 2.0)
	var step := fastest / 60.0
	print("first person, sprinting: the eye moves at most %.1f mm between frames at 60 fps (%.2f m/s)"
		% [step * 1000.0, fastest])
	if step > MAX_EYE_STEP:
		_failures.append("at a sprint the first-person eye moves %.1f mm between frames (limit %.0f mm) — it is riding the animated head again"
			% [step * 1000.0, MAX_EYE_STEP * 1000.0])

	# How far the head itself travels, for the log: this is what the eye is NOT following.
	var away := camera.global_position.distance_to(rig.HeadSphereCentre)
	body.velocity = Vector3.ZERO
	for i in 60:
		await process_frame
		rig.Tick(1.0 / 60.0)

	# --- the head cut-out -----------------------------------------------------------------------
	if not rig.HeadHidden:
		_failures.append("in first person the head is not cut out of the body — the camera is looking at the inside of the player's own skull")
	var radius: float = rig.HeadSphereRadius
	var cut := _surfaces_cut(body)
	print("first person: head cut out = %s, radius %.2f m, set on %d body surface(s); the eye sat %.3f m from its centre at a sprint"
		% [rig.HeadHidden, radius, cut, away])
	if cut == 0:
		_failures.append("in first person no body surface carries the fp_head cut-out — the shader has nothing to discard")

	for degrees in [0.0, -LOOK_DEGREES, LOOK_DEGREES]:
		await _look(rig, degrees)
		var reached := rad_to_deg(rig.Pitch)
		var from_centre := camera.global_position.distance_to(rig.HeadSphereCentre)
		if absf(reached - degrees) > 4.0:
			_failures.append("the look could not be pitched to %+.0f deg (it reached %+.0f)" % [degrees, reached])
		_check_cutouts(body, camera, reached, from_centre)
	await _look(rig, 0.0)

	# --- and it comes back in third person ------------------------------------------------------
	rig.SetFirstPerson(false, true)
	for i in 30:
		await process_frame
		rig.Tick(1.0 / 60.0)
	print("third person: head cut out = %s, camera %.2f m from the head"
		% [rig.HeadHidden, camera.global_position.distance_to(rig.HeadSphereCentre)])
	if rig.HeadHidden:
		_failures.append("the head is still cut out in third person — the character is headless from behind")
	var still_cut := _surfaces_cut(body)
	if still_cut > 0:
		_failures.append("%d body surface(s) still carry the fp_head cut-out in third person" % still_cut)
	if rig.EyeSphereRadius > 0.0:
		_failures.append("the cut-out round the camera is still on in third person (radius %.3f m)" % rig.EyeSphereRadius)

	body.queue_free()
	await process_frame


# One pitch of the cut-out check. The two spheres are read back off the body's own mesh instance,
# which is what the shader is given, not recomputed from the rig.
func _check_cutouts(body: CharacterBody3D, camera: Camera3D, reached: float, from_centre: float) -> void:
	var head_sphere := Vector4.ZERO
	var eye_sphere := Vector4.ZERO
	for found in body.find_children("*", "MeshInstance3D", true, false):
		var mesh_instance := found as MeshInstance3D
		if mesh_instance == null:
			continue
		var head_value = mesh_instance.get_instance_shader_parameter("fp_head")
		var eye_value = mesh_instance.get_instance_shader_parameter("fp_eye")
		if head_value is Vector4 and head_value.w > 0.0:
			head_sphere = head_value
			if eye_value is Vector4:
				eye_sphere = eye_value
			break

	var eye: Vector3 = camera.global_position
	var head_centre := Vector3(head_sphere.x, head_sphere.y, head_sphere.z)
	var eye_centre := Vector3(eye_sphere.x, eye_sphere.y, eye_sphere.z)

	# The far corners of the near plane: the furthest from the eye that the near plane cuts.
	var size: Vector2 = root.get_visible_rect().size
	var aspect := 16.0 / 9.0
	if size.x > 1.0 and size.y > 1.0:
		aspect = size.x / size.y
	var half := tan(deg_to_rad(camera.fov) * 0.5)
	var near_corner := camera.near * sqrt(1.0 + half * half * (1.0 + aspect * aspect))

	print("first person, looking %+.0f deg: eye %.3f m from the head cut-out's centre (radius %.2f); eye cut-out radius %.3f m, %.3f m off the camera; near plane corners at %.3f m"
		% [reached, from_centre, head_sphere.w, eye_sphere.w, eye.distance_to(eye_centre), near_corner])

	if head_sphere.w <= 0.0:
		_failures.append("looking %+.0f deg no body surface carries a head cut-out" % reached)
		return
	if eye_sphere.w <= 0.0:
		_failures.append("looking %+.0f deg no body surface carries the fp_eye cut-out round the camera — whatever of the body is nearest the eye is sliced by the near plane"
			% reached)
	else:
		if eye.distance_to(eye_centre) > 0.02:
			_failures.append("looking %+.0f deg the eye cut-out is centred %.3f m from the camera — it is not following the eye"
				% [reached, eye.distance_to(eye_centre)])
		# Level and looking up only. Looking down the rig narrows the sphere on purpose
		# (CameraRigMath.EyeSphereLimit), so it cannot take a bite out of the chest in view.
		if reached > -45.0 and eye_sphere.w < near_corner:
			_failures.append("looking %+.0f deg the eye cut-out (%.3f m) is smaller than the near plane's corners (%.3f m) — the near plane can still slice the body"
				% [reached, eye_sphere.w, near_corner])

	var skeletons := body.find_children("*", "Skeleton3D", true, false)
	if skeletons.is_empty():
		_failures.append("the first-person body has no skeleton to check the cut-outs against")
		return
	var sk: Skeleton3D = skeletons[0]
	var up: Vector3 = body.global_basis.y.normalized()
	var forward: Vector3 = -body.global_basis.z.normalized()

	# The head, all of it, is cut out: the face cannot be in view from anywhere.
	var head_bone := sk.find_bone("Head")
	if head_bone < 0:
		_failures.append("chr_player_base has no Head bone")
		return
	var head: Vector3 = sk.global_transform * sk.get_bone_global_pose(head_bone).origin
	for point in HEAD_POINTS:
		var at: Vector3 = head + up * point[0] + forward * point[1]
		if at.distance_to(head_centre) >= head_sphere.w:
			_failures.append("looking %+.0f deg a point of the head (%.2f up, %.2f forward of the bone) is %.3f m from the head cut-out's centre, outside its %.2f m radius — part of the player's own head is drawn"
				% [reached, point[0], point[1], at.distance_to(head_centre), head_sphere.w])

	# The body is not: there is something to look down at.
	for bone_name in BODY_BONES:
		var bone := sk.find_bone(bone_name)
		if bone < 0:
			continue
		var joint: Vector3 = sk.global_transform * sk.get_bone_global_pose(bone).origin
		if joint.distance_to(head_centre) < head_sphere.w or joint.distance_to(eye_centre) < eye_sphere.w:
			_failures.append("looking %+.0f deg the %s joint is inside a first-person cut-out (%.3f m from the head's centre, %.3f m from the eye) — the body the player should see is being hidden"
				% [reached, bone_name, joint.distance_to(head_centre), joint.distance_to(eye_centre)])

	# Looking down, the eye is out in front of the chest, not in it.
	if reached < -45.0:
		var chest_bone := sk.find_bone("UpperChest")
		if chest_bone < 0:
			chest_bone = sk.find_bone("Chest")
		if chest_bone >= 0:
			var chest: Vector3 = sk.global_transform * sk.get_bone_global_pose(chest_bone).origin
			var ahead := (eye - chest).dot(forward)
			var above := (eye - chest).dot(up)
			print("first person, looking %+.0f deg: the eye is %.3f m ahead of and %+.3f m above the %s bone"
				% [reached, ahead, above, sk.get_bone_name(chest_bone)])
			if ahead < MIN_EYE_AHEAD_OF_CHEST:
				_failures.append("looking %+.0f deg the eye is only %.3f m ahead of the chest bone (at least %.2f m is needed to clear the chest's surface and the near plane) — the frame is the player's own torso"
					% [reached, ahead, MIN_EYE_AHEAD_OF_CHEST])


# How many of the body's mesh instances currently carry a head cut-out (an fp_head with a radius).
func _surfaces_cut(body: Node) -> int:
	var count := 0
	for found in body.find_children("*", "MeshInstance3D", true, false):
		var mesh_instance := found as MeshInstance3D
		if mesh_instance == null:
			continue
		var value = mesh_instance.get_instance_shader_parameter("fp_head")
		if value is Vector4 and value.w > 0.0:
			count += 1
	return count
