---
name: phantomcamera-set-priority
description: "Set the 'priority' of an existing PhantomCamera3D, addressed by 'nodePath' (relative to the edited scene root). The Phantom Camera addon activates the highest-priority camera that has a matching PhantomCameraHost, so raising priority is how you switch the active camera. Returns the camera's updated config, or installed:false when the Phantom Camera addon is absent."
---

# PhantomCamera / Set Priority

Set the 'priority' of an existing PhantomCamera3D, addressed by 'nodePath' (relative to the edited scene root). The Phantom Camera addon activates the highest-priority camera that has a matching PhantomCameraHost, so raising priority is how you switch the active camera. Returns the camera's updated config, or installed:false when the Phantom Camera addon is absent.

## How to Call

### HTTP API (Direct Tool Execution)

Execute this tool directly via the MCP Plugin HTTP API:

```bash
curl -X POST http://localhost:23630/api/tools/phantomcamera-set-priority \
  -H "Content-Type: application/json" \
  -d '{
  "nodePath": "string_value",
  "priority": 0
}'
```

> For complex input (multi-line strings, code), save the JSON to a file and use `-d @args.json`.
>
> Or pipe via stdin:
> ```bash
> curl -X POST http://localhost:23630/api/tools/phantomcamera-set-priority -H "Content-Type: application/json" -d @- <<'EOF'
> {"param": "value"}
> EOF
> ```

#### With Authorization (if required)

```bash
curl -X POST http://localhost:23630/api/tools/phantomcamera-set-priority \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer YOUR_TOKEN" \
  -d '{
  "nodePath": "string_value",
  "priority": 0
}'
```

## Input

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `nodePath` | `string` | Yes | Node path (relative to the edited scene root) of the PhantomCamera3D to modify. |
| `priority` | `integer` | Yes | New priority value. Higher wins when multiple PhantomCameras are active. |

### Input JSON Schema

```json
{
  "type": "object",
  "properties": {
    "nodePath": {
      "type": "string",
      "description": "Node path (relative to the edited scene root) of the PhantomCamera3D to modify."
    },
    "priority": {
      "type": "integer",
      "description": "New priority value. Higher wins when multiple PhantomCameras are active."
    }
  },
  "required": [
    "nodePath",
    "priority"
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

