---
name: phantomcamera-set-follow
description: "Set how an existing PhantomCamera3D follows: its follow mode and/or its follow target. Address the camera by 'nodePath' (relative to the edited scene root). 'followMode' is one of None/Glued/Simple/Group/Path/Framed/ThirdPerson (case-insensitive); 'targetPath' is a node path (relative to the scene root) of a Node3D to follow. Only the arguments you supply are changed. Returns the camera's updated config, or installed:false when the Phantom Camera addon is absent."
---

# PhantomCamera / Set Follow

Set how an existing PhantomCamera3D follows: its follow mode and/or its follow target. Address the camera by 'nodePath' (relative to the edited scene root). 'followMode' is one of None/Glued/Simple/Group/Path/Framed/ThirdPerson (case-insensitive); 'targetPath' is a node path (relative to the scene root) of a Node3D to follow. Only the arguments you supply are changed. Returns the camera's updated config, or installed:false when the Phantom Camera addon is absent.

## How to Call

### HTTP API (Direct Tool Execution)

Execute this tool directly via the MCP Plugin HTTP API:

```bash
curl -X POST http://localhost:23630/api/tools/phantomcamera-set-follow \
  -H "Content-Type: application/json" \
  -d '{
  "nodePath": "string_value",
  "followMode": "string_value",
  "targetPath": "string_value"
}'
```

> For complex input (multi-line strings, code), save the JSON to a file and use `-d @args.json`.
>
> Or pipe via stdin:
> ```bash
> curl -X POST http://localhost:23630/api/tools/phantomcamera-set-follow -H "Content-Type: application/json" -d @- <<'EOF'
> {"param": "value"}
> EOF
> ```

#### With Authorization (if required)

```bash
curl -X POST http://localhost:23630/api/tools/phantomcamera-set-follow \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer YOUR_TOKEN" \
  -d '{
  "nodePath": "string_value",
  "followMode": "string_value",
  "targetPath": "string_value"
}'
```

## Input

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `nodePath` | `string` | Yes | Node path (relative to the edited scene root) of the PhantomCamera3D to modify. |
| `followMode` | `string` | No | Follow mode: None, Glued, Simple, Group, Path, Framed, or ThirdPerson (case-insensitive). When omitted, the mode is left unchanged. |
| `targetPath` | `string` | No | Node path (relative to the edited scene root) of the follow target (a Node3D). When omitted, the target is left unchanged. |

### Input JSON Schema

```json
{
  "type": "object",
  "properties": {
    "nodePath": {
      "type": "string",
      "description": "Node path (relative to the edited scene root) of the PhantomCamera3D to modify."
    },
    "followMode": {
      "type": "string",
      "description": "Follow mode: None, Glued, Simple, Group, Path, Framed, or ThirdPerson (case-insensitive). When omitted, the mode is left unchanged."
    },
    "targetPath": {
      "type": "string",
      "description": "Node path (relative to the edited scene root) of the follow target (a Node3D). When omitted, the target is left unchanged."
    }
  },
  "required": [
    "nodePath"
  ]
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

