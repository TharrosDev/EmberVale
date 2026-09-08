extends SceneTree
## SDK entry point; the editor scene uses the same route runner.
func _initialize() -> void:
    _start.call_deferred()
func _start() -> void:
    root.add_child(load("res://tools/environment_route_runner.gd").new())
