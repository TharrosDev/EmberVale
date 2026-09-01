#!/usr/bin/env python3
"""Blender-side geometry inspection and truthful six-view diagnostic rendering.

Run through audit_3d.py or directly with Blender. Production files are imported into an empty
temporary scene and never saved or exported.
"""
from __future__ import annotations
import argparse, hashlib, json, math, struct, sys
from pathlib import Path
import bpy
from mathutils import Vector

def args_after_dash():
    values = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    p=argparse.ArgumentParser(); p.add_argument("--root",type=Path,required=True); p.add_argument("--output",type=Path,required=True)
    p.add_argument("--render",choices=("none","selected","all"),default="none"); p.add_argument("--asset",action="append",default=[]); p.add_argument("--render-size",type=int,default=320)
    p.add_argument("--pose",action="append",choices=("idle","movement","attack","equipment"),default=[])
    return p.parse_args(values)

def clear():
    bpy.ops.object.select_all(action="SELECT"); bpy.ops.object.delete(use_global=False)
    for collection in (bpy.data.meshes,bpy.data.materials,bpy.data.images,bpy.data.armatures,bpy.data.actions,bpy.data.cameras,bpy.data.lights,bpy.data.curves):
        for block in list(collection): collection.remove(block)

def import_model(path):
    bpy.ops.import_scene.gltf(filepath=str(path), import_shading="NORMALS")

def is_production_object(obj):
    # Godot/Rigify exports can carry bone-display helpers in this explicitly excluded collection.
    # Including its Icosphere in bounds produced the exact false measurement warned about in
    # ASSET_POLICY.md §0.6.
    return not any(collection.name.startswith("glTF_not_exported") for collection in obj.users_collection)

def bounds(objects):
    points=[]
    for obj in objects:
        if obj.type=="MESH": points += [obj.matrix_world @ Vector(corner) for corner in obj.bound_box]
    if not points: return None,None,None
    lo=[min(p[i] for p in points) for i in range(3)]; hi=[max(p[i] for p in points) for i in range(3)]
    return lo,hi,[hi[i]-lo[i] for i in range(3)]

def geometry_hash(objects):
    digest=hashlib.sha256(); deps=bpy.context.evaluated_depsgraph_get()
    for obj in sorted((o for o in objects if o.type=="MESH"),key=lambda o:o.name):
        evaluated=obj.evaluated_get(deps); mesh=evaluated.to_mesh(); world=obj.matrix_world
        digest.update(struct.pack("<II",len(mesh.vertices),len(mesh.polygons)))
        for vertex in mesh.vertices:
            co=world @ vertex.co; digest.update(struct.pack("<3i",*(round(v*100000) for v in co)))
        for polygon in mesh.polygons:
            digest.update(struct.pack("<I",len(polygon.vertices))); digest.update(struct.pack("<"+"I"*len(polygon.vertices),*polygon.vertices))
        evaluated.to_mesh_clear()
    return digest.hexdigest()

def add_material(name,color,metallic=0.0,roughness=0.7):
    mat=bpy.data.materials.new(name); mat.diffuse_color=(*color,1); mat.metallic=metallic; mat.roughness=roughness; return mat

