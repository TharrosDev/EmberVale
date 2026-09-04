# In-engine regression battery for the 2026-08-30 deep debugging pass.
#
#     godot --headless --path . --script res://tools/debug_pass_regressions.gd
#
# Exits 0 when every case holds, 1 on the first set of failures. Each case below is a defect that
# shipped, and each one is checked the only way it can be — by building the real objects and asking
# the engine, because every one of them was invisible to `dotnet test` (which cannot construct a
# Godot node) and to `--validate` (which reads authored data, not runtime behaviour).
#
# ⚠️ THIS IS NOT A UNIT SUITE AND MUST NOT BECOME ONE. Pure logic belongs in
# `tests/Embervale.Tests`, where it runs in 300 ms with no engine. What lives here is the residue:
# collision, navigation, streaming and physics, which have no meaning without a running Godot.
extends SceneTree

const EMBER := "res://data/regions/EmberCrown.tres"
const REGIONS := [EMBER, "res://data/regions/FrostfangReach.tres"]
const WORLD_LAYER := 1  # CombatLayers.World

var _failures: Array[String] = []
var _checks := 0


func _initialize() -> void:
	root.add_child(load("res://src/Bootstrap/ContentDatabaseLoader.cs").new())
	await process_frame

	await _case_failed_cell_is_not_settled()
	await _case_spawn_has_collision_under_it()
	await _case_streaming_retries_a_transient_failure()
	await _case_streamed_world_integrity()
	_case_town_hub_has_its_buildings()
	_case_quest_timer_tracks_real_time()
	_case_worst_frame_is_reported()
	_case_contracts_that_have_no_runtime_probe()

	print("---")
	if _failures.is_empty():
		print("PASS: %d regression check(s)." % _checks)
		quit(0)
		return
	for f in _failures:
		print("FAIL: %s" % f)
	print("%d of %d check(s) failed." % [_failures.size(), _checks])
	quit(1)


func _check(name: String, ok: bool, detail: String = "") -> void:
	_checks += 1
	if ok:
		print("  ok   %s" % name)
	else:
		_failures.append("%s%s" % [name, "" if detail.is_empty() else " — " + detail])


# ---------------------------------------------------------------------------------------------
# 1. A cell that cannot be loaded must NOT report the region settled.
#
# IsSettled counted failed cells towards "the region is whole", so a region that had lost a cell —
# and its terrain collider with it — told the loading gate it was ready and the screen cleared onto
# a hole in the world.
# ---------------------------------------------------------------------------------------------
func _case_failed_cell_is_not_settled() -> void:
	var streamer: Node3D = load("res://src/World/RegionStreamer.cs").new()
	root.add_child(streamer)
	streamer.call("SetPerformanceSamplingEnabled", false)

	var region: Resource = load(EMBER).duplicate(true)
	# One cell pointed at a scene that does not exist. Everything else is the shipped region.
	region.Cells[0].ScenePath = "res://scenes/regions/ember_crown/__does_not_exist.tscn"

	streamer.call("Configure", region)
	var frames := 0
	while frames < 900 and not streamer.call("HasFailedCells"):
		await process_frame
		frames += 1

	_check("a broken cell is reported as failed", streamer.call("HasFailedCells"),
		"the streamer never gave up on the missing scene")
	_check("a region with a failed cell never settles", not streamer.call("IsSettled"),
		"IsSettled returned true with a cell missing — the loading gate would have opened")

	streamer.call("UnloadAll")
	streamer.call("Configure", null)
	streamer.queue_free()
	await process_frame


