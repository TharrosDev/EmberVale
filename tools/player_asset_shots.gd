extends SceneTree

const PLAYER := preload("res://assets/models/characters/chr_player_base.glb")
const RIGHT_ARM := preload("res://assets/models/characters/fp_arm_right.glb")
const LEFT_ARM := preload("res://assets/models/characters/fp_arm_left.glb")
const SWORD := preload("res://assets/models/weapons/wpn_sword_iron.glb")
const PAULDRON := preload("res://assets/models/equipment/eqp_pauldron_embervale.glb")
const POUCH := preload("res://assets/models/equipment/eqp_pouch_embervale.glb")

const RIGHT_REST := Vector3(0.30, -0.49, -0.74)
const LEFT_REST := Vector3(-0.30, -0.49, -0.74)
const RIGHT_REST_ROTATION := Vector3(26, -8, -8)
const LEFT_REST_ROTATION := Vector3(18, 8, 8)
const GRIP_POINT := Vector3(0.0595, 0.1526, -0.1343)
const BLADE_DIRECTION := Vector3(-0.10, 0.82, -0.56)
const SWORD_GRIP_HEIGHT := 0.03

var output := "reports/3d/session-02-player-weapons-handoff/gameplay-renders"
var stage: Node3D
var camera: Camera3D


func _initialize() -> void:
	var args := OS.get_cmdline_user_args()
	for i in range(args.size() - 1):
		if args[i] == "--output":
			output = args[i + 1]
	DirAccess.make_dir_recursive_absolute(ProjectSettings.globalize_path("res://" + output))
	call_deferred("_run")


func _run() -> void:
	build_stage()
	await process_frame
	await process_frame
	await render_player_views()
	await render_viewmodel_views()
	print("Player asset shots written to ", output)
	quit(0)


func build_stage() -> void:
	stage = Node3D.new()
	root.add_child(stage)
	var world_environment := WorldEnvironment.new()
	var environment := Environment.new()
	environment.background_mode = Environment.BG_COLOR
	environment.background_color = Color("20242b")
	environment.ambient_light_source = Environment.AMBIENT_SOURCE_COLOR
	environment.ambient_light_color = Color("9aa7b7")
	environment.ambient_light_energy = 0.65
	environment.tonemap_mode = Environment.TONE_MAPPER_FILMIC
	world_environment.environment = environment
	stage.add_child(world_environment)
	var key := DirectionalLight3D.new()
	key.rotation_degrees = Vector3(-42, -32, 0)
	key.light_color = Color("ffd6a3")
	key.light_energy = 1.5
	key.shadow_enabled = true
	stage.add_child(key)
	var fill := DirectionalLight3D.new()
	fill.rotation_degrees = Vector3(-18, 145, 0)
	fill.light_color = Color("9eb8d7")
	fill.light_energy = 0.75
	stage.add_child(fill)
	var floor_mesh := MeshInstance3D.new()
	var plane := PlaneMesh.new()
	plane.size = Vector2(8, 8)
	floor_mesh.mesh = plane
	floor_mesh.material_override = material(Color("55524b"), 0.92, 0.0)
	stage.add_child(floor_mesh)
	camera = Camera3D.new()
	camera.current = true
	camera.fov = 55
	camera.near = 0.05
	stage.add_child(camera)


func render_player_views() -> void:
	var player := PLAYER.instantiate() as Node3D
	player.name = "Session02Player"
	player.rotation.y = PI
	stage.add_child(player)
	var skeleton := find_type(player, "Skeleton3D") as Skeleton3D
	attach_gear(skeleton, "LeftUpperArm", PAULDRON, "PauldronLeft", Vector3(0, 0.015, 0), Vector3.ZERO)
	attach_gear(skeleton, "RightUpperArm", PAULDRON, "PauldronRight", Vector3(0, 0.015, 0), Vector3(0, 180, 0))
	attach_gear(skeleton, "Hips", POUCH, "UtilityPouch", Vector3(-0.22, 0.02, 0.13), Vector3(5, -8, -8))
	attach_gear(skeleton, "RightHand", SWORD, "IronSword", Vector3.ZERO, Vector3.ZERO)
	var weapon_socket := skeleton.get_node_or_null("IronSwordSocket") as BoneAttachment3D
	if weapon_socket:
		weapon_socket.transform = third_person_grip_transform()
	var animation := find_type(player, "AnimationPlayer") as AnimationPlayer
	if animation:
		animation.play("CharacterArmature|Idle_Sword")
	await process_frame
	var views := {
		"front": Vector3(0, 1.0, 3.2), "rear": Vector3(0, 1.0, -3.2),
		"left": Vector3(-3.2, 1.0, 0), "right": Vector3(3.2, 1.0, 0),
		"front_3q": Vector3(2.45, 1.15, 2.45), "rear_3q": Vector3(-2.45, 1.15, -2.45),
	}
	for label in views:
		camera.global_position = views[label]
		camera.look_at(Vector3(0, 0.86, 0), Vector3.UP)
		await capture("player_" + label)
	if animation:
		animation.play("CharacterArmature|Run")
		await process_frame
		await capture("player_pose_run")
		animation.play("CharacterArmature|Sword_Slash")
		await process_frame
		await capture("player_pose_attack")
	player.queue_free()
	await process_frame


