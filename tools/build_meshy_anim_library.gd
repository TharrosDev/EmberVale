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
# Re-run whenever the sources or their .import retarget settings change — the committed .res is
# otherwise unreproducible.
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

		# Locomotion and stance clips loop; one-shot actions do not. Getting this wrong is visible
		# immediately (a walk that plays once and freezes) rather than subtly, so it is a table.
		if slot in ["idle", "walk", "run", "sprint", "walk_back", "combat_walk_fwd",
				"combat_walk_back", "block", "fall"]:
			anim.loop_mode = Animation.LOOP_LINEAR

		out.add_animation(slot, anim)
		added += 1
		total_tracks += anim.get_track_count()
		print("  %-20s %5.2fs  %2d tracks  %2d mapped bones  %s"
			% [slot, anim.length, anim.get_track_count(), mapped,
			   "loop" if anim.loop_mode == Animation.LOOP_LINEAR else "once"])
		scene.free()

	if added == 0:
		_failures.append("no clips were added at all")

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


# How many of this clip's tracks address a SkeletonProfileHumanoid bone. Counting rather than
# asserting a total: not every clip animates every bone, but a clip that reaches none of them was
# never retargeted.
func _mapped_bones(anim: Animation) -> int:
	var seen := {}
	for t in anim.get_track_count():
		var path := str(anim.track_get_path(t))
		if ":" not in path:
			continue
		var bone := path.get_slice(":", 1)
		if bone in PROFILE_BONES:
			seen[bone] = true
	return seen.size()
