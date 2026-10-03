# Native regression fixtures for the runtime world audit, including clear/blocked player loading.
# No region load or world generation.
# Run --headless --path . --script res://tools/world_audit_probe.gd.
extends SceneTree

var _failures: Array[String] = []

func _initialize() -> void:
	var world := Node3D.new()
	root.add_child(world)
	current_scene = world
	var driver = load("res://src/Debugging/WorldAuditProbeDriver.cs").new()
	await process_frame
	_collect(driver.Begin(world))
	await process_frame
	_collect(driver.CheckRetirement())
	await physics_frame
	_collect(driver.CheckNoGround())
	driver.AddFloor()
	await physics_frame
	await physics_frame
	_collect(driver.CheckClearGround())
	driver.AddBlocker()
	await physics_frame
	await physics_frame
	_collect(driver.CheckBlockedGround())
	driver.Finish() # close the original session/world scopes before opening player sessions
	await process_frame
	driver.BeginPlayerLoading(world)
	await physics_frame
	await physics_frame
	_collect(driver.CheckClearPlayerLoading())
	driver.AddPlayerLoadingBlocker()
	await physics_frame
	await physics_frame
	_collect(driver.CheckBlockedPlayerLoading())
	var checks: int = driver.CheckCount
	driver.FinishPlayerLoading()
	driver = null
	world.queue_free()
	await process_frame
	if _failures.is_empty():
		print("PASS: world runtime audit (%d checks)" % checks)
		quit(0)
	else:
		for issue in _failures: print("FAIL: %s" % issue)
		quit(1)

func _collect(issues) -> void:
	for issue in issues: _failures.append(str(issue))
