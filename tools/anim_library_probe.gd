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
#   4. The clips stay where the capsule is. A loop that carries the hips forward and snaps them back
#      runs the body out from under the camera every stride; an idle that rests turned away from
#      forward makes a standing character face the camera. Both shipped, and both look fine in a
#      clip list. Measured on the real body: every loop ends where it starts and never strays far,
#      the idle faces forward and barely turns, and strafe_right is strafe_left's mirror image.
#
# Run:  Godot_..._console.exe --headless --path . --script res://tools/anim_library_probe.gd
extends SceneTree

const LIBRARY := "res://assets/models/animations/anim_meshy.res"
const MANIFEST := "res://assets/models/manifest.json"
const BODY := "res://assets/models/characters/chr_player_base.glb"

# The slots the game asks for by name. A clip missing here is a silent fallback to a bind pose.
const REQUIRED_SLOTS := ["idle", "idle_alert", "walk", "run", "sprint", "walk_back", "strafe_left",
	"strafe_right", "turn_left", "turn_right",
	"jump", "fall", "attack1", "attack2", "attack3", "heavy", "block", "parry", "dodge",
	"hit", "knockdown", "getup", "death"]

# The loops the locomotion tree blends between. Each has to stay on the spot: the capsule does the
# travelling. (idle_alert is the untouched source idle, kept for reference; it is printed, not held
# to these.)
const ON_THE_SPOT := ["idle", "walk", "run", "sprint", "walk_back", "strafe_left", "strafe_right",
	"combat_walk_fwd", "combat_walk_back"]

# A loop's last frame must land this close to its first (metres, in the ground plane), and the hips
# may never be further than this from where they started.
const LOOP_CLOSE := 0.02
const MAX_EXCURSION := 0.3

# The idle's hips, in degrees about the vertical axis: how far its average facing may be from
# straight ahead, and how far it may turn over the clip.
const IDLE_MEAN_YAW := 8.0
const IDLE_YAW_RANGE := 20.0

# How far a mirrored hand or foot may sit from the reflection of its opposite number (metres).
const MIRROR_TOLERANCE := 0.05

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
	await _check_every_humanoid_is_retargeted()
	await _check_it_moves_a_real_body(library)
	await _check_clips_stay_put(library)

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
	for slot in ["idle", "walk", "run", "sprint", "walk_back", "strafe_left", "strafe_right"]:
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
		# ⚠️ IN THE TREE, or the arms check below proves nothing. An AnimationPlayer that is not
		# inside the scene tree cannot resolve its root_node and advance() poses no bones at all —
		# so every body silently "passed". Caught by negative-testing the gate itself.
		root.add_child(scene)
		await process_frame
		var skeletons := scene.find_children("*", "Skeleton3D", true, false)
		if skeletons.is_empty():
			_failures.append("%s: no Skeleton3D but the manifest calls it HUMANOID" % entry.get("id", "?"))
		elif not _hands_stay_on_their_own_side(scene, skeletons[0], entry.get("id", "?")):
			pass
		elif str(skeletons[0].name) != "GeneralSkeleton":
			# This is the T-pose bug, caught at build time instead of by someone looking at a market.
			_failures.append("%s: skeleton is '%s', not GeneralSkeleton — it receives NO shared library and will T-pose"
				% [entry.get("id", "?"), skeletons[0].name])
		else:
			checked += 1
		scene.queue_free()
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


