# Exact held consumables, reentrant healing, dead actors, direct-child lookup order,
# and a same-process allocation/timing comparison against the old snapshot lookup.
extends SceneTree

func _initialize() -> void:
	call_deferred("_run")

func _run() -> void:
	var driver = load("res://src/Debugging/RuntimeAuditProbeDriver.cs").new()
	root.add_child(driver)
	var passed: bool = driver.RunChecks()
	driver.free()
	await process_frame
	if passed:
		print("runtime_audit_probe: PASS")
	else:
		push_error("runtime_audit_probe: FAIL")
	quit(0 if passed else 1)