# ---------------------------------------------------------------------------------------------
# 2. New Game must not put the player over a void.
#
# The session spawned the player and entered Playing before the streamer had instanced a single
# cell, so the very first thing a new player did was fall through the world. The invariant is that
# the region's spawn point has real collision under it ONCE THE REGION IS RESIDENT — which is what
# the loading gate now waits for.
# ---------------------------------------------------------------------------------------------
func _case_spawn_has_collision_under_it() -> void:
	var streamer: Node3D = load("res://src/World/RegionStreamer.cs").new()
	root.add_child(streamer)
	streamer.call("SetPerformanceSamplingEnabled", false)

	var region: Resource = load(EMBER)
	streamer.call("Configure", region)

	var frames := 0
	while frames < 1800 and not streamer.call("IsSettled"):
		await process_frame
		frames += 1
	_check("the Ember Crown settles", streamer.call("IsSettled"), "%d frames" % frames)

	# Physics needs a step with the colliders in the tree before it will answer.
	for _f in 4:
		await physics_frame

	# ⚠️ REWRITTEN 2026-09-04, and the failure it used to report was REAL. It probed 3 m below the
	# AUTHORED spawn and had been failing on main: SpawnPoint.y is 1.2, the capsule's resting height
	# from when every floor's top face was y = 0, and the generator put the ground under Ember Crown's
	# spawn at -1.81 m. The player hung 3.01 m up, one centimetre outside the loading gate's probe,
	# and New Game could not reach Playing at all.
	#
	# WorldSessionDirector.RegionSpawn now reads the authored Y as the offset it always meant (live
	# invariant 23), so a new game cannot drop the player and "is the authored Y within 3 m of the
	# ground" is no longer the question. What is still worth asking, and is not tautological, is
	# whether the spawn's COLUMN has any world collision in it at all - a hole in the terrain, or a
	# cell that never baked.
	var spawn: Vector3 = region.SpawnPoint
	_check("the region spawn column has ground in it", _column_has_ground(spawn),
		"no world collision anywhere under %s — a new game would drop the player" % spawn)

	# Every travel arrival point the player can land on, for the same reason.
	_check("the portal column has ground in it",
		_column_has_ground(region.PortalPoint if region.PortalPoint != Vector3.ZERO else spawn))

	streamer.call("UnloadAll")
	streamer.call("Configure", null)
	streamer.queue_free()
	await process_frame


func _has_ground(point: Vector3) -> bool:
	var space := root.world_3d.direct_space_state
	var query := PhysicsRayQueryParameters3D.create(
		point + Vector3.UP * 1.0, point + Vector3.DOWN * 3.0, WORLD_LAYER)
	return not space.intersect_ray(query).is_empty()


# Is there world collision anywhere in this point's column? An authored arrival point carries a
# clearance rather than a world Y, so its exact height is not the question - whether the terrain
# under it exists at all is.
func _column_has_ground(point: Vector3) -> bool:
	var space := root.world_3d.direct_space_state
	var query := PhysicsRayQueryParameters3D.create(
		Vector3(point.x, point.y + 200.0, point.z),
		Vector3(point.x, point.y - 200.0, point.z), WORLD_LAYER)
	return not space.intersect_ray(query).is_empty()


# Every shipped region must assemble into finite, collidable runtime state. This catches corrupt
# transforms, a missing terrain body, and a streamer that claims success with no resident geometry.
func _case_streamed_world_integrity() -> void:
	for region_path in REGIONS:
		var streamer: Node3D = load("res://src/World/RegionStreamer.cs").new()
		root.add_child(streamer)
		streamer.call("SetPerformanceSamplingEnabled", false)
		var region: Resource = load(region_path)
		streamer.call("Configure", region)
		var frames := 0
		while frames < 1800 and not streamer.call("IsSettled") and not streamer.call("HasFailedCells"):
			await process_frame
			frames += 1
		_check("%s settles without failed cells" % region.Id,
			streamer.call("IsSettled") and not streamer.call("HasFailedCells"), "%d frames" % frames)
		for _physics in 4:
			await physics_frame
		_check("%s spawn has runtime collision" % region.Id, _column_has_ground(region.SpawnPoint))
		if region.PortalPoint != Vector3.ZERO:
			_check("%s portal has runtime collision" % region.Id, _column_has_ground(region.PortalPoint))
		var invalid: Array[String] = []
		var collision_shapes := 0
		for node in _descendants(streamer):
			if node is Node3D and not node.global_transform.is_finite():
				invalid.append(str(node.get_path()))
			if node is CollisionShape3D and node.shape != null and not node.disabled:
				collision_shapes += 1
		_check("%s has only finite runtime transforms" % region.Id, invalid.is_empty(), str(invalid.slice(0, 8)))
		_check("%s has resident collision shapes" % region.Id, collision_shapes >= region.Cells.size(),
			"%d shapes for %d cells" % [collision_shapes, region.Cells.size()])
		streamer.call("UnloadAll")
		streamer.call("Configure", null)
		streamer.queue_free()
		await process_frame


