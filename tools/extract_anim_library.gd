# Regenerates assets/models/animations/anim_library.res from anim_library.glb (Phase 38, animation
# retarget). The .res is what the game loads: it holds the 46 retargeted clips and nothing else, so
# the library's 6.6 MB Mannequin mesh never reaches a running build.
#
# Re-run this whenever anim_library.glb or its .import retarget settings change — the committed .res
# is otherwise unreproducible.
#
# Run:  Godot_..._console.exe --headless --path . --script res://tools/extract_anim_library.gd
extends SceneTree

const SOURCE := "res://assets/models/animations/anim_library.glb"
const DEST := "res://assets/models/animations/anim_library.res"

func _initialize() -> void:
	var packed: PackedScene = load(SOURCE)
	if packed == null:
		print("FAIL: %s did not load" % SOURCE)
		quit(1)
		return

	var scene: Node = packed.instantiate()
	var players: Array[Node] = scene.find_children("*", "AnimationPlayer", true, false)
	if players.is_empty():
		print("FAIL: no AnimationPlayer in %s" % SOURCE)
		scene.free()
		quit(1)
		return

	var library: AnimationLibrary = (players[0] as AnimationPlayer).get_animation_library("")
	var clips: PackedStringArray = library.get_animation_list()
	# Take it off the imported scene so ResourceSaver writes the clips out rather than a reference
	# back into the .glb's imported .scn, which is a build artifact under .godot/.
	var out := AnimationLibrary.new()
	var stripped := 0
	for name in clips:
		var anim: Animation = library.get_animation(name).duplicate(true)
		anim.resource_path = ""
		stripped += _strip_translation(anim)
		stripped += _strip_lower_body(anim)
		out.add_animation(name, anim)
	print("stripped %d position/scale tracks" % stripped)

	var err := ResourceSaver.save(out, DEST)
	if err != OK:
		print("SAVE FAILED: %s" % str(err))
	print("%d clips -> %s (%s)" % [clips.size(), DEST, "ok" if err == OK else str(err)])
	scene.free()
	quit(0 if err == OK else 1)


# Drops every position and scale track, leaving pure rotation — a POSE, not a journey.
#
# ⚠️ This is not tidying, it is the fix the 38A render gate forced. The library's rig is
# root -> Hips, so its hip translation track carries the whole standing height (y ~ 0.79). The
# Quaternius body is Root -> Body -> Hips, and its Body bone already carries that lift, so the
# track landed on top of it and stood the merchant at 1.63 m — floating, with the legs strung out
# below. Nothing logged; it was only visible in a render.
#
# Stripping rather than rescaling is the right call for THIS game and not a general one: nothing
# here consumes root motion (locomotion is CharacterBody3D velocity and ScheduleComponent writing
# GlobalPosition), so a hip translation from a generic library could only ever fight the mover that
# actually owns the character's position. If root motion is ever wanted, the pack ships explicit
# _RM variants (Sword_Attack_RM, Roll_RM) and they would need their own path anyway.
func _strip_translation(anim: Animation) -> int:
	var removed := 0
	for t in range(anim.get_track_count() - 1, -1, -1):
		var kind := anim.track_get_type(t)
		if kind == Animation.TYPE_POSITION_3D or kind == Animation.TYPE_SCALE_3D:
			anim.remove_track(t)
			removed += 1
	return removed


# Bones below the hips, in SkeletonProfileHumanoid's names.
const LOWER_BODY := [
	"LeftUpperLeg", "LeftLowerLeg", "LeftFoot", "LeftToes",
	"RightUpperLeg", "RightLowerLeg", "RightFoot", "RightToes",
]


# Drops the leg tracks, leaving each clip an UPPER-BODY pose from the hips up.
#
# ⚠️ The reason is a property of the target rig, measured, not a preference. On every adopted
# Quaternius body the feet are not FK bones: Foot.L/Foot.R and the pole targets PT.L/PT.R are
# parented to Root, not to the shin — an IK goal rig. (PT.L's rest sits 63 cm up and 30 cm forward
# of the foot, which is what gave it away; it is a pole target, not a toe.) The library's rig is a
# plain FK chain, so its foot rotation lands on a root-parented goal that the shin does not carry,
# and the boot stays pinned at its rest spot while the leg swings away from it — a black spike out
# of the ankle. Only the render caught it; the clip plays and nothing is logged.
#
# ponytail: upper body only, and that is the ceiling. It is exactly right for the three slots this
# library is here to fill — block, cast and channel are standing poses, and the legs keeping the
# body's own stance under them is what you want anyway. It is NOT right for Jog/Crouch/Sitting/Swim,
# which is why none of those are wired to a slot; the run slot resolves to the body's own Run clip.
# To use the library's locomotion, the upgrade is an AnimationTree blending it over the body's own
# legs through a bone filter, or a body whose feet are FK. Neither is worth it for three poses.
func _strip_lower_body(anim: Animation) -> int:
	var removed := 0
	for t in range(anim.get_track_count() - 1, -1, -1):
		var bone := String(anim.track_get_path(t).get_subname(0))
		if LOWER_BODY.has(bone):
			anim.remove_track(t)
			removed += 1
	return removed