# ⚠️ THE ARMS-CROSSED GATE. A shared clip is authored against the profile's T-pose rest. A body whose
# own rest is an A-pose — which is how several of this cast were generated — keeps that A-pose unless
# `retarget/rest_fixer/fix_silhouette` is enabled, and the difference between the two rests is applied
# to every clip as a rotation. The visible result is both arms swinging THROUGH the torso and crossing
# in front of the chest like an X.
#
# It hid for a long time because the old shared library only ever supplied block/cast/channel, which
# almost never play; locomotion came from each body's own clip. The moment the library started driving
# locomotion it was on screen constantly, on the player, on Kael and on the goblin.
#
# Nothing else catches it: the rig gate passes (the skeleton IS GeneralSkeleton and has every bone),
# the clips resolve, --validate passes, and the only symptom is in a render. So it is measured here:
# play a clip and check each hand stayed on the side of the body it rests on.
func _hands_stay_on_their_own_side(scene: Node, sk: Skeleton3D, id: String) -> bool:
	var lh := sk.find_bone("LeftHand")
	var rh := sk.find_bone("RightHand")
	if lh < 0 or rh < 0:
		return true   # not a defect: some rigs have no hands, and the socket probe owns that.

	var aps := scene.find_children("*", "AnimationPlayer", true, false)
	var ap: AnimationPlayer
	if aps.is_empty():
		ap = AnimationPlayer.new()
		scene.add_child(ap)
		ap.root_node = ap.get_path_to(scene)
	else:
		ap = aps[0]
	ap.add_animation_library("probe", load(LIBRARY))

	var rest_left: float = sk.get_bone_global_pose(lh).origin.x
	var rest_right: float = sk.get_bone_global_pose(rh).origin.x
	if absf(rest_left - rest_right) < 0.05:
		return true   # the rest itself has the hands together; nothing to compare against.

	var ok := true
	for slot in ["idle", "walk"]:
		if not ap.has_animation("probe/%s" % slot):
			continue
		ap.play("probe/%s" % slot)
		ap.advance(0.5)
		var left: float = sk.get_bone_global_pose(lh).origin.x
		var right: float = sk.get_bone_global_pose(rh).origin.x
		if signf(left) != signf(rest_left) or signf(right) != signf(rest_right):
			_failures.append(
				"%s: '%s' crosses the arms — left hand rests at x=%+.3f and plays at x=%+.3f; right hand rests at x=%+.3f and plays at x=%+.3f. Its rest is not the profile silhouette; enable retarget/rest_fixer/fix_silhouette in its .import."
				% [id, slot, rest_left, left, rest_right, right])
			ok = false
	return ok


# Claim 4, on the real body: the clips stay where the capsule is, the idle faces forward, and the
# mirrored strafe is a mirror image.
#
# Posed with seek(time, true) rather than played, so the samples are at known times in the clip and
# the result does not depend on how fast this machine runs its frames.
func _check_clips_stay_put(library: AnimationLibrary) -> void:
	var body := (load(BODY) as PackedScene).instantiate()
	root.add_child(body)
	await process_frame

	var skeletons := body.find_children("*", "Skeleton3D", true, false)
	var players := body.find_children("*", "AnimationPlayer", true, false)
	if skeletons.is_empty() or players.is_empty():
		_failures.append("chr_player_base has no skeleton/player to measure the clips on")
		body.queue_free()
		return

	var sk: Skeleton3D = skeletons[0]
	var ap: AnimationPlayer = players[0]
	ap.add_animation_library("spot", library)
	var hips := sk.find_bone("Hips")
	if hips < 0:
		_failures.append("chr_player_base has no Hips bone")
		body.queue_free()
		return

	for slot in ON_THE_SPOT:
		if not library.has_animation(slot):
			_failures.append("the library has no '%s' clip to measure" % slot)
			continue
		var length: float = library.get_animation(slot).length
		var steps := maxi(int(length * 60.0), 8)
		ap.play("spot/%s" % slot)
		var start := _hips_on_ground(sk, ap, hips, 0.0)
		var excursion := 0.0
		for i in range(1, steps):
			var here := _hips_on_ground(sk, ap, hips, length * float(i) / float(steps))
			excursion = maxf(excursion, here.distance_to(start))
		var closing := _hips_on_ground(sk, ap, hips, maxf(length - 0.001, 0.0)).distance_to(start)
		print("  %-18s ends %.3f m from where it starts; hips stray at most %.3f m" % [slot, closing, excursion])
		if closing > LOOP_CLOSE:
			_failures.append("'%s' ends %.3f m from where it starts (limit %.2f m) — its root travel was not removed, so the body slides and snaps back every loop"
				% [slot, closing, LOOP_CLOSE])
		if excursion > MAX_EXCURSION:
			_failures.append("'%s' carries the hips %.3f m from where they start (limit %.2f m) — the body leaves its capsule"
				% [slot, excursion, MAX_EXCURSION])

	if library.has_animation("idle"):
		var idle_length: float = library.get_animation("idle").length
		var facing := _hips_facing(sk, ap, hips, "spot/idle", idle_length)
		print("  idle hips face %+.1f deg on average and turn through %.1f deg" % [facing[0], facing[1]])
		if absf(facing[0]) > IDLE_MEAN_YAW:
			_failures.append("the idle rests %+.1f deg off forward (limit %.0f deg) — a standing character faces away from where it is looking"
				% [facing[0], IDLE_MEAN_YAW])
		if facing[1] > IDLE_YAW_RANGE:
			_failures.append("the idle turns the hips through %.1f deg (limit %.0f deg) — it is still a look-around"
				% [facing[1], IDLE_YAW_RANGE])
	if library.has_animation("idle_alert"):
		var alert_length: float = library.get_animation("idle_alert").length
		var alert := _hips_facing(sk, ap, hips, "spot/idle_alert", alert_length)
		print("  idle_alert (the source idle, for reference) faces %+.1f deg and turns through %.1f deg"
			% [alert[0], alert[1]])

	_check_mirror(sk, ap, library)

	body.queue_free()
	await process_frame


