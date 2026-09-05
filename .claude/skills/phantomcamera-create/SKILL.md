---
name: phantomcamera-create
description: "Create a PhantomCamera3D node (a Cinemachine-style virtual camera from the Phantom Camera addon) in the currently edited Godot scene and return its structured config. Optionally pass 'name' to rename it, 'parentPath' (a node path relative to the scene root) to parent it (defaults to the scene root), and 'priority' to seed its priority (higher wins). The new node's owner is set to the scene root so it is saved with the scene. Returns a structured installed:false result when the Phantom Camera addon is not installed."
---

# PhantomCamera / Create

Create a PhantomCamera3D node (a Cinemachine-style virtual camera from the Phantom Camera addon) in the currently edited Godot scene and return its structured config. Optionally pass 'name' to rename it, 'parentPath' (a node path relative to the scene root) to parent it (defaults to the scene root), and 'priority' to seed its priority (higher wins). The new node's owner is set to the scene root so it is saved with the scene. Returns a structured installed:false result when the Phantom Camera addon is not installed.

## How to Call

### HTTP API (Direct Tool Execution)

Execute this tool directly via the MCP Plugin HTTP API:

```bash
curl -X POST http://localhost:23630/api/tools/phantomcamera-create \
  -H "Content-Type: application/json" \
  -d '{
  "name": "string_value",
  "parentPath": "string_value",
  "priority": "string_value"
}'
```

> For complex input (multi-line strings, code), save the JSON to a file and use `-d @args.json`.
>
> Or pipe via stdin:
> ```bash
> curl -X POST http://localhost:23630/api/tools/phantomcamera-create -H "Content-Type: application/json" -d @- <<'EOF'
> {"param": "value"}
> EOF
> ```

#### With Authorization (if required)

```bash
curl -X POST http://localhost:23630/api/tools/phantomcamera-create \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer YOUR_TOKEN" \
  -d '{
  "name": "string_value",
  "parentPath": "string_value",
  "priority": "string_value"
}'
```

## Input

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `name` | `string` | No | Name for the new PhantomCamera3D node. When omitted, Godot's default name is used. |
| `parentPath` | `string` | No | Node path (relative to the edited scene root) of the parent. When omitted, the node is parented to the scene root. |
| `priority` | `any` | No | Optional initial priority (Phantom Camera 'priority'); higher wins when multiple PhantomCameras are active. Defaults to the addon's default (0). |

### Input JSON Schema

```json
{
  "type": "object",
  "properties": {
    "name": {
      "type": "string",
      "description": "Name for the new PhantomCamera3D node. When omitted, Godot's default name is used."
    },
    "parentPath": {
      "type": "string",
      "description": "Node path (relative to the edited scene root) of the parent. When omitted, the node is parented to the scene root."
    },
    "priority": {
      "$ref": "#/$defs/System.Int32",
      "description": "Optional initial priority (Phantom Camera 'priority'); higher wins when multiple PhantomCameras are active. Defaults to the addon's default (0)."
    }
  },
  "$defs": {
    "System.Int32": {
      "type": "integer"
    }
  }
}
```

## Output

### Output JSON Schema

```json
{
  "type": "object",
  "properties": {
    "result": {
      "$ref": "#/$defs/com.IvanMurzak.Godot.MCP.PhantomCamera.PhantomCameraInfo"
    }
  },
  "$defs": {
    "com.IvanMurzak.Godot.MCP.PhantomCamera.PhantomCameraInfo": {
      "type": "object",
      "properties": {
        "Installed": {
          "type": "boolean"
        },
        "Addon": {
          "type": "string"
        },
        "MissingClass": {
          "type": "string"
        },
        "Hint": {
          "type": "string"
        },
        "NodePath": {
          "type": "string"
        },
        "TypeName": {
          "type": "string"
        },
        "CameraPath": {
          "type": "string"
        },
        "Priority": {
          "type": "integer"
        },
        "FollowMode": {
          "type": "string"
        },
        "FollowTarget": {
          "type": "string"
        },
        "LookAtMode": {
          "type": "string"
        },
        "LookAtTarget": {
          "type": "string"
        },
        "FollowDamping": {
          "type": "boolean"
        },
        "LookAtDamping": {
          "type": "boolean"
        }
      },
      "required": [
        "Installed",
        "Priority",
        "FollowDamping",
        "LookAtDamping"
      ]
    }
  },
  "required": [
    "result"
  ]
}
```

