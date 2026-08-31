# Audits every region cell scene for the authoring defects a `.tscn` cannot complain about.
#
#     godot --headless --path . --script res://tools/cell_scene_audit.gd
#
# Exits 0 when clean, 1 on any finding. Intended for `tools/world_quality_check.py` and CI.
#
# Why this exists
# ---------------
# `--validate` checks authored DATA (ids, cross-references, graph reachability) and
# `cell_mesh_census.gd` counts meshes. Neither one instantiates a cell and asks whether the thing
# the player will walk into is actually there. Every finding below is a defect that is invisible in
# the file and invisible in the editor outline, and produces no error at runtime:
#
#   INVISIBLE_MESH      a MeshInstance3D with no mesh and no visible descendant — a node that
#                       renders nothing and reads, in the outline, exactly like a building.
#   GHOST_COLLIDER      a building-sized StaticBody3D with nothing visible anywhere in its owner's
#                       subtree — an invisible wall the player walks into in an empty street.
#   DEAD_SHAPE          a CollisionShape3D with no shape, or disabled — collision that is not there.
#   NO_COLLISION        a building-sized model subtree with no collider at all — a wall you walk
#                       through.
#   BAD_TRANSFORM       a non-finite or zero-scaled transform. Godot silently keeps rendering.
#   BURIED / FLOATING   an authored Y far outside the clearance range the terrain conform assumes
#                       (WorldTerrainConform: an authored Y is a height ABOVE the ground).
extends SceneTree

const REGIONS := [
	"res://data/regions/EmberCrown.tres",
	"res://data/regions/FrostfangReach.tres",
]

# A collider at least this big on two horizontal axes is "building-sized": something the player will
# read as architecture rather than as a crate or a lamp post.
const BUILDING_SPAN := 3.0

# The band an authored Y may sit in. It is a clearance above the ground, so below zero is buried and
# anything above a two-storey roof is either flying or an authoring slip. `terrain_absolute` opts out.
const MIN_Y := -0.6
const MAX_Y := 12.0
const ABSOLUTE_GROUP := "terrain_absolute"

var _findings: Array[String] = []


func _initialize() -> void:
	for region_path in REGIONS:
		var region: Resource = load(region_path)
		if region == null:
			_findings.append("region %s did not load" % region_path)
			continue
		for cell in region.Cells:
			_audit_cell(region.Id, cell)

	if _findings.is_empty():
		print("cell scene audit: clean.")
		quit(0)
		return

	printerr("cell scene audit: %d finding(s)" % _findings.size())
	for f in _findings:
		printerr("  " + f)
	quit(1)


func _audit_cell(region_id: String, cell) -> void:
	var packed: PackedScene = load(cell.ScenePath)
	if packed == null:
		_findings.append("%s/%s: scene '%s' did not load" % [region_id, cell.Id, cell.ScenePath])
		return
	var root: Node = packed.instantiate()
	if root == null:
		_findings.append("%s/%s: scene did not instantiate" % [region_id, cell.Id])
		return
	if not (root is Node3D):
		_findings.append("%s/%s: scene root is %s, not a Node3D" % [region_id, cell.Id, root.get_class()])
	_walk(cell.Id, root, root)
	_check_authored_heights(cell.Id, root, _colliders(root))
	root.free()


func _walk(cell_id: String, node: Node, root: Node) -> void:
	if node is Node3D:
		var t: Transform3D = node.transform
		if not _finite(t):
			_findings.append("%s: BAD_TRANSFORM at %s (non-finite)" % [cell_id, _path(node, root)])
		elif t.basis.get_scale().length() < 0.001:
			_findings.append("%s: BAD_TRANSFORM at %s (zero scale)" % [cell_id, _path(node, root)])

	if node is MeshInstance3D and node.mesh == null and not _has_visible(node):
		_findings.append("%s: INVISIBLE_MESH at %s (MeshInstance3D with no mesh and nothing visible under it)"
			% [cell_id, _path(node, root)])

	if node is CollisionShape3D:
		if node.shape == null:
			_findings.append("%s: DEAD_SHAPE at %s (no shape)" % [cell_id, _path(node, root)])
		elif node.disabled:
			_findings.append("%s: DEAD_SHAPE at %s (disabled)" % [cell_id, _path(node, root)])

	if node is StaticBody3D:
		var span := _collider_span(node)
		if span.x >= BUILDING_SPAN and span.z >= BUILDING_SPAN:
			# The owner is the authored placement node the collider hangs under; a building's mesh is
			# a sibling of the collider far more often than a child of it.
			var owner_node: Node = node.get_parent() if node.get_parent() != null else node
			if not _has_visible(owner_node):
				_findings.append("%s: GHOST_COLLIDER at %s (%.1f x %.1f m of collision, nothing visible in %s)"
					% [cell_id, _path(node, root), span.x, span.z, _path(owner_node, root)])

	for child in node.get_children():
		_walk(cell_id, child, root)


# Direct children of the cell root (and of its Nav region) are what WorldTerrainConform lifts, so
# their authored Y is the one that has to be a clearance.
func _check_authored_heights(cell_id: String, root: Node, colliders: Array) -> void:
	_check_children(cell_id, root, root, colliders)
	var nav: Node = root.get_node_or_null("Nav")
	if nav != null:
		_check_children(cell_id, nav, root, colliders)


func _check_children(cell_id: String, parent: Node, root: Node, colliders: Array) -> void:
	for child in parent.get_children():
		if not (child is Node3D):
			continue
		_check_walkthrough(cell_id, child, root, colliders)
		if child.is_in_group(ABSOLUTE_GROUP):
			continue
		var y: float = child.position.y
		if y < MIN_Y:
			_findings.append("%s: BURIED %s at authored y=%.2f (an authored Y is a clearance above the ground)"
				% [cell_id, _path(child, root), y])
		elif y > MAX_Y:
			_findings.append("%s: FLOATING %s at authored y=%.2f (join '%s' if that Y is a real world height)"
				% [cell_id, _path(child, root), y, ABSOLUTE_GROUP])


