# Builds assets/models/animations/anim_meshy.res — the full-body shared animation library.
#
# ⚠️ THIS IS THE LIBRARY extract_anim_library.gd COULD NOT BUILD, AND THE DIFFERENCE IS THE POINT.
# That one strips every position/scale track AND all eight leg bones, leaving an upper-body,
# rotation-only pose set that answers three gameplay slots. It has to: its source is a Rigify rig
# (root -> Hips) retargeted onto Quaternius bodies whose own rig is Root -> Body -> Hips, so the hip
# translation lands on top of a lift the body already has and stands the actor 1.63 m in the air with
# its legs strung out below. Nothing logged; it was only ever visible in a render.
#
# These clips come off a MESHY rig, which is the same rig family as 31 of the 33 humanoid bodies —
# meshy_adopt.py fingerprints the animation sources and the character bodies to the same shape hash,
# 's3_c188e7a9', and therefore the same bone map. There is no hierarchy mismatch to compensate for,
# so nothing is stripped and the library carries legs.
#
# Clip names come from the FILENAME, not from Meshy's action name: anim_walk.glb -> "walk". The
# library's vocabulary is Embervale's gameplay slots, so AnimationClips resolves them exactly rather
# than through an alias table guessing at "Casual_Walk".
#
# ⚠️ THE CLIPS ARE NOT STORED AS THEY ARRIVE (the 2026-10 camera/animation pass). Three things are
# done to them here, and each one was a visible defect before it:
#
#   1. ROOT TRAVEL IS REMOVED. Meshy bakes the distance a clip covers into the Hips position track:
#      the sprint carries the hips 3.18 m forward per loop and snaps them back. The game moves the
#      capsule itself, so that travel was played ON TOP of the real movement — the body ran out from
#      under the camera and teleported home every stride. The Hips X/Z of every clip in DETREND is
#      detrended linearly so it ends where it starts. Y is left alone (a jump still rises), and so are
#      death, knockdown and getup, whose travel IS the animation.
#   2. THE IDLE IS CALMED AND TURNED TO FACE FORWARD. The source idle is a look-around whose hips
#      rest about 40 degrees off forward and sweep through 90, so a standing character appeared to be
#      facing the camera. The idle slot is the original with every key pulled to IDLE_MOTION of its
#      motion about the clip's mean pose, then turned as a whole so the mean hips yaw is zero. The
#      untouched original is kept as "idle_alert".
#   3. strafe_right IS MIRRORED from strafe_left; Meshy shipped only the one.
#
# Every one of these prints what it did, per clip, so the log is the check: hips travel removed (in
# metres), idle mean yaw and range before and after.
#
# Re-run whenever the sources or their .import retarget settings change — the committed .res is
# otherwise unreproducible. It writes anim_meshy.res and nothing else.
#
# Run:  Godot_..._console.exe --headless --path . --script res://tools/build_meshy_anim_library.gd
extends SceneTree

const SOURCE_DIR := "res://assets/models/animations/meshy"
const DEST := "res://assets/models/animations/anim_meshy.res"

# Every mapped bone the retarget is expected to produce. A clip that reaches none of these retargeted
# is a clip whose bone map did not apply, which is the silent failure docs/3D_ASSETS.md calls the only
# symptom of an unresolved rig — it imports clean, compiles, validates, and then T-poses.
const PROFILE_BONES := ["Hips", "Spine", "Chest", "Head", "LeftUpperArm", "RightUpperArm",
	"LeftUpperLeg", "RightUpperLeg", "LeftFoot", "RightFoot"]

# Locomotion and stance clips loop; one-shot actions do not. Getting this wrong is visible
# immediately (a walk that plays once and freezes) rather than subtly, so it is a table.
const LOOPS := ["idle", "idle_alert", "walk", "run", "sprint", "walk_back", "strafe_left",
	"strafe_right", "combat_walk_fwd", "combat_walk_back", "block", "fall"]