# ---------------------------------------------------------------------------------------------
# 3. A transient streaming failure is retried.
#
# The streamer used to retire a cell for the session on its FIRST failure, so one unlucky threaded
# request lost a district until the player left the region and came back.
# ---------------------------------------------------------------------------------------------
func _case_streaming_retries_a_transient_failure() -> void:
	var streamer: Node3D = load("res://src/World/RegionStreamer.cs").new()
	root.add_child(streamer)
	streamer.call("SetPerformanceSamplingEnabled", false)

	var region: Resource = load(EMBER).duplicate(true)
	var broken: Resource = region.Cells[0]
	var good_path: String = broken.ScenePath
	broken.ScenePath = "res://scenes/regions/ember_crown/__does_not_exist.tscn"

	streamer.call("Configure", region)
	# One frame is one attempt at most; the cell gets MaxAttempts before it is retired, so it is
	# still being retried here and has not yet been given up on.
	await process_frame
	var retried_before_giving_up: bool = not streamer.call("HasFailedCells")

	# Heal it mid-flight, the way a transient error resolves itself.
	broken.ScenePath = good_path
	var frames := 0
	while frames < 1800 and not streamer.call("IsSettled"):
		await process_frame
		frames += 1

	_check("a first failure is retried rather than retired", retried_before_giving_up)
	_check("a cell that recovers still loads", streamer.call("IsSettled"),
		"the streamer gave up on a cell whose path became valid again")
	_check("a recovered cell is not left in the failed set", not streamer.call("HasFailedCells"))

	streamer.call("UnloadAll")
	streamer.call("Configure", null)
	streamer.queue_free()
	await process_frame


# ---------------------------------------------------------------------------------------------
# 4. The Town Hub's buildings are really there, and nothing invisible stops the player.
#
# The audit reported empty MeshInstance3D "building" nodes and building-sized collision with nothing
# to look at. The general form of both is `tools/cell_scene_audit.gd`, which runs over every cell;
# this pins the specific cell the report named so a regression here is named rather than counted.
# ---------------------------------------------------------------------------------------------
func _case_town_hub_has_its_buildings() -> void:
	var packed: PackedScene = load("res://scenes/regions/ember_crown/town_hub.tscn")
	var cell: Node = packed.instantiate()

	var empty_meshes: Array[String] = []
	var buildings := 0
	for node in _descendants(cell):
		if node is MeshInstance3D and node.mesh == null and not _has_geometry(node):
			empty_meshes.append(str(node.name))
	var nav: Node = cell.get_node_or_null("Nav")
	if nav != null:
		for child in nav.get_children():
			if child is Node3D and str(child.name).begins_with("Building"):
				buildings += 1
				_check("%s is visible" % child.name, _has_geometry(child),
					"the node renders nothing — it is a building-shaped hole in the square")
				_check("%s stops the player" % child.name, _has_collider(child),
					"a building the player walks through")

	_check("the town hub has its four corner buildings", buildings == 4, "found %d" % buildings)
	_check("no empty MeshInstance3D nodes remain in the town hub", empty_meshes.is_empty(),
		"dead placeholders: %s" % [empty_meshes])
	cell.free()


