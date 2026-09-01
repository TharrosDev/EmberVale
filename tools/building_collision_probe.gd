extends SceneTree

## Exercises authored architecture with the real 0.4 m x 1.8 m player capsule.
## The visual QA proves openings exist; this proves their simplified collision agrees.

const PLAYER_RADIUS := 0.4
const PLAYER_HEIGHT := 1.8
const EYE_Y := PLAYER_HEIGHT * 0.5

var stage: Node3D
var player: CharacterBody3D
var failures: Array[String] = []


func _initialize() -> void:
	call_deferred("_run")


func _run() -> void:
	stage = Node3D.new()
	root.add_child(stage)
	player = CharacterBody3D.new()
	var collision := CollisionShape3D.new()
	var capsule := CapsuleShape3D.new()
	capsule.radius = PLAYER_RADIUS
	capsule.height = PLAYER_HEIGHT
	collision.shape = capsule
	player.add_child(collision)
	stage.add_child(player)
	await physics_frame
	await _check_ashfall_entry()
	await _check_open_workshop()
	await _check_ruin_breach()
	if failures.is_empty():
		print("building collision: PASS (player capsule enters intended openings, walls and floor hold)")
		quit(0)
	else:
		for failure in failures:
			push_error(failure)
		quit(1)


func _check_ashfall_entry() -> void:
	var house := await _load_subject("res://scenes/props/bld_ashfall_house.tscn")
	if house == null:
		return
	# Door is the first front slot: x=-2, outer face at +Z=4.
	player.global_position = Vector3(-2, EYE_Y, 6.2)
	var doorway_hit := player.move_and_collide(Vector3(0, 0, -4.5))
	var doorway_blocked := (doorway_hit != null and absf(doorway_hit.get_normal().y) < 0.5)
	if doorway_blocked or player.global_position.z > 2.2:
		failures.append("Ashfall doorway blocks the player capsule at z=%.3f (%s, normal=%s)" % [
			player.global_position.z, doorway_hit.get_collider().name if doorway_hit != null else "no hit",
			str(doorway_hit.get_normal()) if doorway_hit != null else "none"])
	# Adjacent front module must stop the same capsule.
	player.global_position = Vector3(0, EYE_Y, 6.2)
	var wall_hit := player.move_and_collide(Vector3(0, 0, -4.5))
	if wall_hit == null or player.global_position.z < 3.8:
		failures.append("Ashfall front wall can be walked through")
	# Once inside, the flush authored floor must catch the player without a threshold at the door.
	player.global_position = Vector3(-2, 2.2, 2.5)
	var floor_hit := player.move_and_collide(Vector3(0, -3.0, 0))
	if floor_hit == null or player.global_position.y < EYE_Y - 0.05:
		failures.append("Ashfall floor does not hold the player capsule")
	house.queue_free()
	await physics_frame


func _check_open_workshop() -> void:
	var workshop := await _load_subject("res://scenes/props/bld_workshop_open.tscn")
	if workshop == null:
		return
	player.global_position = Vector3(0, EYE_Y, 5.2)
	var hit := player.move_and_collide(Vector3(0, 0, -4.5))
	if hit != null or player.global_position.z > 1.0:
		failures.append("open workshop has an invisible barrier across its front")
	workshop.queue_free()
	await physics_frame


func _check_ruin_breach() -> void:
	var ruin := await _load_subject("res://scenes/props/bld_ruin_house.tscn")
	if ruin == null:
		return
	player.global_position = Vector3(0, EYE_Y, 4.0)
	var hit := player.move_and_collide(Vector3(0, 0, -3.5))
	if hit != null or player.global_position.z > 0.8:
		failures.append("ruin breach is blocked by obsolete invisible collision")
	ruin.queue_free()
	await physics_frame


func _load_subject(path: String) -> Node3D:
	var packed := load(path) as PackedScene
	if packed == null:
		failures.append(path + ": could not load")
		return null
	var subject := packed.instantiate() as Node3D
	if subject == null:
		failures.append(path + ": root is not Node3D")
		return null
	stage.add_child(subject)
	await physics_frame
	await physics_frame
	return subject
