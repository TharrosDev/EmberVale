# Animation contract probe: is the shared library whole, does every humanoid actually RECEIVE it,
# and do its clips drive the legs of a real body?
#
# ⚠️ THE HOLE THIS CLOSES IS THE SHARPEST ONE IN THE ASSET CONTRACT. Before it, nothing anywhere
# asserted that a character could resolve a single gameplay clip. meshy_rig_probe.gd PRINTS a rig's
# clip list and asserts nothing about it, so a body that ships no clips imports cleanly, compiles,
# passes the tests, passes --validate, and then stands in the market in its bind pose forever.
# docs/3D_ASSETS.md names that as the only symptom an unresolved rig ever has, and npc_woman_dress
# did exactly it from the day she was adopted until somebody looked at her.
#
# Three claims, in order of how expensive they are to get wrong:
#   1. The library holds every slot gameplay names, and its clips carry LEG tracks. A library that
#      lost its legs is the upper-body one all over again, and it looks identical in a log.
#   2. Every active humanoid rig receives it — i.e. is named GeneralSkeleton, which is the retarget's
#      own marker.
#   3. Playing a library clip on a real body moves that body's legs.
#
# Run:  Godot_..._console.exe --headless --path . --script res://tools/anim_library_probe.gd
extends SceneTree

const LIBRARY := "res://assets/models/animations/anim_meshy.res"
const MANIFEST := "res://assets/models/manifest.json"
const BODY := "res://assets/models/characters/chr_player_base.glb"

# The slots the game asks for by name. A clip missing here is a silent fallback to a bind pose.
const REQUIRED_SLOTS := ["idle", "walk", "run", "sprint", "walk_back", "turn_left", "turn_right",
	"jump", "fall", "attack1", "attack2", "attack3", "heavy", "block", "parry", "dodge",
	"hit", "knockdown", "getup", "death"]

const LEG_BONES := ["LeftUpperLeg", "RightUpperLeg", "LeftLowerLeg", "RightLowerLeg",
	"LeftFoot", "RightFoot"]

var _failures: Array[String] = []


func _initialize() -> void:
	var library: AnimationLibrary = load(LIBRARY)
	if library == null:
		print("FAIL: %s did not load — run tools/build_meshy_anim_library.gd" % LIBRARY)
		quit(1)
		return

	_check_library(library)
	_check_every_humanoid_is_retargeted()
	await _check_it_moves_a_real_body(library)

	print("---")
	if _failures.is_empty():
		print("PASS: the shared animation contract holds")
		quit(0)
	else:
		for f in _failures:
			print("FAIL: %s" % f)
		quit(1)


func _check_library(library: AnimationLibrary) -> void:
	var names := library.get_animation_list()
	var have := {}
	for n in names:
		have[str(n)] = true

	for slot in REQUIRED_SLOTS:
		if not have.has(slot):
			_failures.append("the library has no '%s' clip; gameplay names that slot" % slot)

	print("library holds %d clips" % names.size())

	# The legs are the whole reason this library exists rather than the old one. Checked on the
	# locomotion clips, where a missing leg track is not a stylistic choice.
	for slot in ["walk", "run", "idle"]:
		if not have.has(slot):
			continue
		var anim: Animation = library.get_animation(slot)
		var legs := {}
		for t in anim.get_track_count():
			var path := str(anim.track_get_path(t))
			if ":" in path and path.get_slice(":", 1) in LEG_BONES:
				legs[path.get_slice(":", 1)] = true
		if legs.size() < LEG_BONES.size():
			_failures.append(
				"clip '%s' tracks only %d of %d leg bones — this library has been stripped like the old one"
				% [slot, legs.size(), LEG_BONES.size()])
		else:
			print("  %-8s tracks all %d leg bones" % [slot, legs.size()])

	# A locomotion clip that does not loop stops dead at its last frame and the actor freezes mid-
	# stride. Visible instantly in play and invisible everywhere else.
	for slot in ["idle", "walk", "run", "sprint"]:
		if have.has(slot) and library.get_animation(slot).loop_mode == Animation.LOOP_NONE:
			_failures.append("clip '%s' does not loop; it would freeze on its last frame" % slot)


func _check_every_humanoid_is_retargeted() -> void:
	var text := FileAccess.get_file_as_string(MANIFEST)
	if text.is_empty():
		_failures.append("could not read %s" % MANIFEST)
		return
	var parsed = JSON.parse_string(text)
	var entries: Array = parsed if parsed is Array else []
	if parsed is Dictionary:
		for key in ["models", "entries", "assets"]:
			if parsed.has(key) and parsed[key] is Array:
				entries = parsed[key]
				break

	var checked := 0
	for entry in entries:
		if entry.get("type", "") != "HUMANOID" or entry.get("status", "active") != "active":
			continue
		var path: String = entry.get("path", "")
		if path.is_empty() or not ResourceLoader.exists(path):
			_failures.append("%s: '%s' does not resolve" % [entry.get("id", "?"), path])
			continue
		var scene := (load(path) as PackedScene).instantiate()
		var skeletons := scene.find_children("*", "Skeleton3D", true, false)
		if skeletons.is_empty():
			_failures.append("%s: no Skeleton3D but the manifest calls it HUMANOID" % entry.get("id", "?"))
		elif str(skeletons[0].name) != "GeneralSkeleton":
			# This is the T-pose bug, caught at build time instead of by someone looking at a market.
			_failures.append("%s: skeleton is '%s', not GeneralSkeleton — it receives NO shared library and will T-pose"
				% [entry.get("id", "?"), skeletons[0].name])
		else:
			checked += 1
		scene.free()
	print("%d humanoid rig(s) are retargeted and will receive the library" % checked)


func _check_it_moves_a_real_body(library: AnimationLibrary) -> void:
	var body := (load(BODY) as PackedScene).instantiate()
	root.add_child(body)
	await process_frame

	var skeletons := body.find_children("*", "Skeleton3D", true, false)
	var players := body.find_children("*", "AnimationPlayer", true, false)
	if skeletons.is_empty() or players.is_empty():
		_failures.append("chr_player_base has no skeleton/player")
		body.queue_free()
		return

	var sk: Skeleton3D = skeletons[0]
	var ap: AnimationPlayer = players[0]
	ap.add_animation_library("probe", library)

	var lul := sk.find_bone("LeftUpperLeg")
	var rul := sk.find_bone("RightUpperLeg")
	if lul < 0 or rul < 0:
		_failures.append("chr_player_base has no upper-leg bones")
		body.queue_free()
		return

	for slot in ["walk", "run", "attack1"]:
		ap.play("probe/%s" % slot)
		await process_frame
		var base_l: Quaternion = sk.get_bone_pose_rotation(lul)
		var base_r: Quaternion = sk.get_bone_pose_rotation(rul)
		var swing := 0.0
		for i in 45:
			await process_frame
			swing = max(swing, base_l.angle_to(sk.get_bone_pose_rotation(lul)))
			swing = max(swing, base_r.angle_to(sk.get_bone_pose_rotation(rul)))
		print("  '%s' swings the legs %.1f deg on the real body" % [slot, rad_to_deg(swing)])
		if slot in ["walk", "run"] and rad_to_deg(swing) < 5.0:
			_failures.append("'%s' barely moves the legs (%.1f deg) — it is not driving this rig"
				% [slot, rad_to_deg(swing)])

	body.queue_free()
	await process_frame
