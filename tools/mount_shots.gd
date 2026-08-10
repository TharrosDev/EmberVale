# Mount verification harness (Phase 39A). Measures the imported mount model in the REAL engine and
# renders the mounted composition — the horse with a rider on it — from eye level, front and back,
# beside the market's own geometry.
#
# It exists because two of this repo's most expensive recurring defects meet here:
#   * "MEASURING A MODEL'S ACCESSORS IS NOT MEASURING THE MODEL" (NOW.md invariant 19). The glTF
#     accessor bounds for this pack are in a 100x armature space and read ~4.8 m tall for a horse.
#     AABB from the imported scene is the only number that means anything.
#   * "RENDER THE THING, AND RENDER IT WITH THE PEOPLE AND FURNITURE AROUND IT" (invariant 14, seven
#     firings). A horse under the player is a placement, and the saddle height is a number that is
#     only wrong on screen.
#
# Run:  Godot_..._console.exe --path . --script res://tools/mount_shots.gd
# Output under tools/shots/ is DISPOSABLE — regenerate it, do not commit it.
extends SceneTree

const MOUNT := "res://assets/models/creatures/mnt_horse.glb"
const PLAYER := "res://assets/models/characters/chr_player_base.glb"
const CELL := "res://scenes/regions/ember_crown/embermarket.tscn"

# Kept in step with MountComponent's own constants; this harness is what proves them.
const SADDLE_Y := 0.86
const SADDLE_Z := -0.52
const MOUNT_Z := 0.0
const RIDE_CLIP := "Driving"

var _shots: Array = []


func _initialize() -> void:
	var mount_scene: PackedScene = load(MOUNT)
	if mount_scene == null:
		print("FAIL: mount model did not load")
		quit(1)
		return

	var horse: Node3D = mount_scene.instantiate()
	root.add_child(horse)
	# ⚠️ A node added during _initialize is not yet "inside tree", and global_transform on one
	# returns IDENTITY with an error — which silently reports the raw bind-pose mesh box (0.057 m
	# for this pack) as if it were the model. That is invariant 19's trap wearing an engine costume.
	await process_frame
	await process_frame
	var box: AABB = _aabb(horse, horse)
	print("mount AABB position=%s size=%s" % [box.position, box.size])
	print("  withers/top   %.3f m" % box.end.y)
	print("  nose-to-tail  %.3f m" % box.size.z)
	print("  width         %.3f m" % box.size.x)
	print("  feet at       %.3f m" % box.position.y)
	print("  clips: %s" % [_clips(horse)])
	# The saddle seat is a BONE height, not a fraction of the bounding box — the box top is the ears.
	var skel: Skeleton3D = _skeleton(horse)
	if skel != null:
		for i in skel.get_bone_count():
			var bone_name: String = skel.get_bone_name(i)
			if bone_name.to_lower().begins_with("spine") or bone_name.to_lower().begins_with("body"):
				var y: float = (skel.global_transform * skel.get_bone_global_pose(i)).origin.y
				print("  bone %-16s y=%.3f" % [bone_name, y])
	horse.queue_free()

	var packed: PackedScene = load(CELL)
	if packed == null:
		print("FAIL: cell did not load")
		quit(1)
		return
	var cell: Node = packed.instantiate()
	root.add_child(cell)

	# The rider: the player's own body raised to the saddle, exactly as MountComponent does it.
	var rider_root := Node3D.new()
	root.add_child(rider_root)
	rider_root.position = Vector3(0, 0, 0)
	var steed: Node3D = mount_scene.instantiate()
	# glTF forward is +Z and Godot's is -Z, so the steed turns 180° exactly as the player body does.
	steed.rotate_y(PI)
	steed.position = Vector3(0, 0, MOUNT_Z)
	rider_root.add_child(steed)
	var body: Node3D = load(PLAYER).instantiate()
	body.rotate_y(PI)
	body.position = Vector3(0, SADDLE_Y, SADDLE_Z)
	rider_root.add_child(body)

	# ⚠️ The seat is only judgeable in the SEATED pose. A standing model at saddle height reads as
	# floating no matter what the number is, and a correct number reads as floating too — which is
	# how a "reviewed the transform" pass ships a rider standing on a horse's back.
	await process_frame
	_pose(body, "ride")
	_pose(steed, "idle")

	_light()
	_shots = [
		["01_front", Vector3(0, 1.7, -5.0), Vector3(0, 1.4, 0)],
		["02_back", Vector3(0, 1.7, 5.0), Vector3(0, 1.4, 0)],
		["03_side", Vector3(5.0, 1.7, 0.4), Vector3(0, 1.4, 0)],
		["04_walkup", Vector3(-4.5, 1.7, -8.0), Vector3(0, 1.3, 0)],
		["05_high", Vector3(-6, 6, -9), Vector3(0, 1, 0)],
		# ⚠️ The two that matter most and are the least obvious. MountComponent raises the camera
		# pivot by SADDLE_Y, and if it did not, the shipping first-person eye would sit at 1.62 m —
		# INSIDE the horse's neck. These are those two camera seats, not artistic angles:
		# first person at the raised eye, and the third-person rest offset from PlayerFactory
		# (3.8 m back, 0.4 m up, 0.6 m right shoulder) measured from the same raised pivot.
		["06_firstperson", Vector3(0, SADDLE_Y + 1.62, SADDLE_Z), Vector3(0, SADDLE_Y + 1.5, -12)],
		["07_thirdperson", Vector3(0.6, SADDLE_Y + 2.02, SADDLE_Z + 3.8), Vector3(0, SADDLE_Y + 1.4, -6)],
	]
	_render()


