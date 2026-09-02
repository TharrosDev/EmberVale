@tool
extends SceneTree

# Proves the shared-texture rewrite actually reaches the GAME, not just the file.
#
# ⚠️ A GLB WHOSE IMAGE IS A URI STILL IMPORTS WHEN THE URI RESOLVES TO NOTHING — the surface
# just comes back with a null albedo and renders flat white, which in a MultiMesh scatter layer
# reads as "the grass went pale" rather than as a missing file. So the check is not "did it
# import" but "does surface 0 have an albedo texture, and is it the SAME Texture2D object across
# every prop in the family". Sharing is the whole point; two distinct textures of identical bytes
# is the state this pass exists to remove.

# family -> [the texture every member must share, [members...]]
#
# ⚠️ THE ASSERTION IS "THE SHARED MAP HAS EVERY MEMBER", NOT "THE FAMILY HAS ONE TEXTURE".
# A broadleaf tree legitimately carries bark AND a leaf atlas, and the flowering bush carries
# its own blossom map — counting distinct textures fails a correct result. What must hold is
# that the map they have in common resolves to ONE imported resource with the full member count.
const FAMILIES := {
	"leaves": ["T_Nature_Leaves.png",
		["prp_clover.glb", "prp_fern.glb", "prp_flowers_a.glb", "prp_flowers_b.glb"]],
	"grass": ["T_Nature_Grass.png",
		["prp_grass_short.glb", "prp_grass_tall.glb", "prp_grass_wispy.glb"]],
	"pathrocks": ["T_Nature_PathRocks.png",
		["prp_pebble_a.glb", "prp_pebble_b.glb", "prp_rockpath_small.glb", "prp_rockpath_wide.glb"]],
	"broadleaf": ["T_Nature_LeafBroadleaf.png",
		["prp_tree_broadleaf.glb", "prp_bush_flowering.glb"]],
}


func _find_meshes(node: Node, out: Array) -> void:
	if node is MeshInstance3D and node.mesh != null:
		out.append(node)
	for child in node.get_children():
		_find_meshes(child, out)


func _init() -> void:
	var failures := 0
	for family in FAMILIES:
		var shared: String = "res://assets/models/props/%s" % FAMILIES[family][0]
		var members: Array = FAMILIES[family][1]
		var seen := {}
		for filename in members:
			var path := "res://assets/models/props/%s" % filename
			var scene: PackedScene = load(path)
			if scene == null:
				print("FAIL %s: did not load" % filename)
				failures += 1
				continue
			var root: Node = scene.instantiate()
			var meshes: Array = []
			_find_meshes(root, meshes)
			if meshes.is_empty():
				print("FAIL %s: no MeshInstance3D" % filename)
				failures += 1
				root.free()
				continue
			var textured := 0
			for mesh_instance in meshes:
				var mesh: Mesh = mesh_instance.mesh
				for surface in mesh.get_surface_count():
					var material := mesh.surface_get_material(surface)
					if material is StandardMaterial3D and material.albedo_texture != null:
						textured += 1
						seen[material.albedo_texture.resource_path] = \
							seen.get(material.albedo_texture.resource_path, 0) + 1
			if textured == 0:
				print("FAIL %s: no surface carries an albedo texture" % filename)
				failures += 1
			root.free()
		print("%-10s -> %s" % [family, seen])
		if seen.get(shared, 0) != members.size():
			print("FAIL %s: %s has %d users, expected %d — the family is not sharing one texture"
				% [family, shared, seen.get(shared, 0), members.size()])
			failures += 1

	if failures > 0:
		print("nature_texture_probe: FAIL (%d)" % failures)
		quit(1)
	else:
		print("nature_texture_probe: PASS")
		quit(0)