# The clips whose Hips X/Z travel is removed. Not death, knockdown or getup: there the body really
# does end up somewhere else, and the capsule does not move to match.
const DETREND := ["sprint", "walk_back", "strafe_left", "walk", "run", "combat_walk_fwd",
	"combat_walk_back", "dodge", "hit", "jump", "attack3", "heavy_overhead"]

const HIPS := "Hips"
const IDLE := "idle"
const IDLE_ALERT := "idle_alert"
const STRAFE_LEFT := "strafe_left"
const STRAFE_RIGHT := "strafe_right"

# How much of the idle's motion about its mean pose survives.
const IDLE_MOTION := 0.15

# Samples per second when a track is averaged. Keys cannot be averaged directly: the importer's
# optimiser drops the ones it can interpolate, so they are not evenly spaced in time.
const SAMPLE_RATE := 30.0

var _failures: Array[String] = []


func _initialize() -> void:
	var dir := DirAccess.open(SOURCE_DIR)
	if dir == null:
		print("FAIL: %s does not exist" % SOURCE_DIR)
		quit(1)
		return

	var out := AnimationLibrary.new()
	var files := dir.get_files()
	files.sort()
	var added := 0
	var total_tracks := 0

	for file in files:
		if not file.ends_with(".glb"):
			continue
		var path := "%s/%s" % [SOURCE_DIR, file]
		var packed: PackedScene = load(path)
		if packed == null:
			_failures.append("%s did not load" % file)
			continue

		var scene: Node = packed.instantiate()
		var skeletons := scene.find_children("*", "Skeleton3D", true, false)
		var players := scene.find_children("*", "AnimationPlayer", true, false)
		if skeletons.is_empty() or players.is_empty():
			_failures.append("%s has no Skeleton3D/AnimationPlayer" % file)
			scene.free()
			continue

		var skeleton: Skeleton3D = skeletons[0]
		if str(skeleton.name) != "GeneralSkeleton":
			# The retarget did not run. Its ONLY other symptom is a T-posing actor at runtime.
			_failures.append("%s imported with skeleton '%s', not GeneralSkeleton — its .import is not retargeting"
				% [file, skeleton.name])
			scene.free()
			continue

		# The slot name is the filename: anim_walk.glb -> walk.
		var slot := file.get_basename()
		if slot.begins_with("anim_"):
			slot = slot.substr(5)

		var library: AnimationLibrary = (players[0] as AnimationPlayer).get_animation_library("")
		var names := library.get_animation_list()
		if names.size() != 1:
			_failures.append("%s holds %d clips; one file is one clip" % [file, names.size()])
			scene.free()
			continue

		var anim: Animation = library.get_animation(names[0]).duplicate(true)
		anim.resource_path = ""

		var mapped := _mapped_bones(anim)
		if mapped == 0:
			_failures.append("%s: no track addresses a profile bone — the bone map did not apply" % file)
			scene.free()
			continue

		if _has_compressed_track(anim):
			# A compressed track cannot have its keys rewritten, and every pass below rewrites keys.
			_failures.append("%s: its tracks are compressed; turn animation compression off in its .import" % file)
			scene.free()
			continue

		# Position tracks are stored normalised (the .import's normalize_position_tracks); the
		# skeleton's motion scale is what turns them back into metres, here only for the log.
		var metres := skeleton.motion_scale
		# The hips' rest orientation, so "which way do the hips face" can be asked of a pose key.
		var hips_bone := skeleton.find_bone(HIPS)
		var hips_rest := Quaternion.IDENTITY
		if hips_bone >= 0:
			hips_rest = skeleton.get_bone_rest(hips_bone).basis.get_rotation_quaternion()

		if slot in LOOPS:
			anim.loop_mode = Animation.LOOP_LINEAR

		_report(slot, anim, mapped)

		if slot in DETREND:
			_detrend_hips(anim, slot, metres)

		if slot == IDLE:
			# The original, exactly as it arrived, under its own name.
			var alert: Animation = anim.duplicate(true)
			alert.resource_path = ""
			out.add_animation(IDLE_ALERT, alert)
			added += 1
			total_tracks += alert.get_track_count()
			_report(IDLE_ALERT, alert, mapped)
			_calm_idle(anim, hips_rest)

		out.add_animation(slot, anim)
		added += 1
		total_tracks += anim.get_track_count()

		if slot == STRAFE_LEFT and not out.has_animation(STRAFE_RIGHT):
			var mirrored := _mirrored(anim)
			out.add_animation(STRAFE_RIGHT, mirrored)
			added += 1
			total_tracks += mirrored.get_track_count()
			_report(STRAFE_RIGHT, mirrored, _mapped_bones(mirrored))
			print("    mirror  %-18s from %s: Left/Right tracks swapped, rotations and positions reflected in X"
				% [STRAFE_RIGHT, STRAFE_LEFT])

		scene.free()

	if added == 0:
		_failures.append("no clips were added at all")

	for required in [IDLE, IDLE_ALERT, STRAFE_LEFT, STRAFE_RIGHT]:
		if not out.has_animation(required):
			_failures.append("the library came out with no '%s' clip" % required)

	if not _failures.is_empty():
		for f in _failures:
			print("FAIL: %s" % f)
		quit(1)
		return

	var err := ResourceSaver.save(out, DEST)
	print("---")
	print("%d clips, %d tracks -> %s (%s)" % [added, total_tracks, DEST, "ok" if err == OK else str(err)])
	if err != OK:
		quit(1)
		return
	print("PASS: the full-body Meshy library built")
	quit(0)