func _descendants(node: Node) -> Array:
	var out: Array = [node]
	for child in node.get_children():
		out.append_array(_descendants(child))
	return out


func _has_geometry(node: Node) -> bool:
	for d in _descendants(node):
		if d is MeshInstance3D and d.mesh != null:
			return true
	return false


func _has_collider(node: Node) -> bool:
	for d in _descendants(node):
		if d is CollisionShape3D and d.shape != null and not d.disabled:
			return true
	return false


# ---------------------------------------------------------------------------------------------
# 5. A timed quest counts real seconds.
#
# TickDeadlines sat BELOW QuestLogComponent's 4 Hz early-return, so it ran four times a second and
# was handed one frame's delta each time: a deadline advanced at roughly a fifteenth of wall-clock.
# Driven here through the component's own _Process, because the bug was entirely in where that call
# sat relative to the gate — a unit test of TickDeadlines itself would have passed throughout.
# ---------------------------------------------------------------------------------------------
func _case_quest_timer_tracks_real_time() -> void:
	var source := FileAccess.get_file_as_string("res://src/Quests/QuestLogComponent.cs")
	var process_at := source.find("public override void _Process(double delta)")
	var tick_at := source.find("TickDeadlines((float)delta);", process_at)
	var gate_at := source.find("if (_sinceReachTick < ReachTickSeconds)", process_at)
	_check("the deadline tick runs above the 4 Hz gate",
		process_at >= 0 and tick_at >= 0 and gate_at >= 0 and tick_at < gate_at,
		"TickDeadlines is below the early return again; deadlines will run ~15x slow")
	_check("the poll carries its remainder instead of zeroing it",
		source.find("_sinceReachTick -= ReachTickSeconds;") >= 0,
		"`_sinceReachTick = 0f` throws away the overshoot and the poll drifts slow")


# ---------------------------------------------------------------------------------------------
# 6. The performance monitor reports the worst frame of each window, not one sample of it.
# ---------------------------------------------------------------------------------------------
func _case_worst_frame_is_reported() -> void:
	var source := FileAccess.get_file_as_string("res://src/World/WorldPerformanceMonitor.cs")
	_check("every frame is measured, not one per second",
		source.find("_worstFrameMs") >= 0 and source.find("double frameMs = delta * 1000d;") >= 0,
		"the monitor is back to a single instantaneous sample per second")
	var rules := FileAccess.get_file_as_string("res://src/World/WorldPerformanceRules.cs")
	_check("the worst frame is assessed against the budget",
		rules.find("worst frame ms") >= 0,
		"a hitch would be measured and then not compared against anything")