func _light() -> void:
	var env := WorldEnvironment.new()
	var e := Environment.new()
	var sky := Sky.new()
	var mat := ProceduralSkyMaterial.new()
	mat.sky_top_color = Color(0.42, 0.45, 0.52)
	mat.sky_horizon_color = Color(0.72, 0.63, 0.5)
	e.sky = sky
	sky.sky_material = mat
	e.background_mode = Environment.BG_SKY
	e.ambient_light_source = Environment.AMBIENT_SOURCE_SKY
	e.ambient_light_energy = 1.0
	env.environment = e
	root.add_child(env)

	var sun := DirectionalLight3D.new()
	sun.rotation_degrees = Vector3(-48, -130, 0)
	sun.light_energy = 1.2
	root.add_child(sun)


func _render() -> void:
	var camera := Camera3D.new()
	camera.fov = 70
	camera.current = true
	root.add_child(camera)
	DirAccess.make_dir_recursive_absolute("res://tools/shots")

	await process_frame
	await process_frame

	for shot in _shots:
		camera.global_position = shot[1]
		camera.look_at(shot[2], Vector3.UP)
		for _i in range(8):
			await process_frame
		var image: Image = root.get_texture().get_image()
		var path := "res://tools/shots/mount_%s.png" % shot[0]
		var err := image.save_png(path)
		print("%s -> %s" % [path, "ok" if err == OK else str(err)])

	quit(0)


func _aabb(node: Node, origin: Node3D) -> AABB:
	var box := AABB()
	var seeded := false
	for child in node.get_children():
		var sub: AABB = _aabb(child, origin)
		if sub.size != Vector3.ZERO:
			box = sub if not seeded else box.merge(sub)
			seeded = true
	if node is VisualInstance3D:
		var local: AABB = (node as VisualInstance3D).get_aabb()
		var world := origin.global_transform.affine_inverse() * (node as Node3D).global_transform
		var own := world * local
		box = own if not seeded else box.merge(own)
	return box


# Plays a clip by the same slot vocabulary C# resolves through AnimationClips: "ride" is the shared
# library's Sitting_Idle, "idle" the model's own. Approximate on purpose — the harness only has to
# put the body in the pose the game will put it in, not re-implement the resolver.
func _pose(node: Node, slot: String) -> void:
	var player: AnimationPlayer = _anim_player(node)
	if player == null:
		return
	if slot == "ride":
		var library: AnimationLibrary = load("res://assets/models/animations/anim_library.res")
		if library != null and not player.has_animation_library("lib"):
			player.add_animation_library("lib", library)
	for name in player.get_animation_list():
		var bare: String = name.get_slice("/", name.get_slice_count("/") - 1).get_slice("|", 1) \
			if "|" in name else name.get_slice("/", name.get_slice_count("/") - 1)
		if (slot == "ride" and bare.begins_with(RIDE_CLIP)) or \
				(slot == "idle" and bare == "Idle"):
			player.play(name)
			print("  posed %s -> %s" % [slot, name])
			return
	print("  posed %s -> NOTHING FOUND" % slot)


func _anim_player(node: Node) -> AnimationPlayer:
	if node is AnimationPlayer:
		return node as AnimationPlayer
	for child in node.get_children():
		var found: AnimationPlayer = _anim_player(child)
		if found != null:
			return found
	return null


func _skeleton(node: Node) -> Skeleton3D:
	if node is Skeleton3D:
		return node as Skeleton3D
	for child in node.get_children():
		var found: Skeleton3D = _skeleton(child)
		if found != null:
			return found
	return null


func _clips(node: Node) -> Array:
	if node is AnimationPlayer:
		return (node as AnimationPlayer).get_animation_list()
	for child in node.get_children():
		var found: Array = _clips(child)
		if not found.is_empty():
			return found
	return []