def add_diagnostics(lo,hi):
    center=Vector([(lo[i]+hi[i])/2 for i in range(3)]); dims=[hi[i]-lo[i] for i in range(3)]; scale=max(dims)
    bpy.ops.mesh.primitive_plane_add(size=max(scale*3,3),location=(center.x,center.y,0)); plane=bpy.context.object; plane.name="QA_Ground"; plane.data.materials.append(add_material("QA Ground",(0.13,0.15,0.14)))
    bpy.ops.mesh.primitive_cube_add(size=1,location=center); box=bpy.context.object; box.name="QA_Bounds"; box.dimensions=dims; bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
    box.display_type="WIRE"; box.data.materials.append(add_material("QA Bounds",(1.0,0.45,0.08)))
    wire=box.modifiers.new("QA wire","WIREFRAME"); wire.thickness=max(scale*0.002,0.003)
    # 1.8 m human reference, deliberately simple and neutral.
    human_root=bpy.data.objects.new("QA_Human_Root",None); bpy.context.scene.collection.objects.link(human_root)
    bpy.ops.mesh.primitive_cylinder_add(vertices=12,radius=.18,depth=1.45,location=(0,0,.725)); human=bpy.context.object; human.name="QA_Human_1p8m"; human.data.materials.append(add_material("QA Human",(.18,.42,.72))); human.parent=human_root
    bpy.ops.mesh.primitive_uv_sphere_add(segments=16,ring_count=8,radius=.18,location=(0,0,1.62)); head=bpy.context.object; head.data.materials.append(bpy.data.materials["QA Human"]); head.parent=human_root
    # Origin axes rendered as colored cylinders.
    for name,axis,color in (("X",Vector((1,0,0)),(.9,.1,.08)),("Y",Vector((0,1,0)),(.08,.8,.15)),("Z",Vector((0,0,1)),(.08,.25,.95))):
        length=max(scale*.18,.25); bpy.ops.mesh.primitive_cylinder_add(vertices=8,radius=max(scale*.004,.006),depth=length,location=axis*length*.5)
        obj=bpy.context.object; obj.name="QA_Origin_"+name; obj.rotation_mode="QUATERNION"; obj.rotation_quaternion=Vector((0,0,1)).rotation_difference(axis); obj.data.materials.append(add_material("QA "+name,color))
    return human_root

def look_at(obj,target): obj.rotation_euler=(Vector(target)-obj.location).to_track_quat("-Z","Y").to_euler()

def setup_render(size):
    scene=bpy.context.scene
    # Blender 5.1 exposes Eevee as BLENDER_EEVEE (the old 4.x identifier was
    # BLENDER_EEVEE_NEXT). Querying RNA keeps this reproducible across both lines.
    engines={item.identifier for item in scene.render.bl_rna.properties["engine"].enum_items}
    scene.render.engine="BLENDER_EEVEE" if "BLENDER_EEVEE" in engines else "BLENDER_EEVEE_NEXT"
    scene.render.resolution_x=size; scene.render.resolution_y=size; scene.render.resolution_percentage=100
    scene.render.image_settings.file_format="PNG"; scene.render.film_transparent=False; scene.world.color=(.035,.045,.055)
    bpy.ops.object.light_add(type="AREA",location=(4,-5,7)); bpy.context.object.data.energy=1100; bpy.context.object.data.shape="DISK"; bpy.context.object.data.size=5
    bpy.ops.object.light_add(type="AREA",location=(-4,2,4)); bpy.context.object.data.energy=650; bpy.context.object.data.size=4
    bpy.ops.object.camera_add(); camera=bpy.context.object; camera.data.lens=58; scene.camera=camera; return camera

def frame_camera(camera, center, extent, direction, human_root):
    direction=Vector(direction).normalized(); distance=max(extent*2.5+1.2,4.5)
    camera.location=center+direction*distance+Vector((0,0,extent*.08)); look_at(camera,center)
    screen_right=camera.rotation_euler.to_quaternion() @ Vector((1,0,0))
    human_root.location=center+screen_right*(extent*.55+.35); human_root.location.z=0

def update_bounds_box(lo,hi):
    box=bpy.data.objects.get("QA_Bounds")
    if box is None: return
    box.location=Vector([(lo[i]+hi[i])/2 for i in range(3)]); box.dimensions=[hi[i]-lo[i] for i in range(3)]

