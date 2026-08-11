# Cell verification harness (Phase 38K second pass, kept for 38L and after). Instantiates a region
# cell in the real engine, lights it two ways, and writes PNGs — so a cell's look is judged from what
# Godot actually renders rather than from a Blender viewport or a reading of the .tscn.
#
# It earns its place: a .tscn reads fine while looking wrong. One run of this found camping tents
# standing in for market stalls, the district's own banner colliderless in the middle of the only
# route in, guessed collider sizes, and a hi-vis construction worker in a medieval market. Three
# readings of the file had found none of them.
#
# Run:  Godot_..._console.exe --path . --script res://tools/market_shots.gd
#
# Point it at another cell by changing the load path and the SHOTS list. Its output under
# tools/shots/ is DISPOSABLE — regenerate it, do not commit it; the .gdignore there keeps Godot from
# importing the PNGs as textures if any are left lying about.
extends SceneTree

const SHOTS := [
	# name, camera position, look-at target
	["01_approach",   Vector3(0, 2.4, -34),    Vector3(0, 3, 6)],
	["02_aisle",      Vector3(0, 1.75, -16),   Vector3(0, 2, 18)],
	["03_midmarket",  Vector3(2.5, 1.75, -1),  Vector3(-12, 2, 14)],
	["04_plaza",      Vector3(-3, 2.2, 8),     Vector3(-14, 1.5, 17)],
	["05_tower",      Vector3(0, 1.75, 6),     Vector3(0, 7, 22)],
	["06_overhead",   Vector3(-34, 32, -38),   Vector3(0, 0, 2)],
	# 38L: a customer's eye on a stall, to check the merchant is behind the boards and facing out.
	["07_weststall",  Vector3(-3.4, 1.7, -3),  Vector3(-9, 1.5, -3)],
	["08_eaststall",  Vector3(3.4, 1.7, -14),  Vector3(9, 1.5, -14)],
	["09_crosslane",  Vector3(-3.6, 1.7, -8.5), Vector3(-13, 1.5, -9)],
	# 39C: the plaza dais — the realm's only raised ground, and the one surface the step-up exists
	# for. Shot 10 is the walk-up from the aisle (where the 0.3 m edge is either legible or a trip
	# hazard), 11 is eye level ON the dais with the well and benches, 12 the kerb from a metre away.
	["10_daisapproach", Vector3(-4.0, 1.7, 13.0), Vector3(-13, 1.2, 16.5)],
	["11_daistop",    Vector3(-9.5, 2.0, 15.5),  Vector3(-15, 1.5, 16.5)],
	["12_daiskerb",   Vector3(-5.4, 0.9, 15.5),  Vector3(-9, 0.35, 15.5)],
]

func _initialize() -> void:
	var packed: PackedScene = load("res://scenes/regions/ember_crown/embermarket.tscn")
	if packed == null:
		print("FAIL: embermarket.tscn did not load")
		quit(1)
		return

	var cell: Node = packed.instantiate()
	root.add_child(cell)
	print("instantiated: %d descendant nodes" % _count(cell))

	var env := WorldEnvironment.new()
	var e := Environment.new()
	var sky := Sky.new()
	var mat := ProceduralSkyMaterial.new()
	mat.sky_top_color = Color(0.42, 0.45, 0.52)
	mat.sky_horizon_color = Color(0.72, 0.63, 0.5)
	mat.ground_bottom_color = Color(0.22, 0.2, 0.18)
	mat.ground_horizon_color = Color(0.4, 0.36, 0.3)
	sky.sky_material = mat
	e.background_mode = Environment.BG_SKY
	e.sky = sky
	e.ambient_light_source = Environment.AMBIENT_SOURCE_SKY
	e.ambient_light_energy = 0.6
	e.glow_enabled = true
	e.fog_enabled = true
	e.fog_light_color = Color(0.62, 0.58, 0.52)
	e.fog_density = 0.004
	env.environment = e
	root.add_child(env)

	var sun := DirectionalLight3D.new()
	sun.rotation_degrees = Vector3(-38, 42, 0)
	sun.light_color = Color(1, 0.9, 0.76)
	sun.light_energy = 1.5
	root.add_child(sun)

	var cam := Camera3D.new()
	cam.fov = 68
	root.add_child(cam)
	cam.current = true

	await process_frame
	await process_frame

	for pass_name in ["day", "dusk"]:
		if pass_name == "dusk":
			sun.light_energy = 0.12
			sun.light_color = Color(0.5, 0.5, 0.72)
			mat.sky_top_color = Color(0.06, 0.07, 0.12)
			mat.sky_horizon_color = Color(0.2, 0.15, 0.14)
			e.ambient_light_energy = 0.12
		for shot in SHOTS:
			cam.global_position = shot[1]
			cam.look_at(shot[2], Vector3.UP)
			for _i in range(8):
				await process_frame
			var img: Image = root.get_texture().get_image()
			var path := "res://tools/shots/%s_%s.png" % [pass_name, shot[0]]
			DirAccess.make_dir_recursive_absolute(ProjectSettings.globalize_path("res://tools/shots"))
			var err := img.save_png(path)
			print("%s -> %s" % [path, "ok" if err == OK else str(err)])

	print("done")
	quit(0)

func _count(n: Node) -> int:
	var total := 1
	for c in n.get_children():
		total += _count(c)
	return total