func render_viewmodel_views() -> void:
	camera.global_position = Vector3.ZERO
	camera.global_rotation = Vector3.ZERO
	camera.fov = 75
	var vm := Node3D.new()
	vm.name = "Viewmodel"
	camera.add_child(vm)
	var right := Node3D.new()
	right.name = "RightArm"
	right.position = RIGHT_REST
	right.rotation_degrees = RIGHT_REST_ROTATION
	right.add_child(RIGHT_ARM.instantiate())
	vm.add_child(right)
	var left := Node3D.new()
	left.name = "LeftArm"
	left.position = LEFT_REST
	left.rotation_degrees = LEFT_REST_ROTATION
	left.add_child(LEFT_ARM.instantiate())
	vm.add_child(left)
	var view_scale := tan(deg_to_rad(75.0) * 0.5) / tan(deg_to_rad(55.0) * 0.5)
	right.scale = Vector3.ONE * view_scale
	left.scale = Vector3.ONE * view_scale
	var weapon_socket := Node3D.new()
	weapon_socket.name = "WeaponSocket"
	weapon_socket.transform = grip_transform()
	right.add_child(weapon_socket)
	weapon_socket.add_child(SWORD.instantiate())
	await capture("viewmodel_idle")
	right.position = RIGHT_REST + Vector3(0.008, 0.014, 0)
	left.position = LEFT_REST + Vector3(0.008, 0.014, 0)
	await capture("viewmodel_walk")
	right.position = RIGHT_REST + Vector3(-0.06, 0.10, -0.02)
	right.rotation_degrees = RIGHT_REST_ROTATION + Vector3(16, 0, -15)
	left.position = LEFT_REST + Vector3(0.06, 0.10, -0.02)
	left.rotation_degrees = LEFT_REST_ROTATION + Vector3(16, 0, 15)
	await capture("viewmodel_block")
	right.position = RIGHT_REST + Vector3(0, 0, -0.16)
	right.rotation_degrees = RIGHT_REST_ROTATION + Vector3(-40, 30, -12)
	left.position = LEFT_REST
	left.rotation_degrees = LEFT_REST_ROTATION
	await capture("viewmodel_attack")
	right.position = RIGHT_REST
	right.rotation_degrees = RIGHT_REST_ROTATION
	left.position = LEFT_REST + Vector3(0.05, 0.08, -0.08)
	left.rotation_degrees = LEFT_REST_ROTATION + Vector3(-18, -14, -30)
	await capture("viewmodel_cast")
	left.position = LEFT_REST + Vector3(0.035, 0.025, -0.06)
	left.rotation_degrees = LEFT_REST_ROTATION + Vector3(-12, 0, -10)
	await capture("viewmodel_interact")
	vm.queue_free()
	await process_frame


func attach_gear(skeleton: Skeleton3D, bone: String, scene: PackedScene, label: String,
		position: Vector3, rotation: Vector3, scale := Vector3.ONE) -> void:
	if not skeleton or skeleton.find_bone(bone) < 0:
		push_error("Missing gear bone: " + bone)
		return
	var socket := BoneAttachment3D.new()
	socket.name = label + "Socket"
	socket.bone_name = bone
	socket.position = position
	socket.rotation_degrees = rotation
	socket.scale = scale
	skeleton.add_child(socket)
	var visual := scene.instantiate() as Node3D
	visual.name = label
	socket.add_child(visual)


func find_type(node: Node, type_name: String) -> Node:
	if node.get_class() == type_name:
		return node
	for child in node.get_children():
		var found := find_type(child, type_name)
		if found:
			return found
	return null


func grip_transform() -> Transform3D:
	var y := BLADE_DIRECTION.normalized()
	var x := y.cross(Vector3.UP).normalized()
	var z := x.cross(y)
	var basis := Basis(x, y, z)
	return Transform3D(basis, GRIP_POINT - basis.y * SWORD_GRIP_HEIGHT)


func third_person_grip_transform() -> Transform3D:
	var y := Vector3(-0.30, 0.25, -0.90).normalized()
	var x := y.cross(Vector3.UP).normalized()
	var z := x.cross(y)
	return Transform3D(Basis(x, y, z), Vector3.ZERO)


func material(color: Color, roughness: float, metallic: float) -> StandardMaterial3D:
	var mat := StandardMaterial3D.new()
	mat.albedo_color = color
	mat.roughness = roughness
	mat.metallic = metallic
	return mat


func capture(label: String) -> void:
	await process_frame
	await RenderingServer.frame_post_draw
	var image := root.get_texture().get_image()
	var path := ProjectSettings.globalize_path("res://" + output + "/" + label + ".png")
	var error := image.save_png(path)
	if error != OK:
		push_error("Could not save " + path + ": " + str(error))
