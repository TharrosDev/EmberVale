# Climate preparation tests run inside Godot; Resources cannot be constructed in xUnit.
extends SceneTree
var failures: Array[String] = []
func check(condition: bool,message: String) -> void:
    if not condition: failures.append(message); printerr("ERROR climate probe: " + message)
func _initialize() -> void:
    _run.call_deferred()
func _run() -> void:
    for slug in ["EmberCrown","FrostfangReach"]:
        var region: Resource = load("res://data/regions/"+slug+".tres")
        var id: String = region.get("Id")
        var prepared: Resource = load("res://data/world_bake/regions/"+id.replace(".","_")+".res")
        check(bool(prepared.call("IsValidFor",region)),slug+" package valid")
        var temperatures: PackedFloat32Array = prepared.get("Temperatures")
        var moisture: PackedFloat32Array = prepared.get("Moistures")
        var heights: PackedFloat32Array = prepared.get("Heights")
        check(temperatures.size()==heights.size() and moisture.size()==heights.size(),slug+" retains climate")
        var mean := 0.0
        for value in temperatures:
            check(value>=0 and value<=1,slug+" bounded temperature")
            mean += value
        mean /= maxi(1,temperatures.size())
        check(mean>.4 if slug=="EmberCrown" else mean<.3,slug+" climate identity")
        var copy: Resource = prepared.duplicate()
        copy.set("Temperatures",PackedFloat32Array([.5]))
        check(not bool(copy.call("IsValidFor",region)),"reject undersized climate grid")
        var oversized := temperatures.duplicate()
        oversized.append(.5)
        copy.set("Temperatures",oversized)
        check(not bool(copy.call("IsValidFor",region)),"reject oversized climate grid")
        copy.set("Temperatures",PackedFloat32Array())
        copy.set("Moistures",PackedFloat32Array())
        check(bool(copy.call("IsValidFor",region)),"legacy schema-1 grids remain compatible")
    print("Environment climate probe: "+str(failures.size())+" failures")
    quit(0 if failures.is_empty() else 1)
