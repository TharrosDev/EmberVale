extends Node3D

## Puts landmark_detail.gdshader on every surface under a scenes/props/lm_*.tscn wrapper.
##
## WHY A SCRIPT AND NOT A MATERIAL IN THE SCENE FILE. The shader needs the model's own albedo,
## normal and ORM textures. Those live inside the imported .glb scene, on mesh surfaces whose node
## names and texture files only exist after Godot has imported the model, and the wrapper is
## written before that. So the wrapper names no texture: this reads each surface's imported
## StandardMaterial3D when the landmark enters the tree and builds the shader material from it.
## One material per source material and setting is built and shared by every instance of the
## landmark, so sixty monoliths are still one material. The cache holds it weakly: when the last
## instance leaves, the material and the atlases it holds are freed with the model.
##
## The alternatives were worse: an import script or `_subresources` override ties the material to
## the .import of every model (and any .import change stales the whole world bake); a material
## .tres per landmark has to name texture files that do not exist until the importer has extracted
## them; a `next_pass` overlay costs a second draw of the mesh and cannot touch the normal.
##
## A scripted root also keeps the landmark out of WorldArchitectureBatcher (it skips scripted
## nodes), so the mesh stays its own node and this runs the same in a baked cell and a loose one.
## Not a @tool script: the editor shows the plain imported material.

const DETAIL_SHADER: Shader = preload("res://assets/shaders/world/landmark_detail.gdshader")

## Metres across one coarse lump of grain.
@export var detail_scale: float = 0.8
## Scales both the albedo and the normal effect. 0 leaves the imported look, 1 is the shader default.
@export var detail_strength: float = 1.0
## Multiplies the model's albedo. Several generated stone atlases came back near-white; a grey here
## weathers them at no cost (tools/make_landmark_wrapper.py --slices suggests the value).
@export var albedo_tint: Color = Color.WHITE

static var _shared: Dictionary = {}


func _ready() -> void:
	for node: Node in find_children("*", "MeshInstance3D", true, false):
		var mesh_instance := node as MeshInstance3D
		if mesh_instance.mesh == null:
			continue
		for surface: int in mesh_instance.mesh.get_surface_count():
			var source := mesh_instance.get_active_material(surface) as BaseMaterial3D
			# Null covers both "no material" and "already converted" (a ShaderMaterial is not a BaseMaterial3D).
			if source == null or source.transparency != BaseMaterial3D.TRANSPARENCY_DISABLED:
				continue
			mesh_instance.set_surface_override_material(surface, _detail_material(source))


func _detail_material(source: BaseMaterial3D) -> ShaderMaterial:
	var key := "%d|%.3f|%.3f|%s" % [source.get_instance_id(), detail_scale, detail_strength, albedo_tint.to_html()]
	var cached := _shared.get(key) as WeakRef
	if cached != null and cached.get_ref() != null:
		return cached.get_ref() as ShaderMaterial
	var material := ShaderMaterial.new()
	material.shader = DETAIL_SHADER
	material.set_shader_parameter("albedo_color", source.albedo_color * albedo_tint)
	material.set_shader_parameter("roughness_value", source.roughness)
	material.set_shader_parameter("metallic_value", source.metallic)
	var albedo := source.albedo_texture
	if albedo != null:
		material.set_shader_parameter("albedo_texture", albedo)
		material.set_shader_parameter("has_albedo_texture", true)
	if source.normal_enabled and source.normal_texture != null:
		material.set_shader_parameter("normal_texture", source.normal_texture)
		material.set_shader_parameter("normal_scale", source.normal_scale)
		material.set_shader_parameter("has_normal_texture", true)
	# glTF packs occlusion, roughness and metallic into one image. The importer hands it to a
	# StandardMaterial3D as its roughness (G) and metallic (B) textures, and to an ORMMaterial3D whole.
	var orm := source.get_texture(BaseMaterial3D.TEXTURE_ORM)
	var packed_occlusion := orm != null
	if orm == null:
		orm = source.get_texture(BaseMaterial3D.TEXTURE_ROUGHNESS)
		packed_occlusion = source.ao_enabled and source.get_texture(BaseMaterial3D.TEXTURE_AMBIENT_OCCLUSION) == orm
	if orm != null:
		material.set_shader_parameter("orm_texture", orm)
		material.set_shader_parameter("has_orm_texture", true)
		material.set_shader_parameter("orm_has_occlusion", packed_occlusion)
	material.set_shader_parameter("detail_scale", detail_scale)
	material.set_shader_parameter("detail_albedo", 0.3 * detail_strength)
	material.set_shader_parameter("detail_bump", detail_strength)
	_shared[key] = weakref(material)
	return material
