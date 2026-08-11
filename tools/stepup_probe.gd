# Step-up verification harness (Phase 39C). Builds a real CharacterEntity with the real
# LocomotionComponent in the real Embermarket cell, walks it at the plaza dais, and reports whether it
# ended up on top.
#
# It exists because the alternative was shipping "reviewed against the API". The step is three
# TestMove probes against actual collision geometry — the one part of it that unit tests cannot reach
# is exactly the part that decides whether a player can walk onto the only raised ground in the realm.
#
# ⚠️ GDScript can only call C# methods whose signatures marshal (NOW.md invariant 10). Move(double,
# Vector3, bool, bool) does; this harness probes has_method first rather than assuming.
#
# Run:  Godot_..._console.exe --headless --path . --script res://tools/stepup_probe.gd
# Exits 0 when the body climbed the dais, 1 when it did not — so it is a gate, not a report.
extends SceneTree

const CELL := "res://scenes/regions/ember_crown/embermarket.tscn"

# The dais spans x -18..-6, z +9.5..+21.5 and stands 0.3 m proud. Start east of its edge on open
# ground and walk west into it.
# East of the dais edge (the dais spans x -18..-6, z +9.5..+21.5) on open ground, walking west into it.
const DAIS_START := Vector3(-4.0, 0.2, 15.5)

# In the aisle south of the bell tower (z +20, dead centre), walking north into 11 m of stone. The
# negative case: this must NOT be climbable, or the step-up has turned the realm into a staircase.
const TOWER_START := Vector3(0.0, 0.2, 16.0)

const CLIMB_EPSILON := 0.1  # comfortably above floor_snap_length, comfortably below the 0.3 dais
const STEPS := 240          # 4 seconds at 60 Hz — far longer than either walk needs


func _initialize() -> void:
	var packed: PackedScene = load(CELL)
	if packed == null:
		print("FAIL: cell did not load")
		quit(1)
		return
	root.add_child(packed.instantiate())

	# ⚠️ BOTH DIRECTIONS OR IT PROVES NOTHING. A step-up that climbs everything passes the first case
	# and turns every wall in the game into a staircase — which is the failure mode the third engine
	# move exists to prevent, and the only one a positive-only harness would ship.
	var climbed_dais := await _walk("the plaza dais", DAIS_START, Vector3(-1, 0, 0), true)
	var climbed_tower := await _walk("the bell tower", TOWER_START, Vector3(0, 0, 1), false)

	if climbed_dais and climbed_tower:
		print("PASS: the body climbs the 0.3 m dais and does not climb an 11 m tower")
		quit(0)
	else:
		print("FAIL: see above")
		quit(1)


# Walks a fresh body from `start` along `direction` and reports whether the outcome matched
# `expect_climb`. Returns true when it did.
func _walk(label: String, start: Vector3, direction: Vector3, expect_climb: bool) -> bool:
	var body := CharacterBody3D.new()
	body.set_script(load("res://src/Entities/CharacterEntity.cs"))
	var shape := CollisionShape3D.new()
	var capsule := CapsuleShape3D.new()
	capsule.radius = 0.4
	capsule.height = 1.8
	shape.shape = capsule
	shape.position = Vector3(0, 0.9, 0)
	body.add_child(shape)

	var locomotion = load("res://src/Movement/LocomotionComponent.cs").new()
	locomotion.name = "Locomotion"
	body.add_child(locomotion)
	root.add_child(body)
	body.global_position = start

	await process_frame
	await process_frame

	if not locomotion.has_method("Move"):
		print("FAIL: LocomotionComponent.Move does not marshal to GDScript")
		return false

	# ⚠️ SETTLE BEFORE MEASURING. The body spawns above the floor and falls the last few centimetres,
	# so a rise measured from the SPAWN height silently includes that drop: the first version of this
	# gate reported the 0.3 m dais as a 0.101 m rise and passed its 0.1 threshold by one millimetre.
	# A gate that would flake on a spawn height is not a gate.
	for _s in 30:
		locomotion.Move(1.0 / 60.0, Vector3.ZERO, false, false)
		await physics_frame

	var start_y: float = body.global_position.y
	for _i in STEPS:
		locomotion.Move(1.0 / 60.0, direction, false, false)
		await physics_frame

	var pos: Vector3 = body.global_position
	var rose: float = pos.y - start_y
	var climbed: bool = rose >= CLIMB_EPSILON
	var ok: bool = climbed == expect_climb
	print("%-16s start %s -> end %s   rose %.3f   climbed=%s expected=%s   %s"
		% [label, start, pos, rose, climbed, expect_climb, "ok" if ok else "WRONG"])
	body.queue_free()
	return ok