func _report(slot: String, anim: Animation, mapped: int) -> void:
	print("  %-20s %5.2fs  %2d tracks  %2d mapped bones  %s"
		% [slot, anim.length, anim.get_track_count(), mapped,
		   "loop" if anim.loop_mode == Animation.LOOP_LINEAR else "once"])


# How many of this clip's tracks address a SkeletonProfileHumanoid bone. Counting rather than
# asserting a total: not every clip animates every bone, but a clip that reaches none of them was
# never retargeted.
func _mapped_bones(anim: Animation) -> int:
	var seen := {}
	for t in anim.get_track_count():
		var bone := _bone_of(anim, t)
		if bone in PROFILE_BONES:
			seen[bone] = true
	return seen.size()


# The bone a track drives: the part of "%GeneralSkeleton:Hips" after the colon. Empty for a track
# that is not a bone track.
func _bone_of(anim: Animation, track: int) -> String:
	var path := str(anim.track_get_path(track))
	if ":" not in path:
		return ""
	return path.get_slice(":", 1)


func _bone_track(anim: Animation, bone: String, type: int) -> int:
	for t in anim.get_track_count():
		if anim.track_get_type(t) == type and _bone_of(anim, t) == bone:
			return t
	return -1


func _has_compressed_track(anim: Animation) -> bool:
	for t in anim.get_track_count():
		if anim.track_is_compressed(t):
			return true
	return false


# Removes the distance a clip covers from its Hips position track, on X and Z only.
#
# Linear on purpose: a loop covers ground at a steady rate, so taking a straight line from the first
# key to the last out of every key leaves exactly the sway and bob and makes the last key land on the
# first. A one-shot (a dodge, a jump) does not travel at a steady rate, so some excursion is left in
# the middle of it; that residual is printed, and is what to look at if a dodge still slides.
func _detrend_hips(anim: Animation, slot: String, metres: float) -> void:
	var track := _bone_track(anim, HIPS, Animation.TYPE_POSITION_3D)
	if track < 0:
		print("    detrend %-18s has no Hips position track; nothing to remove" % slot)
		return

	var count := anim.track_get_key_count(track)
	if count < 2:
		print("    detrend %-18s has %d Hips position key(s); nothing to remove" % [slot, count])
		return

	var t0 := anim.track_get_key_time(track, 0)
	var t1 := anim.track_get_key_time(track, count - 1)
	var span := maxf(t1 - t0, 0.0001)
	var first: Vector3 = anim.track_get_key_value(track, 0)
	var last: Vector3 = anim.track_get_key_value(track, count - 1)
	var travel := Vector3(last.x - first.x, 0.0, last.z - first.z)

	var excursion := 0.0
	for k in count:
		var u := (anim.track_get_key_time(track, k) - t0) / span
		var value: Vector3 = anim.track_get_key_value(track, k)
		value.x -= travel.x * u
		value.z -= travel.z * u
		anim.track_set_key_value(track, k, value)
		excursion = maxf(excursion, Vector2(value.x - first.x, value.z - first.z).length())

	print("    detrend %-18s hips travel removed: x %+.3f m, z %+.3f m (%.3f m over %.2fs); residual excursion %.3f m"
		% [slot, travel.x * metres, travel.z * metres, travel.length() * metres, span, excursion * metres])


