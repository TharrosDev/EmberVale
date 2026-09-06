"""Finite Godot-native construction plans. Data only; source scripts are never accepted."""
import math
import re

NODE_TYPES = {"Node", "Node3D", "Marker3D", "MeshInstance3D", "Camera3D", "DirectionalLight3D",
              "OmniLight3D", "SpotLight3D", "WorldEnvironment", "StaticBody3D", "Area3D",
              "CollisionShape3D", "NavigationRegion3D", "NavigationLink3D"}
RESOURCE_TYPES = {"StandardMaterial3D", "BoxMesh", "SphereMesh", "CylinderMesh", "PlaneMesh",
                  "CapsuleMesh", "BoxShape3D", "SphereShape3D", "CapsuleShape3D", "CylinderShape3D",
                  "Environment", "NavigationMesh"}
OPERATIONS = {"create_node", "instantiate_scene", "create_resource", "customize", "add_group", "remove_node"}
FORBIDDEN = {"script", "owner", "name", "resource_path", "scene_file_path", "process_mode"}


def project_resource(value, scene=False):
    if not isinstance(value, str) or not value.startswith("res://") or ".." in value[6:].split("/") or "\\" in value:
        raise ValueError("authoring resources must use project-local res:// paths without traversal")
    extensions = (".tscn", ".scn") if scene else (".tscn", ".scn", ".tres", ".res", ".glb", ".gltf", ".png", ".jpg", ".webp")
    if not value.endswith(extensions):
        raise ValueError("unsupported resource type; scripts/code are not authoring data")
    return value


def properties(values):
    if not isinstance(values, dict):
        raise ValueError("properties must be an object")
    for key, value in values.items():
        if not re.fullmatch(r"[A-Za-z_][A-Za-z0-9_]*", key) or key in FORBIDDEN:
            raise ValueError(f"property is not authorable: {key}")
        if isinstance(value, dict):
            if set(value) == {"resource"} and isinstance(value["resource"], str):
                continue
            if set(value) == {"path"}:
                project_resource(value["path"])
                continue
            raise ValueError("property objects must be resource references ({resource: id} or {path: res://...})")
        items = value if isinstance(value, list) else [value]
        if len(items) > 256 or any(type(v) not in (str, int, float, bool, type(None)) for v in items):
            raise ValueError("property values must be primitive literals or bounded primitive arrays")
        if any(type(v) is float and not math.isfinite(v) for v in items):
            raise ValueError("authoring requires finite numbers")


def validate_author_plan(plan):
    if not isinstance(plan, dict) or not isinstance(plan.get("operations"), list) or len(plan["operations"]) > 1000:
        raise ValueError("author plan requires operations (at most 1000)")
    if not re.fullmatch(r"[A-Za-z_][A-Za-z0-9_]*", plan.get("name", "SdkBuild")):
        raise ValueError("authoring root name must be a safe node name")
    if "source" in plan:
        project_resource(plan["source"])
    ids = {"root"}
    for operation in plan["operations"]:
        if not isinstance(operation, dict) or operation.get("op") not in OPERATIONS:
            raise ValueError("unsupported authoring operation")
        op = operation["op"]
        if op in {"create_node", "instantiate_scene", "create_resource"}:
            name = operation.get("id", "")
            if not isinstance(name, str) or not re.fullmatch(r"[A-Za-z_][A-Za-z0-9_]*", name) or name in ids:
                raise ValueError("created IDs must be unique safe names")
            ids.add(name)
        if op == "create_node" and operation.get("type", "Node3D") not in NODE_TYPES:
            raise ValueError("node type is not in the construction allowlist")
        if op == "create_resource":
            if "source" in operation:
                project_resource(operation["source"])
            elif operation.get("type") not in RESOURCE_TYPES:
                raise ValueError("resource type is not in the construction allowlist")
        if op == "instantiate_scene":
            project_resource(operation.get("scene"), scene=True)
        if op in {"customize", "remove_node", "add_group"} and not isinstance(operation.get("target"), str):
            raise ValueError(op + " requires target (an ID or relative node path)")
        for key in ("target", "parent"):
            if key in operation and (not isinstance(operation[key], str) or operation[key].startswith(("/", "res:", "user:")) or ".." in operation[key].split("/")):
                raise ValueError("authoring targets must stay inside the constructed scene")
        if op == "remove_node" and operation["target"] in {"root", ".", ""}:
            raise ValueError("the construction root cannot be removed")
        if op == "add_group" and not re.fullmatch(r"[A-Za-z_][A-Za-z0-9_]*", operation.get("group", "")):
            raise ValueError("group must be a safe name")
        properties(operation.get("properties", {}))
    return plan
