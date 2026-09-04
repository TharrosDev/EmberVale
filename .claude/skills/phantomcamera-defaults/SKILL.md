---
name: phantomcamera-defaults
description: "Return the recommended starter configuration (priority, follow mode, look-at mode, damping) for a Godot Phantom Camera virtual camera. Pure-managed: touches no scene and does NOT require the Phantom Camera addon to be installed, so it is safe to call any time to discover sane defaults before creating or configuring a real PhantomCamera. 'dimension' accepts '2D' or '3D' (default '3D') and only selects the reported camera class name."
---

# PhantomCamera / Defaults

Return the recommended starter configuration (priority, follow mode, look-at mode, damping) for a Godot Phantom Camera virtual camera. Pure-managed: touches no scene and does NOT require the Phantom Camera addon to be installed, so it is safe to call any time to discover sane defaults before creating or configuring a real PhantomCamera. 'dimension' accepts '2D' or '3D' (default '3D') and only selects the reported camera class name.

## How to Call

### HTTP API (Direct Tool Execution)

Execute this tool directly via the MCP Plugin HTTP API:

```bash
curl -X POST http://localhost:23630/api/tools/phantomcamera-defaults \
  -H "Content-Type: application/json" \
  -d '{
  "dimension": "string_value"
}'
```

> For complex input (multi-line strings, code), save the JSON to a file and use `-d @args.json`.
>
> Or pipe via stdin:
> ```bash
> curl -X POST http://localhost:23630/api/tools/phantomcamera-defaults -H "Content-Type: application/json" -d @- <<'EOF'
> {"param": "value"}
> EOF
> ```

#### With Authorization (if required)

```bash
curl -X POST http://localhost:23630/api/tools/phantomcamera-defaults \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer YOUR_TOKEN" \
  -d '{
  "dimension": "string_value"
}'
```

## Input

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `dimension` | `string` | No | Camera dimension: '2D' (PhantomCamera2D) or '3D' (PhantomCamera3D). Defaults to '3D'. Only affects the reported TypeName; the scalar starter values are identical for both. |

### Input JSON Schema

```json
{
  "type": "object",
  "properties": {
    "dimension": {
      "type": "string",
      "description": "Camera dimension: '2D' (PhantomCamera2D) or '3D' (PhantomCamera3D). Defaults to '3D'. Only affects the reported TypeName; the scalar starter values are identical for both."
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

