# Save integrity regression. Expected refusal cases deliberately emit ERROR logs.
# Requires an absolute isolated EMBERVALE_USER_DIR and only writes unique probe slots.
# Run --headless --path . --script res://tools/save_audit_probe.gd.
extends SceneTree

func _initialize() -> void:
	await process_frame # application autoloads must be ready before the driver runs
	var world := Node3D.new()
	root.add_child(world)
	current_scene = world
	var driver = load("res://src/Debugging/SaveAuditProbe.cs").new()
	var failures = driver.Run(world)
	driver = null
	world.queue_free()
	await process_frame
	await process_frame
	if failures.is_empty():
		print("PASS: save capture integrity, character metadata, migrations, live restore failures, spawn reconciliation and legacy discovery")
		quit(0)
	else:
		for issue in failures: print("FAIL: %s" % issue)
		quit(1)