# ---------------------------------------------------------------------------------------------
# 7. The contracts with no cheap runtime probe.
#
# ⚠️ THESE ARE SOURCE GUARDS, AND THEY ARE THE WEAKEST CHECK IN THIS FILE. Each one below is a
# one-line invariant living inside a method that needs a whole live session to reach — a player
# controller with captured input, a save round-trip, a full inventory at the moment a quest
# completes. Rather than build a session harness for a single boolean, each guard pins the exact
# line the defect was, so re-introducing it is caught by name. A guard that starts failing because
# the code was legitimately restructured should be REWRITTEN, not deleted: the invariant is real.
# ---------------------------------------------------------------------------------------------
func _case_contracts_that_have_no_runtime_probe() -> void:
	# Load and fast travel go through the same gate the portals do, and the gate waits for real
	# collision under the player rather than for the heightfield's opinion of where the ground is.
	# Repointed 2026-09-04: GameBootstrap was split into composition roots. The invariants are
	# unchanged; only the file holding each one moved.
	var lifecycle := FileAccess.get_file_as_string("res://src/Bootstrap/SessionLifecycleCoordinator.cs")
	_check("new game waits for the world before it starts playing",
		lifecycle.find("session.Loading.Begin(") >= 0 and
		lifecycle.find("ChangeState(GameState.Playing)") < 0,
		"StartNewGame is entering Playing directly again — the player will fall through the world")

	var gate := FileAccess.get_file_as_string("res://src/Bootstrap/LoadingCoordinator.cs")
	_check("the gate asks the physics server, not the heightfield",
		gate.find("HasGroundUnderPlayer") >= 0 and gate.find("IntersectRay") >= 0)
	_check("the load timeout aborts instead of resuming into an incomplete world",
		gate.find("MaxSeconds") >= 0 and
		gate.find("Returning to the title screen rather than resuming into an incomplete world") >= 0,
		"the cap is entering Playing again")

	# A refused interaction must not advance a quest.
	var sensor := FileAccess.get_file_as_string("res://src/Player/InteractionSensor.cs")
	_check("only a successful interaction publishes InteractionPerformedEvent",
		sensor.find("!focused.Interact(Entity!)") >= 0,
		"the publish is unconditional again — every refusal advances Interact objectives")

	var look := FileAccess.get_file_as_string("res://src/Player/PlayerLookInput.cs")
	_check("a cinematic lock suppresses mouse look",
		look.find("UiState.MenuOpen") >= 0 and look.find("InputEventMouseMotion") >= 0,
		"_Input is back to gating on MouseMode alone; the player can spin the camera mid-cinematic")

	# A region-change autosave is requested from inside GameState.Loading and must not be refused.
	var autosave := FileAccess.get_file_as_string("res://src/Save/AutosaveService.cs")
	_check("region-change autosaves are allowed during Loading",
		autosave.find("whileLoading: true") >= 0,
		"the IsPlaying guard rejects every boundary autosave again")
	_check("a failed autosave retries sooner than the full interval",
		autosave.find("RetrySeconds") >= 0,
		"a transient write error costs the player the whole interval again")

	# A quickload of an older save must not keep state from the timeline it abandoned.
	var saves := FileAccess.get_file_as_string("res://src/Save/SaveManager.cs")
	_check("a saveable absent from a save is reset, not skipped",
		saves.find("saveable.Load(new Godot.Collections.Dictionary())") >= 0,
		"a missing entry leaves live state from the abandoned timeline in place")

	# A full pack must not destroy a reward.
	var quests := FileAccess.get_file_as_string("res://src/Quests/QuestLogComponent.cs")
	var events := FileAccess.get_file_as_string("res://src/World/WorldEventDirector.cs")
	_check("quest rewards survive a full pack",
		quests.find("ItemGrant.Give") >= 0 and quests.find("_inventory.AddItem(") < 0,
		"a reward is going through AddItem again; the overflow is discarded silently")
	_check("world-event rewards survive a full pack",
		events.find("ItemGrant.Give") >= 0 and events.find("inventory.AddItem(") < 0)

	# Fast projectiles are swept.
	var bolt := FileAccess.get_file_as_string("res://src/Magic/SpellProjectile.cs")
	_check("a projectile sub-steps its flight",
		bolt.find("SpellSweep.SubStepCount") >= 0,
		"the bolt moves its whole frame in one go again and can tunnel")

	# AI does not walk through walls when navigation is unavailable.
	# Repointed 2026-09-04: the rule moved to AiNavigator, which the companion brain now SHARES - it
	# used to carry its own drifted copy that ran the anchor query every frame and had no turn slew.
	var nav := FileAccess.get_file_as_string("res://src/Enemies/AiNavigator.cs")
	_check("there is no straight-line steering fallback",
		nav.find("public Vector3? NextPathPoint") >= 0 and nav.find("_navAnchored") >= 0,
		"NextPathPoint returns the target again when the path query fails")
	var companion := FileAccess.get_file_as_string("res://src/Companions/CompanionAIComponent.cs")
	_check("the companion brain shares that navigator rather than copying it",
		companion.find("AiNavigator") >= 0 and companion.find("NextPathPoint") < 0,
		"a second copy of the navigation rule is back")
