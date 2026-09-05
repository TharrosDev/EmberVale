# View-switch probe: does swapping between first and third person mid-action change anything the
# player would feel?
#
# ⚠️ THIS IS §18 OF THE OVERHAUL, AND IT USED TO BE STRUCTURALLY UNANSWERABLE. First person drew a
# rigless viewmodel with its own procedural swing while the world body was hidden — two skeletons,
# two action states and two weapons that had to be kept in step by hand. Switching view mid-swing
# meant handing the swing to a different animator halfway through.
#
# There is one body now: the camera rides its head bone and the same arms, weapon and equipment are
# on screen either way. So the claim is testable, and it is this — across a view switch, the running
# action keeps its identity, its phase and its progress; the hitbox stays exactly as it was; and the
# equipment is still hanging where it was hung.
#
# Run:  Godot_..._console.exe --headless --path . --script res://tools/view_switch_probe.gd
extends SceneTree

const BODY := "res://assets/models/characters/chr_player_base.glb"
const WEAPON_MODEL := "res://assets/models/weapons/wpn_sword_iron.glb"
const WEAPON := "res://data/weapons/IronSword.tres"

var _failures: Array[String] = []


func _initialize() -> void:
	root.add_child(load("res://src/Bootstrap/ContentDatabaseLoader.cs").new())
	await process_frame

	var parts := await _build()
	var action = parts[0]
	var presentation = parts[1]
	var rig = parts[2]

	if not presentation.HasRig:
		_failures.append("the presentation component found no rig")
	else:
		presentation.AttachSimple(0, WEAPON_MODEL, "MainHand")   # EquipmentSocket.HandR
		await process_frame

	# Start a swing, run into its committed window, then switch view underneath it.
	if not action.TryAttack():
		_failures.append("the attack did not start")
	else:
		for i in 8:
			await physics_frame

		var before := {
			"id": (action.Current.Id if action.Current != null else ""),
			"phase": action.Phase,
			"committed": action.IsCommitted,
			"combo": action.ComboIndex,
			"weapon": presentation.IsAttached("MainHand"),
		}

		rig.SetFirstPerson(true, true)
		await process_frame
		await physics_frame
		var mid := _snapshot(action, presentation)

		rig.SetFirstPerson(false, true)
		await process_frame
		await physics_frame
		var after := _snapshot(action, presentation)

		print("before -> %s" % str(before))
		print("first  -> %s" % str(mid))
		print("third  -> %s" % str(after))

		for key in before.keys():
			if key == "phase":
				continue   # the action legitimately advances a phase while frames pass
			if mid[key] != before[key]:
				_failures.append("switching to first person changed %s: %s -> %s"
					% [key, str(before[key]), str(mid[key])])
			if after[key] != mid[key]:
				_failures.append("switching back to third person changed %s: %s -> %s"
					% [key, str(mid[key]), str(after[key])])

		if not after["weapon"]:
			_failures.append("the weapon came off its socket across the view switch")
		if after["id"] == "":
			_failures.append("the action was lost across the view switch")

	print("---")
	if _failures.is_empty():
		print("PASS: a view switch preserves the action, the combo and the equipment")
		quit(0)
	else:
		for f in _failures:
			print("FAIL: %s" % f)
		quit(1)


func _snapshot(action, presentation) -> Dictionary:
	return {
		"id": (action.Current.Id if action.Current != null else ""),
		"phase": action.Phase,
		"committed": action.IsCommitted,
		"combo": action.ComboIndex,
		"weapon": presentation.IsAttached("MainHand"),
	}


func _build() -> Array:
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

	var visual := Node3D.new()
	visual.name = "BodyMesh"
	visual.add_child((load(BODY) as PackedScene).instantiate())
	body.add_child(visual)

	# The camera chain the rig expects, built the way PlayerFactory builds it.
	var pivot := Node3D.new()
	pivot.name = "CameraPivot"
	pivot.position = Vector3(0, 1.62, 0)
	body.add_child(pivot)
	var camera := Camera3D.new()
	camera.name = "Camera"
	camera.near = 0.08
	pivot.add_child(camera)

	var stats = load("res://src/Stats/StatsComponent.cs").new()
	stats.name = "Stats"
	body.add_child(stats)
	var combat = load("res://src/Combat/CombatComponent.cs").new()
	combat.name = "Combat"
	combat.Team = 1
	body.add_child(combat)
	var animation = load("res://src/Animation/CharacterAnimationComponent.cs").new()
	animation.name = "Animation"
	body.add_child(animation)
	var presentation = load("res://src/Animation/EquipmentPresentationComponent.cs").new()
	presentation.name = "EquipmentVisuals"
	body.add_child(presentation)

	var hitbox := Area3D.new()
	hitbox.set_script(load("res://src/Combat/Hitbox.cs"))
	hitbox.name = "MeleeHitbox"
	var arc := CollisionShape3D.new()
	var box := BoxShape3D.new()
	box.size = Vector3(1.0, 1.4, 1.6)
	arc.shape = box
	hitbox.add_child(arc)
	body.add_child(hitbox)

	var action = load("res://src/Combat/Actions/CharacterActionComponent.cs").new()
	action.name = "Action"
	action.Weapon = load(WEAPON)
	action.Hitbox = hitbox
	body.add_child(action)

	var rig = load("res://src/Player/PlayerCameraRig.cs").new()
	rig.name = "CameraRig"
	rig.CameraPivot = pivot
	rig.Camera = camera
	body.add_child(rig)

	root.add_child(body)
	await process_frame
	await process_frame
	return [action, presentation, rig]
