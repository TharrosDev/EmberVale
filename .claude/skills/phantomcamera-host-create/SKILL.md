---
name: phantomcamera-host-create
description: "Ensure a PhantomCameraHost exists under a Camera3D in the currently edited Godot scene (required by the Phantom Camera addon — without a host, PhantomCameras do nothing). Pass 'cameraPath' (a node path relative to the scene root) to target a specific Camera3D; when omitted the first Camera3D in the scene is used, or a new one is created under the scene root. Returns the host's path and the camera it was attached to, or a structured installed:false result when the Phantom Camera addon is not installed."
---

# PhantomCamera / Host Create

Ensure a PhantomCameraHost exists under a Camera3D in the currently edited Godot scene (required by the Phantom Camera addon — without a host, PhantomCameras do nothing). Pass 'cameraPath' (a node path relative to the scene root) to target a specific Camera3D; when omitted the first Camera3D in the scene is used, or a new one is created under the scene root. Returns the host's path and the camera it was attached to, or a structured installed:false result when the Phantom Camera addon is not installed.

## How to Call

### HTTP API (Direct Tool Execution)

Execute this tool directly via the MCP Plugin HTTP API:

```bash
curl -X POST http://localhost:23630/api/tools/phantomcamera-host-create \
  -H "Content-Type: application/json" \
  -d '{
  "cameraPath": "string_value",
  "cameraName": "string_value"
}'
```

> For complex input (multi-line strings, code), save the JSON to a file and use `-d @args.json`.
>
> Or pipe via stdin:
> ```bash
> curl -X POST http://localhost:23630/api/tools/phantomcamera-host-create -H "Content-Type: application/json" -d @- <<'EOF'
> {"param": "value"}
> EOF
> ```

#### With Authorization (if required)

```bash
curl -X POST http://localhost:23630/api/tools/phantomcamera-host-create \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer YOUR_TOKEN" \
  -d '{
  "cameraPath": "string_value",
  "cameraName": "string_value"
}'
```

## Input

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `cameraPath` | `string` | No | Optional node path (relative to the edited scene root) of the Camera3D to attach the host to. When omitted, the first Camera3D in the scene is used, or one is created. |
| `cameraName` | `string` | No | Optional name for the Camera3D when one must be created. Defaults to 'Camera3D'. |

### Input JSON Schema

```json
{
  "type": "object",
  "properties": {
    "cameraPath": {
      "type": "string",
      "description": "Optional node path (relative to the edited scene root) of the Camera3D to attach the host to. When omitted, the first Camera3D in the scene is used, or one is created."
    },
    "cameraName": {
      "type": "string",
      "description": "Optional name for the Camera3D when one must be created. Defaults to 'Camera3D'."
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