# The hips' position over the ground (X and Z, in metres, in the world) with a clip posed at a time.
func _hips_on_ground(sk: Skeleton3D, ap: AnimationPlayer, hips: int, time: float) -> Vector2:
	ap.seek(time, true)
	var at: Vector3 = sk.global_transform * sk.get_bone_global_pose(hips).origin
	return Vector2(at.x, at.z)


# [mean yaw, yaw range] of the hips over a clip, in degrees. 0 is the rig's own forward (+Z).
func _hips_facing(sk: Skeleton3D, ap: AnimationPlayer, hips: int, clip: String, length: float) -> Array[float]:
	var rest_inverse: Basis = sk.get_bone_global_rest(hips).basis.orthonormalized().inverse()
	var steps := maxi(int(length * 30.0), 8)
	var yaws: Array[float] = []
	var sum_sin := 0.0
	var sum_cos := 0.0
	ap.play(clip)
	for i in steps:
		ap.seek(length * float(i) / float(steps), true)
		var turned: Basis = sk.get_bone_global_pose(hips).basis.orthonormalized() * rest_inverse
		var forward: Vector3 = turned * Vector3(0.0, 0.0, 1.0)
		var yaw := atan2(forward.x, forward.z)
		yaws.append(yaw)
		sum_sin += sin(yaw)
		sum_cos += cos(yaw)

	var mean := atan2(sum_sin, sum_cos)
	var low := 0.0
	var high := 0.0
	for yaw in yaws:
		var off := wrapf(yaw - mean, -PI, PI)
		low = minf(low, off)
		high = maxf(high, off)
	var result: Array[float] = [rad_to_deg(mean), rad_to_deg(high - low)]
	return result


# strafe_right is built by reflecting strafe_left. If the reflection is right, the right clip puts
# each hand and foot where the left clip puts the opposite one, flipped across the body. If the
# quaternion arithmetic or the bone swap were wrong the limbs would be twisted, and this is the only
# place short of a render that would say so.
func _check_mirror(sk: Skeleton3D, ap: AnimationPlayer, library: AnimationLibrary) -> void:
	if not library.has_animation("strafe_left") or not library.has_animation("strafe_right"):
		return

	var pairs := [["LeftFoot", "RightFoot"], ["LeftHand", "RightHand"], ["LeftLowerLeg", "RightLowerLeg"]]
	var length: float = library.get_animation("strafe_left").length
	var worst := 0.0
	var compared := 0
	for i in 6:
		var time := length * float(i) / 6.0
		ap.play("spot/strafe_left")
		ap.seek(time, true)
		var left_pose := {}
		for pair in pairs:
			for bone_name in pair:
				var left_bone := sk.find_bone(bone_name)
				if left_bone >= 0:
					left_pose[bone_name] = sk.get_bone_global_pose(left_bone).origin

		ap.play("spot/strafe_right")
		ap.seek(time, true)
		for pair in pairs:
			for side in 2:
				var mine: String = pair[side]
				var other: String = pair[1 - side]
				var right_bone := sk.find_bone(mine)
				if right_bone < 0 or not left_pose.has(other):
					continue
				var source: Vector3 = left_pose[other]
				var reflected := Vector3(-source.x, source.y, source.z)
				worst = maxf(worst, sk.get_bone_global_pose(right_bone).origin.distance_to(reflected))
				compared += 1

	if compared == 0:
		_failures.append("no hand or foot bones to compare strafe_right against strafe_left with")
		return
	print("  strafe_right mirrors strafe_left to within %.3f m across %d hand, shin and foot samples" % [worst, compared])
	if worst > MIRROR_TOLERANCE:
		_failures.append("strafe_right is not strafe_left's mirror image (a limb is %.3f m from where the reflection puts it, limit %.2f m) — the mirroring in build_meshy_anim_library.gd is wrong for this rig"
			% [worst, MIRROR_TOLERANCE])
