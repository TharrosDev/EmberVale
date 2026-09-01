extends SceneTree

## Studio proof for the live C# NPC visual-kit path. Each actor is an Entity with its production
## GLB, CharacterAnimationComponent, and authored TemplateId; the same code used in settlements
## therefore attaches the kit. Run with:
##   godot --path . --script res://tools/npc_kit_shots.gd -- --output <folder>

const ENTITY_SCRIPT := preload("res://src/Entities/Entity.cs")
const ANIMATION_SCRIPT := preload("res://src/Animation/CharacterAnimationComponent.cs")

const CASES := [
	{"id":"kael", "template":"npc.kael", "model":"npc_kael", "important":true},
	{"id":"elder", "template":"npc.elder", "model":"npc_guild_rep", "important":true},
	{"id":"innkeeper_holt", "template":"npc.innkeeper", "model":"npc_innkeeper", "important":true},
	{"id":"aldreth_goods", "template":"npc.vendor_goods", "model":"npc_vendor", "important":true},
	{"id":"bryn_smith", "template":"npc.vendor_smith", "model":"npc_vendor", "important":true},
	{"id":"mirela_apothecary", "template":"npc.vendor_alch", "model":"npc_vendor", "important":true},
	{"id":"hesk_wayfarer", "template":"npc.traveller", "model":"npc_vendor", "important":true},
	{"id":"dawnwarden_captain", "template":"npc.dawnwarden_captain", "model":"npc_guild_rep", "important":true},
	{"id":"syndicate_broker", "template":"npc.syndicate_broker", "model":"npc_merchant_m", "important":true},
	{"id":"archive_keeper", "template":"npc.archive_keeper", "model":"npc_woman_dress", "important":true},
	{"id":"hunter_master", "template":"npc.hunter_master", "model":"npc_adventurer_f", "important":true},
	{"id":"emberbound_hierarch", "template":"npc.emberbound_hierarch", "model":"npc_hooded", "important":true},
	{"id":"clan_chief", "template":"npc.clan_chief", "model":"npc_guild_rep", "important":true},
	{"id":"mine_foreman", "template":"npc.bregan", "model":"npc_vendor", "important":false},
	{"id":"netmender", "template":"npc.hana", "model":"npc_woman_dress", "important":false},
	{"id":"ironmonger", "template":"npc.gilda", "model":"npc_innkeeper", "important":false},
	{"id":"curioseller", "template":"npc.quill", "model":"npc_townswoman", "important":false},
	{"id":"hunter_tracker", "template":"npc.hunter_tracker", "model":"npc_hooded", "important":false},
]

var output := "reports/3d/session-03-npc-handoff/visual-qa/studio"
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
	for entry in CASES:
		await render_case(entry)
	print("NPC kit shots written to ", output)
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
	environment.ambient_light_energy = 0.68
	environment.tonemap_mode = Environment.TONE_MAPPER_FILMIC
	world_environment.environment = environment
	stage.add_child(world_environment)
	var key := DirectionalLight3D.new()
	key.rotation_degrees = Vector3(-42, -32, 0)
	key.light_color = Color("ffd6a3")
	key.light_energy = 1.45
	key.shadow_enabled = true
	stage.add_child(key)
	var fill := DirectionalLight3D.new()
	fill.rotation_degrees = Vector3(-18, 145, 0)
	fill.light_color = Color("9eb8d7")
	fill.light_energy = 0.72
	stage.add_child(fill)
	var floor_mesh := MeshInstance3D.new()
	var plane := PlaneMesh.new()
	plane.size = Vector2(8, 8)
	floor_mesh.mesh = plane
	floor_mesh.material_override = material(Color("55524b"), 0.92, 0.0)
	stage.add_child(floor_mesh)
	camera = Camera3D.new()
	camera.current = true
	camera.fov = 53
	camera.near = 0.05
	stage.add_child(camera)


func render_case(entry: Dictionary) -> void:
	var entity := ENTITY_SCRIPT.new()
	entity.name = entry.id
	entity.set("DisplayName", entry.id)
	entity.set("TemplateId", entry.template)
	var model_path: String = "res://assets/models/characters/" + String(entry.model) + ".glb"
	var model := (load(model_path) as PackedScene).instantiate() as Node3D
	model.name = "Model"
	entity.add_child(model)
	var animation_component := ANIMATION_SCRIPT.new()
	animation_component.name = "Animation"
	animation_component.set("BodyMeshPath", "Model")
	entity.add_child(animation_component)
	stage.add_child(entity)
	await process_frame
	await process_frame
	var player := find_type(model, "AnimationPlayer") as AnimationPlayer
	play_slot(player, "idle")
	await process_frame
	await capture(entry.id + "__front_3q", Vector3(2.6, 1.2, 2.8), Vector3(0, 0.92, 0))
	await capture(entry.id + "__rear_3q", Vector3(-2.6, 1.2, -2.8), Vector3(0, 0.92, 0))
	if entry.important:
		await capture(entry.id + "__front", Vector3(0, 1.05, 3.2), Vector3(0, 0.9, 0))
		await capture(entry.id + "__rear", Vector3(0, 1.05, -3.2), Vector3(0, 0.9, 0))
		await capture(entry.id + "__left", Vector3(-3.2, 1.05, 0), Vector3(0, 0.9, 0))
		await capture(entry.id + "__right", Vector3(3.2, 1.05, 0), Vector3(0, 0.9, 0))
		await capture(entry.id + "__dialogue", Vector3(0.78, 1.50, 1.45), Vector3(0, 1.46, 0))
		play_slot(player, "walk")
		await process_frame
		await capture(entry.id + "__walk", Vector3(2.6, 1.2, 2.8), Vector3(0, 0.92, 0))
		play_slot(player, "run")
		await process_frame
		await capture(entry.id + "__run", Vector3(2.6, 1.2, 2.8), Vector3(0, 0.92, 0))
		if entry.template == "npc.kael":
			play_slot(player, "attack")
			await process_frame
			await capture(entry.id + "__weapon_pose", Vector3(2.6, 1.2, 2.8), Vector3(0, 0.92, 0))
	entity.queue_free()
	await process_frame


func play_slot(player: AnimationPlayer, slot: String) -> void:
	if not player:
		return
	var aliases := {
		"idle": ["idle"], "walk": ["walk"], "run": ["run", "jog"],
		"attack": ["attack", "slash", "dagger"],
	}
	for candidate in player.get_animation_list():
		var lowered := String(candidate).to_lower()
		for alias in aliases.get(slot, []):
			if lowered.contains(alias):
				player.play(candidate)
				return


func find_type(node: Node, type_name: String) -> Node:
	if node.get_class() == type_name:
		return node
	for child in node.get_children():
		var found := find_type(child, type_name)
		if found:
			return found
	return null


func material(color: Color, roughness: float, metallic: float) -> StandardMaterial3D:
	var mat := StandardMaterial3D.new()
	mat.albedo_color = color
	mat.roughness = roughness
	mat.metallic = metallic
	return mat


func capture(label: String, position: Vector3, target: Vector3) -> void:
	camera.global_position = position
	camera.look_at(target, Vector3.UP)
	await process_frame
	await RenderingServer.frame_post_draw
	var image := root.get_texture().get_image()
	var path := ProjectSettings.globalize_path("res://" + output + "/" + label + ".png")
	var error := image.save_png(path)
	if error != OK:
		push_error("Could not save " + path + ": " + str(error))