# Turns the look-around idle into a quiet stand that faces forward.
#
# Every rotation track is pulled to IDLE_MOTION of its motion about that track's own mean, which
# keeps the clip's character (the breath, the weight shift) at a size that reads as standing still.
# The Hips position track is pulled in by the same amount, so the feet the legs are still reaching
# for stay under the body. Then the whole pose is turned about the vertical axis so the mean hips yaw
# is zero: the Hips rotation is pre-multiplied and the Hips position rotated about the origin, which
# is a rigid turn of the character and moves nothing relative to anything else.
func _calm_idle(anim: Animation, hips_rest: Quaternion) -> void:
	var hips_rotation := _bone_track(anim, HIPS, Animation.TYPE_ROTATION_3D)
	var hips_position := _bone_track(anim, HIPS, Animation.TYPE_POSITION_3D)
	if hips_rotation < 0:
		_failures.append("idle has no Hips rotation track; it cannot be turned to face forward")
		return

	var before := _hips_yaw(anim, hips_rotation, hips_rest)

	for t in anim.get_track_count():
		var type := anim.track_get_type(t)
		if type == Animation.TYPE_ROTATION_3D:
			var mean_rotation := _mean_rotation(anim, t)
			for k in anim.track_get_key_count(t):
				var q: Quaternion = anim.track_get_key_value(t, k)
				anim.track_set_key_value(t, k, mean_rotation.slerp(q.normalized(), IDLE_MOTION).normalized())
		elif type == Animation.TYPE_POSITION_3D:
			var mean_position := _mean_position(anim, t)
			for k in anim.track_get_key_count(t):
				var p: Vector3 = anim.track_get_key_value(t, k)
				anim.track_set_key_value(t, k, mean_position.lerp(p, IDLE_MOTION))

	# Measured again after the calming, because the mean of a 90 degree sweep is not quite the yaw
	# of the mean pose and it is the calmed clip that has to come out at zero.
	var calmed := _hips_yaw(anim, hips_rotation, hips_rest)
	var calmed_mean: float = calmed["mean"]
	var turn := Quaternion(Vector3.UP, -deg_to_rad(calmed_mean))

	for k in anim.track_get_key_count(hips_rotation):
		var q: Quaternion = anim.track_get_key_value(hips_rotation, k)
		anim.track_set_key_value(hips_rotation, k, (turn * q.normalized()).normalized())
	if hips_position >= 0:
		for k in anim.track_get_key_count(hips_position):
			var p: Vector3 = anim.track_get_key_value(hips_position, k)
			anim.track_set_key_value(hips_position, k, turn * p)

	var after := _hips_yaw(anim, hips_rotation, hips_rest)
	print("    idle    hips mean yaw %+.1f deg -> %+.1f deg; yaw range %.1f deg (%+.1f..%+.1f) -> %.1f deg (%+.1f..%+.1f); every key at %d%% of its motion about the mean pose"
		% [before["mean"], after["mean"],
		   before["range"], before["low"], before["high"],
		   after["range"], after["low"], after["high"],
		   int(round(IDLE_MOTION * 100.0))])


