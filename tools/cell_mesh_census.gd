# Counts the RENDERED meshes each region cell actually instantiates, deterministically.
#
#     godot --headless --path . --script res://tools/cell_mesh_census.gd
#
# Why this exists
# ---------------
# `--validate` counts AUTHORED nodes — the `[node ...]` blocks in the `.tscn`. That is the number
# the per-cell budget is written against, and it is not what the GPU sees: one authored node that
# instances a modular building brings a whole subtree of MeshInstance3Ds with it, so a cell can shed
# authored nodes and gain draw calls at the same time. The `--play` draw-call warning is real but it
# moves 1,000 either way between runs depending on where the camera happens to look and what the
# EncounterDirector spawned, which makes it useless for answering "did this change make it worse".
#
# This instantiates every cell of every region and counts the MeshInstance3D descendants, which is
# stable across runs and directly comparable between two commits.
extends SceneTree

const REGIONS := [
	"res://data/regions/EmberCrown.tres",
	"res://data/regions/FrostfangReach.tres",
]


func _initialize() -> void:
	var grand_total := 0
	for region_path in REGIONS:
		var region: Resource = load(region_path)
		if region == null:
			printerr("cell mesh census: could not load %s" % region_path)
			quit(1)
			return
		var region_total := 0
		print("%s" % region.Id)
		for cell in region.Cells:
			var packed: PackedScene = load(cell.ScenePath)
			if packed == null:
				printerr("  %s: scene did not load" % cell.Id)
				quit(1)
				return
			var instance: Node = packed.instantiate()
			var meshes := _count(instance)
			instance.free()
			region_total += meshes
			print("  %-38s %5d meshes" % [cell.Id, meshes])
		print("  %-38s %5d meshes" % ["TOTAL", region_total])
		grand_total += region_total
	print("grand total %d meshes" % grand_total)
	quit(0)


func _count(node: Node) -> int:
	var n := 1 if node is MeshInstance3D else 0
	for child in node.get_children():
		n += _count(child)
	return n