def render_views(output,stem,lo,hi,size,model_objects,pose_slots):
    human_root=add_diagnostics(lo,hi); camera=setup_render(size); center=Vector([(lo[i]+hi[i])/2 for i in range(3)]); extent=max(hi[i]-lo[i] for i in range(3)); distance=max(extent*2.2,2.5)
    views={"front":(0,-1,0),"back":(0,1,0),"left":(-1,0,0),"right":(1,0,0),"front_3q":(1,-1,.35),"rear_3q":(-1,1,.35)}
    output.mkdir(parents=True,exist_ok=True)
    for name,direction in views.items():
        frame_camera(camera,center,extent,direction,human_root)
        bpy.context.scene.render.filepath=str(output/f"{stem}__{name}.png"); bpy.ops.render.render(write_still=True)
    aliases={"idle":("idle_neutral","idle"),"movement":("walk","run"),"attack":("sword_slash","slash","attack","punch"),"equipment":("equip","idle_sword","guard","block")}
    armature=next((obj for obj in model_objects if obj.type=="ARMATURE"),None)
    for slot in pose_slots:
        action=next((candidate for term in aliases[slot] for candidate in bpy.data.actions if term in candidate.name.lower()),None)
        if armature is None or action is None:
            print(f"AUDIT_3D_POSE_UNRESOLVED {stem} {slot}"); continue
        armature.animation_data_create(); armature.animation_data.action=action
        first,last=action.frame_range; bpy.context.scene.frame_set(round((first+last)*.5)); bpy.context.view_layer.update()
        pose_lo,pose_hi,pose_dims=bounds(model_objects)
        if pose_lo is None: print(f"AUDIT_3D_POSE_EMPTY {stem} {slot}"); continue
        pose_center=Vector([(pose_lo[i]+pose_hi[i])/2 for i in range(3)]); pose_extent=max(pose_dims); update_bounds_box(pose_lo,pose_hi)
        frame_camera(camera,pose_center,pose_extent,views["front_3q"],human_root)
        bpy.context.scene.render.filepath=str(output/f"{stem}__pose_{slot}.png"); bpy.ops.render.render(write_still=True)

def main():
    a=args_after_dash(); root=a.root.resolve(); selected={Path(x).as_posix() for x in a.asset}; models=sorted((root/"assets"/"models").rglob("*.glb"))+sorted((root/"assets"/"models").rglob("*.gltf")); results={}
    for index,path in enumerate(models,1):
        clear(); key=path.relative_to(root).as_posix(); print(f"AUDIT_3D_BLENDER [{index}/{len(models)}] {key}")
        try:
            import_model(path); imported=[obj for obj in bpy.context.scene.objects if is_production_object(obj)]; meshes=[o for o in imported if o.type=="MESH"]; lo,hi,dims=bounds(imported)
            record={"object_count":len(imported),"mesh_object_count":len(meshes),"armature_object_count":sum(o.type=="ARMATURE" for o in imported),"negative_transform_count":sum(o.matrix_world.to_3x3().determinant()<0 for o in imported),"aabb_min":lo,"aabb_max":hi,"dimensions":dims,"geometry_sha256":geometry_hash(imported),"materials":[{"name":m.name,"metallic":float(m.metallic),"roughness":float(m.roughness)} for m in bpy.data.materials],"actions":[action.name for action in bpy.data.actions],"errors":[]}
            should=a.render=="all" or (a.render=="selected" and key in selected)
            if should and lo is not None:
                render_views(a.output.parent/"renders",Path(key).stem,lo,hi,a.render_size,imported,a.pose); record["rendered_views"]=6
            results[key]=record
        except Exception as exc: results[key]={"errors":[repr(exc)]}; print("AUDIT_3D_BLENDER_ERROR",key,repr(exc))
    a.output.parent.mkdir(parents=True,exist_ok=True); a.output.write_text(json.dumps({"schema_version":1,"blender_version":bpy.app.version_string,"assets":results},indent=2),encoding="utf-8")
    return 0 if all(not x.get("errors") for x in results.values()) else 1

if __name__=="__main__": raise SystemExit(main())