# Which way the hips face over a clip, in degrees about the vertical axis: 0 is the rig's forward
# (+Z, as every imported body faces), positive turns toward +X. "mean" is the circular mean, "low"
# and "high" the extremes, "range" their spread.
#
# The hips' pose rotation times the inverse of its rest is the turn the clip has applied in the
# skeleton's space; carrying +Z through it gives the facing, whatever the bone's own axes are.
func _hips_yaw(anim: Animation, track: int, hips_rest: Quaternion) -> Dictionary:
	var samples := _sample_count(anim)
	var rest_inverse := hips_rest.normalized().inverse()
	var yaws: Array[float] = []
	var sum_sin := 0.0
	var sum_cos := 0.0
	for i in samples:
		var q: Quaternion = anim.rotation_track_interpolate(track, anim.length * float(i) / float(samples))
		var forward: Vector3 = (q.normalized() * rest_inverse) * Vector3(0.0, 0.0, 1.0)
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

	return {
		"mean": rad_to_deg(mean),
		"low": rad_to_deg(mean + low),
		"high": rad_to_deg(mean + high),
		"range": rad_to_deg(high - low),
	}


func _sample_count(anim: Animation) -> int:
	return maxi(int(ceil(anim.length * SAMPLE_RATE)), 2)


# The average orientation a rotation track holds over the clip, sampled evenly in time. Quaternions
# q and -q are the same rotation, so each sample is flipped onto the first one's side before it is
# summed; the normalised sum is the mean for rotations this close together.
func _mean_rotation(anim: Animation, track: int) -> Quaternion:
	var samples := _sample_count(anim)
	var reference: Quaternion = anim.rotation_track_interpolate(track, 0.0)
	reference = reference.normalized()
	var x := 0.0
	var y := 0.0
	var z := 0.0
	var w := 0.0
	for i in samples:
		var q: Quaternion = anim.rotation_track_interpolate(track, anim.length * float(i) / float(samples))
		if q.dot(reference) < 0.0:
			q = -q
		x += q.x
		y += q.y
		z += q.z
		w += q.w

	var total := Quaternion(x, y, z, w)
	if total.length_squared() < 0.000001:
		return reference
	return total.normalized()


func _mean_position(anim: Animation, track: int) -> Vector3:
	var samples := _sample_count(anim)
	var total := Vector3.ZERO
	for i in samples:
		total += anim.position_track_interpolate(track, anim.length * float(i) / float(samples))
	return total / float(samples)


# A copy of a clip reflected left to right, which is how strafe_right is made from strafe_left.
#
# Reflecting in the plane X = 0 (the body's own left/right plane): a track for a Left bone drives the
# Right one and the reverse; a position (x, y, z) becomes (-x, y, z); a rotation (x, y, z, w) becomes
# (x, -y, -z, w). That last one is the reflection of a rotation written out — its axis is a
# pseudovector, so it keeps its X component and flips the other two.
#
# It is exact because the retarget overwrites every mapped bone's rest axes with the humanoid
# profile's (the .import's rest_fixer/overwrite_axis), and the profile's left and right bones are
# mirror images of each other. On a rig whose rests were not overwritten this would twist the limbs.
func _mirrored(source: Animation) -> Animation:
	var anim: Animation = source.duplicate(true)
	anim.resource_path = ""
	for t in anim.get_track_count():
		var path := str(anim.track_get_path(t))
		if ":" in path:
			var node := path.get_slice(":", 0)
			var bone := path.get_slice(":", 1)
			anim.track_set_path(t, NodePath("%s:%s" % [node, _other_side(bone)]))

		var type := anim.track_get_type(t)
		if type == Animation.TYPE_POSITION_3D:
			for k in anim.track_get_key_count(t):
				var p: Vector3 = anim.track_get_key_value(t, k)
				anim.track_set_key_value(t, k, Vector3(-p.x, p.y, p.z))
		elif type == Animation.TYPE_ROTATION_3D:
			for k in anim.track_get_key_count(t):
				var q: Quaternion = anim.track_get_key_value(t, k)
				anim.track_set_key_value(t, k, Quaternion(q.x, -q.y, -q.z, q.w))
	return anim


func _other_side(bone: String) -> String:
	if bone.begins_with("Left"):
		return "Right" + bone.substr(4)
	if bone.begins_with("Right"):
		return "Left" + bone.substr(5)
	return bone
