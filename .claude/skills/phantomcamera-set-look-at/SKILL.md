---
name: phantomcamera-set-look-at
description: "Set how an existing PhantomCamera3D looks at a target: its look-at mode and/or its look-at target. Address the camera by 'nodePath' (relative to the edited scene root). 'lookAtMode' is one of None/Mimic/Simple/Group (case-insensitive); 'targetPath' is a node path (relative to the scene root) of a Node3D to look at. Only the arguments you supply are changed. Returns the camera's updated config, or installed:false when the Phantom Camera addon is absent."
---

# PhantomCamera / Set Look At

Set how an existing PhantomCamera3D looks at a target: its look-at mode and/or its look-at target. Address the camera by 'nodePath' (relative to the edited scene root). 'lookAtMode' is one of None/Mimic/Simple/Group (case-insensitive); 'targetPath' is a node path (relative to the scene root) of a Node3D to look at. Only the arguments you supply are changed. Returns the camera's updated config, or installed:false when the Phantom Camera addon is absent.

## How to Call

### HTTP API (Direct Tool Execution)

Execute this tool directly via the MCP Plugin HTTP API:

```bash
curl -X POST http://localhost:23630/api/tools/phantomcamera-set-look-at \
  -H "Content-Type: application/json" \
  -d '{
  "nodePath": "string_value",
  "lookAtMode": "string_value",
  "targetPath": "string_value"
}'
```

> For complex input (multi-line strings, code), save the JSON to a file and use `-d @args.json`.
>
> Or pipe via stdin:
> ```bash
> curl -X POST http://localhost:23630/api/tools/phantomcamera-set-look-at -H "Content-Type: application/json" -d @- <<'EOF'
> {"param": "value"}
> EOF
> ```

#### With Authorization (if required)

```bash
curl -X POST http://localhost:23630/api/tools/phantomcamera-set-look-at \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer YOUR_TOKEN" \
  -d '{
  "nodePath": "string_value",
  "lookAtMode": "string_value",
  "targetPath": "string_value"
}'
```

## Input

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `nodePath` | `string` | Yes | Node path (relative to the edited scene root) of the PhantomCamera3D to modify. |
| `lookAtMode` | `string` | No | Look-at mode: None, Mimic, Simple, or Group (case-insensitive). When omitted, the mode is left unchanged. |
| `targetPath` | `string` | No | Node path (relative to the edited scene root) of the look-at target (a Node3D). When omitted, the target is left unchanged. |

### Input JSON Schema

```json
{
  "type": "object",
  "properties": {
    "nodePath": {
      "type": "string",
      "description": "Node path (relative to the edited scene root) of the PhantomCamera3D to modify."
    },
    "lookAtMode": {
      "type": "string",
      "description": "Look-at mode: None, Mimic, Simple, or Group (case-insensitive). When omitted, the mode is left unchanged."
    },
    "targetPath": {
      "type": "string",
      "description": "Node path (relative to the edited scene root) of the look-at target (a Node3D). When omitted, the target is left unchanged."
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

