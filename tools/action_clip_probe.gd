# Clip-driven action probe: on a REAL rigged body, is the animation actually the clock?
#
# melee_probe.gd proves the fraction arithmetic on hand-built actors that have no AnimationPlayer,
# so it exercises the fallback timer — the path an unanimated body takes. This probe covers the other
# half, and it is the half the whole 2026-09-04 overhaul is about:
#
#   1. With an authored Duration, the clip is WARPED to span it. The action takes the authored time
#      whatever the clip's natural length is, so a dagger's flick and the Iron King's heave stop
#      being the same clip at the same speed.
#   2. With Duration = 0, the CLIP decides. The action lasts exactly as long as the animation, which
#      is animation authority in the literal sense.
#   3. In both cases the hit lands inside the authored active fraction OF THE OBSERVED duration —
#      not of a separate stopwatch, because there no longer is one.
#
# ⚠️ This needs a rigged body with a resolvable attack clip. chr_player_base retargets to
# GeneralSkeleton and therefore receives the shared library, whose Sword_Attack is what "attack"
# resolves to. If the resolve comes back empty the probe FAILS rather than passing quietly — an
# unresolved clip is exactly the silent defect docs/3D_ASSETS.md warns about.
#
# Run:  Godot_..._console.exe --headless --path . --script res://tools/action_clip_probe.gd
extends SceneTree

const BODY := "res://assets/models/characters/chr_player_base.glb"
const WEAPON := "res://data/weapons/IronSword.tres"
const HEALTH := 0
const STEP := 1.0 / 60.0

var _failures: Array[String] = []


func _initialize() -> void:
	root.add_child(load("res://src/Bootstrap/ContentDatabaseLoader.cs").new())
	await process_frame

	var parts := await _build()
	var action = parts[0]
	var animation = parts[1]
	var stats = parts[2]

	if animation == null:
		_failures.append("the body carries no CharacterAnimationComponent; the probe would prove nothing")
	else:
		# Ask the animation directly what it would do, before any action runs. A duration of -1 here
		# means no clip resolved, and every assertion below would silently become a fallback test.
		var natural: float = animation.StartAction("attack", 0.0)
		animation.StopAction()
		print("resolved attack clip: natural length %.3fs" % natural)
		if natural <= 0.0:
			_failures.append("no 'attack' clip resolved on %s — this probe cannot test the clip-driven path" % BODY)
		else:
			await _case(action, stats, "warped to 0.90s", 0.90, natural)
			await _case(action, stats, "warped to 0.35s", 0.35, natural)
			await _case(action, stats, "clip decides", 0.0, natural)

	if _failures.is_empty():
		print("PASS: the clip is the clock, and the hit lands inside the window it shows")
		quit(0)
	else:
		for f in _failures:
			print("FAIL: %s" % f)
		quit(1)


