"""Validate plans before launching Godot. No expression evaluator or arbitrary method dispatch."""
from __future__ import annotations

import math
import re

OPERATIONS = {"load_scene", "scene_tree", "get_property", "set_property", "call_method",
              "send_action", "wait_frames", "assert", "capture_screenshot", "collect_logs",
              "collect_metrics", "dump_state", "stop", "new_game", "wait_until", "build"}
METHODS = {"show", "hide", "make_current", "reset_physics_interpolation"}
PROPERTIES = {"position", "global_position", "rotation_degrees", "visible", "velocity", "fov"}
COMPARATORS = {"eq", "ne", "gt", "ge", "lt", "le", "exists"}


def resource_path(value):
    if not isinstance(value, str) or not value.startswith("res://") or ".." in value[6:].split("/") or "\\" in value:
        raise ValueError("scene must be a project-local res:// path without traversal")
    if not value.endswith(".tscn") and not value.endswith(".scn"):
        raise ValueError("scene must be a PackedScene (.tscn or .scn)")
    return value


def integer(value, name, low=0, high=100000):
    if type(value) is not int or not low <= value <= high:
        raise ValueError(f"{name} must be an integer in [{low}, {high}]")
    return value


def validate_plan(plan):
    if not isinstance(plan, dict) or not isinstance(plan.get("steps"), list) or len(plan["steps"]) > 1000:
        raise ValueError("scenario requires steps (at most 1000 operations)")
    if "scene" in plan:
        resource_path(plan["scene"])
    for step in plan["steps"]:
        if not isinstance(step, dict) or step.get("op") not in OPERATIONS:
            raise ValueError(f"unsupported operation: {step}")
        op = step["op"]
        if op == "build":
            from .authoring import validate_author_plan
            validate_author_plan(step.get("plan"))
            parent = step.get("parent", "")
            if not isinstance(parent, str) or not parent.startswith("/root/") or ".." in parent.split("/"):
                raise ValueError("build requires an absolute parent within the live scene")
        if op == "load_scene":
            resource_path(step.get("scene"))
        if op in {"get_property", "set_property", "call_method", "assert", "wait_until"}:
            if not isinstance(step.get("node"), str) or not step["node"].startswith("/root/") or ".." in step["node"].split("/"):
                raise ValueError(f"{op} requires an absolute /root/ node path without traversal")
        if op in {"get_property", "set_property"} and not isinstance(step.get("property"), str):
            raise ValueError(f"{op} requires property")
        if op == "set_property":
            prop, value = step["property"], step.get("value")
            if prop not in PROPERTIES:
                raise ValueError(f"property is not writable: {prop}")
            if prop in {"position", "global_position", "rotation_degrees", "velocity"}:
                if not isinstance(value, list) or len(value) != 3 or any(type(v) not in (int, float) or not math.isfinite(v) for v in value):
                    raise ValueError("vector properties require three finite numbers")
            elif prop == "visible" and type(value) is not bool:
                raise ValueError("visible requires a boolean")
            elif prop == "fov" and (type(value) not in (int, float) or not 1 <= value <= 179):
                raise ValueError("fov must be 1..179")
        if op == "call_method" and (step.get("method") not in METHODS or step.get("args")):
            raise ValueError("only documented zero-argument methods are callable")
        if op in {"wait_frames", "send_action"}:
            integer(step.get("frames", 1), "frames")
        if op == "send_action":
            if not isinstance(step.get("action"), str):
                raise ValueError("send_action requires an action name")
            strength = step.get("strength", 1.0)
            if type(strength) not in (int, float) or not 0 <= strength <= 1:
                raise ValueError("action strength must be 0..1")
        if op in {"assert", "wait_until"}:
            if step.get("comparison", "exists") not in COMPARATORS:
                raise ValueError("unsupported comparison")
            if step.get("comparison", "exists") != "exists" and not isinstance(step.get("property"), str):
                raise ValueError("property comparison requires property")
            integer(step.get("frames", 600), "frames", 1)
        if op == "capture_screenshot":
            if not re.fullmatch(r"[A-Za-z0-9_-]+", step.get("name", "capture")):
                raise ValueError("capture name must contain only letters, digits, _ and -")
    return plan