# A placement whose visible geometry is building-sized must stop the player.
#
# ⚠️ IT IS A SPATIAL TEST, NOT AN OWNERSHIP TEST, AND IT HAS TO BE. Asking "does this node own a
# collider" reports every correctly-built arena: the twelve `Ring*` wall segments are the visible
# stonework and the six `Wall*` StaticBody3Ds beside them are the barrier. The question the player
# actually asks is whether something stops them AT THAT PLACE.
func _check_walkthrough(cell_id: String, placement: Node3D, root: Node, colliders: Array) -> void:
	var aabb := _visible_aabb(placement)
	# A WALL is the shape this has to catch and it is thin on one axis, so the test is the LONGER
	# horizontal span, not both of them.
	if maxf(aabb.size.x, aabb.size.z) < BUILDING_SPAN or aabb.size.y < 2.0:
		return
	var here := _relative(root, placement) * aabb
	# Grown a little: a collider authored to the wall's line rather than its skin still stops you.
	here = here.grow(0.5)
	for box in colliders:
		if here.intersects(box):
			return
	_findings.append("%s: NO_COLLISION at %s (%.1f x %.1f x %.1f m of visible geometry and no collider anywhere in it)"
		% [cell_id, _path(placement, root), aabb.size.x, aabb.size.y, aabb.size.z])


# Every collision shape in the cell, as boxes in the cell root's frame.
func _colliders(root: Node) -> Array:
	var out: Array = []
	for shape_node in _shapes(root):
		var s: Shape3D = shape_node.shape
		var size := Vector3.ZERO
		if s is BoxShape3D:
			size = s.size
		elif s is CylinderShape3D:
			size = Vector3(s.radius * 2.0, s.height, s.radius * 2.0)
		elif s is SphereShape3D:
			size = Vector3.ONE * s.radius * 2.0
		elif s is CapsuleShape3D:
			size = Vector3(s.radius * 2.0, s.height + (s.radius * 2.0), s.radius * 2.0)
		elif s is ConcavePolygonShape3D or s is ConvexPolygonShape3D:
			# Mesh collision: take its own bounds rather than skipping it, or a trimesh-collided
			# building reads as having no collision at all.
			var pts: PackedVector3Array = s.get_faces() if s is ConcavePolygonShape3D else s.points
			if pts.is_empty():
				continue
			var local := AABB(pts[0], Vector3.ZERO)
			for pt in pts:
				local = local.expand(pt)
			out.append(_relative(root, shape_node) * local)
			continue
		else:
			continue
		out.append(_relative(root, shape_node) * AABB(-size * 0.5, size))
	return out


func _shapes(node: Node) -> Array:
	var out: Array = []
	if node is CollisionShape3D and node.shape != null and not node.disabled:
		out.append(node)
	for child in node.get_children():
		out.append_array(_shapes(child))
	return out


# Local-space bounds of every mesh under `node`, in `node`'s own frame.
func _visible_aabb(node: Node3D) -> AABB:
	var out := AABB()
	var seeded := false
	for m in _meshes(node):
		var box: AABB = _relative(node, m) * m.mesh.get_aabb()
		out = box if not seeded else out.merge(box)
		seeded = true
	return out


func _relative(ancestor: Node3D, node: Node3D) -> Transform3D:
	var t := Transform3D.IDENTITY
	var n: Node = node
	while n != null and n != ancestor:
		if n is Node3D:
			t = n.transform * t
		n = n.get_parent()
	return t


func _meshes(node: Node) -> Array:
	var out: Array = []
	if node is MeshInstance3D and node.mesh != null:
		out.append(node)
	for child in node.get_children():
		out.append_array(_meshes(child))
	return out


func _has_visible(node: Node) -> bool:
	if node is MeshInstance3D and node.mesh != null:
		return true
	if node is GeometryInstance3D and not (node is MeshInstance3D):
		return true  # MultiMesh, CSG and the like carry their own geometry
	for child in node.get_children():
		if _has_visible(child):
			return true
	return false


func _collider_span(body: CollisionObject3D) -> Vector3:
	var span := Vector3.ZERO
	for child in body.get_children():
		if child is CollisionShape3D and child.shape != null:
			var s: Shape3D = child.shape
			var size := Vector3.ZERO
			if s is BoxShape3D:
				size = s.size
			elif s is CylinderShape3D:
				size = Vector3(s.radius * 2.0, s.height, s.radius * 2.0)
			elif s is SphereShape3D:
				size = Vector3.ONE * s.radius * 2.0
			elif s is CapsuleShape3D:
				size = Vector3(s.radius * 2.0, s.height, s.radius * 2.0)
			else:
				continue  # concave/convex meshes are terrain and props, not building boxes
			var scaled: Vector3 = size * child.transform.basis.get_scale().abs()
			span = Vector3(maxf(span.x, scaled.x), maxf(span.y, scaled.y), maxf(span.z, scaled.z))
	return span * body.transform.basis.get_scale().abs()


func _finite(t: Transform3D) -> bool:
	for v in [t.origin, t.basis.x, t.basis.y, t.basis.z]:
		if not (is_finite(v.x) and is_finite(v.y) and is_finite(v.z)):
			return false
	return true


func _path(node: Node, root: Node) -> String:
	var parts: Array[String] = []
	var n: Node = node
	while n != null and n != root:
		parts.push_front(str(n.name))
		n = n.get_parent()
	return "/".join(parts) if not parts.is_empty() else "<root>"