func _build() -> Array:
	var body := CharacterBody3D.new()
	body.set_script(load("res://src/Entities/CharacterEntity.cs"))
	body.name = "Attacker"
	var shape := CollisionShape3D.new()
	var capsule := CapsuleShape3D.new()
	capsule.radius = 0.4
	capsule.height = 1.8
	shape.shape = capsule
	shape.position = Vector3(0, 0.9, 0)
	body.add_child(shape)

	# CharacterAnimationComponent looks for its AnimationPlayer and Skeleton3D under a child named
	# BodyMeshPath, which is how every factory in the game wires it.
	var visual := Node3D.new()
	visual.name = "BodyMesh"
	var scene: PackedScene = load(BODY)
	if scene != null:
		visual.add_child(scene.instantiate())
	body.add_child(visual)

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

	var hitbox := Area3D.new()
	hitbox.set_script(load("res://src/Combat/Hitbox.cs"))
	hitbox.name = "MeleeHitbox"
	hitbox.position = Vector3(0, 1.0, -1.0)
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

	# The target is a separate actor so the hit window can be observed landing on something.
	var target := CharacterBody3D.new()
	target.set_script(load("res://src/Entities/CharacterEntity.cs"))
	target.name = "Target"
	target.position = Vector3(0, 0, -1.0)
	var tshape := CollisionShape3D.new()
	var tcap := CapsuleShape3D.new()
	tcap.radius = 0.4
	tcap.height = 1.8
	tshape.shape = tcap
	tshape.position = Vector3(0, 0.9, 0)
	target.add_child(tshape)
	var tstats = load("res://src/Stats/StatsComponent.cs").new()
	tstats.name = "Stats"
	target.add_child(tstats)
	var tcombat = load("res://src/Combat/CombatComponent.cs").new()
	tcombat.name = "Combat"
	tcombat.Team = 2
	target.add_child(tcombat)
	var hurtbox := Area3D.new()
	hurtbox.set_script(load("res://src/Combat/Hurtbox.cs"))
	hurtbox.name = "Hurtbox"
	var zone := CollisionShape3D.new()
	var zcap := CapsuleShape3D.new()
	zcap.radius = 0.4
	zcap.height = 1.8
	zone.shape = zcap
	zone.position = Vector3(0, 0.9, 0)
	hurtbox.add_child(zone)
	target.add_child(hurtbox)

	root.add_child(body)
	root.add_child(target)
	await process_frame
	await physics_frame
	return [action, animation, tstats]


# Builds a one-link chain at the given duration, swings, and measures how long the action actually
# took and when the blow landed.
func _case(action, stats, label: String, duration: float, natural: float) -> void:
	var definition = load("res://src/Combat/Actions/ActionDefinitionResource.cs").new()
	definition.Id = "probe.%s" % label
	definition.AnimationSlot = "attack"
	definition.Duration = duration
	definition.FallbackDuration = 0.55
	definition.ActiveFrom = 0.30
	definition.ActiveTo = 0.55
	definition.CancelFrom = 0.55
	definition.ComboFrom = 0.55
	definition.ComboTo = 1.0
	definition.StaminaCost = 0.0
	definition.MoveScale = 0.35

	var expected: float = duration if duration > 0.0 else natural

	# Stamina and health are topped up so neither runs the case out from under the measurement.
	stats.ModifyCurrent(HEALTH, 500.0)

	if not action.TryStart(definition):
		_failures.append("%s: the action refused to start" % label)
		return

	var elapsed := 0.0
	var hit_at := -1.0
	var ended_at := -1.0
	var last: float = stats.GetCurrent(HEALTH)
	var limit: float = expected + 1.0
	while elapsed < limit:
		await physics_frame
		elapsed += STEP
		var now: float = stats.GetCurrent(HEALTH)
		if now < last:
			if hit_at < 0.0:
				hit_at = elapsed
			last = now
		if ended_at < 0.0 and action.Current == null:
			ended_at = elapsed
			break

	var open_at: float = expected * definition.ActiveFrom
	var close_at: float = expected * definition.ActiveTo
	print("%-16s: expected %.3fs, ran %.3fs, hit at %.3fs (window %.3f..%.3f)"
		% [label, expected, ended_at, hit_at, open_at, close_at])

	if ended_at < 0.0:
		_failures.append("%s: the action never ended within %.2fs" % [label, limit])
		return

	# Two frames of slack: an action ends on the physics frame after its progress reaches 1.
	if abs(ended_at - expected) > (3.0 * STEP):
		_failures.append("%s: the action ran %.3fs but the clip was supposed to make it %.3fs — the clip is not the clock"
			% [label, ended_at, expected])

	if hit_at < 0.0:
		_failures.append("%s: the blow never landed" % label)
	elif hit_at < (open_at - STEP) or hit_at > (close_at + (2.0 * STEP)):
		_failures.append("%s: the blow landed at %.3fs, outside the window %.3f..%.3f the animation showed"
			% [label, hit_at, open_at, close_at])
