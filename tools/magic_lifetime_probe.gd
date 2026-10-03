# Native lifetime regression: active spells belong to a session and stop before load restoration or
# caster removal. Run --headless --path . --script res://tools/magic_lifetime_probe.gd.
extends SceneTree

var _failures: Array[String] = []
var _driver
var _databases
var _world: Node3D

func _initialize() -> void:
	_databases = load("res://src/Bootstrap/ContentDatabaseLoader.cs").new()
	root.add_child(_databases)
	_world = Node3D.new()
	root.add_child(_world)
	current_scene = _world
	_driver = load("res://src/Debugging/MagicLifetimeProbeDriver.cs").new()
	await process_frame
	_check(_driver.DefaultAttributesAreIndependent(), "default attributes must be distinct, mutation-isolated resources")
	_driver.Begin(_world)
	await _frames()
	_driver.SpawnAll()
	await _frames() # let the physical wall reach its authored position before the control query
	_check(_driver.ControlEffectsWork(), "control zone/totem must damage and heal before cancellation")
	_driver.Finish()
	await _frames()
	for cancellation in ["load", "detach", "despawn", "queued", "death"]:
		_driver.Begin(_world)
		await _frames()
		_driver.SpawnAll()
		_check(_driver.OwnedBySession(), "%s: deliveries must be children of GameSession" % cancellation)
		match cancellation:
			"load": _driver.PreLoad()
			"detach": _driver.DetachCaster()
			"despawn": _driver.DespawnEvent()
			"queued": _driver.QueueCaster()
			"death": _driver.DeathAndRespawn()
		for issue in _driver.InertIssues():
			_failures.append("%s: %s" % [cancellation, issue])
		await _frames()
		_check(_driver.DeliveriesFreed(), "%s: deliveries survived cleanup frames" % cancellation)
		_driver.Finish()
		await _frames()
	_driver.Begin(_world)
	await _frames()
	_driver.BeginPooling()
	_driver.ExpireShot()
	await _frames()
	_check(_driver.ShotReturned(1), "first expiry did not return the projectile to its pool")
	_driver.ReuseShot()
	_driver.ExpireShot()
	await _frames()
	_check(_driver.ShotReturned(2), "second launch did not reuse and return the same projectile")
	_driver.ReuseShot()
	_driver.ExpireShot() # normal deferred Release is pending when pre-load cancels it
	_driver.PreLoad()
	_check(_driver.QueuedReturnCancelled(), "pre-load failed to cancel a pending pool return immediately")
	await _frames()
	_check(_driver.CancelledShotFreed(), "cancelled pending return reached pool or survived cleanup")
	_driver.Finish()
	await _frames()
	for source in ["projectile", "ground", "zone", "direct", "cone", "dash"]:
		for cancellation in ["normal", "load", "death", "respawn"]:
			# Exercise the production default path across managed collection and session teardown.
			# Defaults remain fresh per component while their C# script stays resident.
			_databases.CollectManagedResources()
			_driver.Begin(_world)
			_check(_driver.FixtureDefaultsRetained(), "components must retain independent default attributes")
			_driver.PrepareBurstCancellation(source, cancellation)
			await _frames()
			for issue in _driver.ResolveBurstCancellation():
				_failures.append("burst %s/%s: %s" % [source, cancellation, issue])
			_driver.Finish()
			await _frames()
	_driver = null
	_world.queue_free()
	await _frames()
	_databases.CollectManagedResources()
	await process_frame
	if _failures.is_empty():
		print("PASS: spell session ownership, pre-load cancellation, caster teardown and projectile pooling")
		quit(0)
	else:
		for issue in _failures: print("FAIL: %s" % issue)
		quit(1)

func _frames() -> void:
	await process_frame
	await process_frame
	await physics_frame

func _check(ok: bool, issue: String) -> void:
	if not ok: _failures.append(issue)
