---
name: animation-insert-key
description: Insert a keyframe on a track of an existing Animation (player addressed by 'nodePath' relative to the edited scene root; 'animationName' is the qualified clip name; 'trackIndex' is the 0-based track). 'time' is the keyframe time in seconds (clamped to >= 0). For a 'value' track, 'value' is the keyed scalar. For a 3D transform track (position/rotation/scale), 'x'/'y'/'z' are the keyed components (rotation is in Euler degrees; scale defaults to 1 when a component is omitted). Returns the animation's updated config.
---

# Animation / Insert Key

Insert a keyframe on a track of an existing Animation (player addressed by 'nodePath' relative to the edited scene root; 'animationName' is the qualified clip name; 'trackIndex' is the 0-based track). 'time' is the keyframe time in seconds (clamped to >= 0). For a 'value' track, 'value' is the keyed scalar. For a 3D transform track (position/rotation/scale), 'x'/'y'/'z' are the keyed components (rotation is in Euler degrees; scale defaults to 1 when a component is omitted). Returns the animation's updated config.

## How to Call

### HTTP API (Direct Tool Execution)

Execute this tool directly via the MCP Plugin HTTP API:

```bash
curl -X POST http://localhost:23630/api/tools/animation-insert-key \
  -H "Content-Type: application/json" \
  -d '{
  "nodePath": "string_value",
  "animationName": "string_value",
  "trackIndex": 0,
  "time": 0,
  "value": 0,
  "x": "string_value",
  "y": "string_value",
  "z": "string_value"
}'
```

> For complex input (multi-line strings, code), save the JSON to a file and use `-d @args.json`.
>
> Or pipe via stdin:
> ```bash
> curl -X POST http://localhost:23630/api/tools/animation-insert-key -H "Content-Type: application/json" -d @- <<'EOF'
> {"param": "value"}
> EOF
> ```

#### With Authorization (if required)

```bash
curl -X POST http://localhost:23630/api/tools/animation-insert-key \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer YOUR_TOKEN" \
  -d '{
  "nodePath": "string_value",
  "animationName": "string_value",
  "trackIndex": 0,
  "time": 0,
  "value": 0,
  "x": "string_value",
  "y": "string_value",
  "z": "string_value"
}'
```

## Input

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `nodePath` | `string` | Yes | Node path (relative to the edited scene root) of the AnimationPlayer. |
| `animationName` | `string` | Yes | Qualified animation name ('name' for the global library, or 'library/name'). |
| `trackIndex` | `integer` | Yes | 0-based index of the track to key (from 'animation-add-track'). |
| `time` | `number` | No | Keyframe time in seconds (Godot key time); clamped to >= 0. Defaults to 0. |
| `value` | `number` | No | Keyed scalar value for a 'value' track (ignored for 3D transform tracks). Defaults to 0. |
| `x` | `any` | No | X component for a 3D transform track (Euler degrees for rotation). Defaults to 0/1. |
| `y` | `any` | No | Y component for a 3D transform track (Euler degrees for rotation). Defaults to 0/1. |
| `z` | `any` | No | Z component for a 3D transform track (Euler degrees for rotation). Defaults to 0/1. |

### Input JSON Schema

```json
{
  "type": "object",
  "properties": {
    "nodePath": {
      "type": "string",
      "description": "Node path (relative to the edited scene root) of the AnimationPlayer."
    },
    "animationName": {
      "type": "string",
      "description": "Qualified animation name ('name' for the global library, or 'library/name')."
    },
    "trackIndex": {
      "type": "integer",
      "description": "0-based index of the track to key (from 'animation-add-track')."
    },
    "time": {
      "type": "number",
      "description": "Keyframe time in seconds (Godot key time); clamped to >= 0. Defaults to 0."
    },
    "value": {
      "type": "number",
      "description": "Keyed scalar value for a 'value' track (ignored for 3D transform tracks). Defaults to 0."
    },
    "x": {
      "$ref": "#/$defs/System.Double",
      "description": "X component for a 3D transform track (Euler degrees for rotation). Defaults to 0/1."
    },
    "y": {
      "$ref": "#/$defs/System.Double",
      "description": "Y component for a 3D transform track (Euler degrees for rotation). Defaults to 0/1."
    },
    "z": {
      "$ref": "#/$defs/System.Double",
      "description": "Z component for a 3D transform track (Euler degrees for rotation). Defaults to 0/1."
    }
  },
  "$defs": {
    "System.Double": {
      "type": "number"
    }
  },
  "required": [
    "nodePath",
    "animationName",
    "trackIndex"
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
      "$ref": "#/$defs/com.IvanMurzak.Godot.MCP.Animation.AnimationClipInfo"
    }
  },
  "$defs": {
    "System.Collections.Generic.List(com.IvanMurzak.Godot.MCP.Animation.AnimationTrackInfo)": {
      "type": "array",
      "items": {
        "$ref": "#/$defs/com.IvanMurzak.Godot.MCP.Animation.AnimationTrackInfo"
      }
    },
    "com.IvanMurzak.Godot.MCP.Animation.AnimationTrackInfo": {
      "type": "object",
      "properties": {
        "Index": {
          "type": "integer"
        },
        "Kind": {
          "type": "string"
        },
        "Path": {
          "type": "string"
        },
        "KeyCount": {
          "type": "integer"
        }
      },
      "required": [
        "Index",
        "KeyCount"
      ]
    },
    "com.IvanMurzak.Godot.MCP.Animation.AnimationClipInfo": {
      "type": "object",
      "properties": {
        "Name": {
          "type": "string"
        },
        "Library": {
          "type": "string"
        },
        "QualifiedName": {
          "type": "string"
        },
        "Length": {
          "type": "number"
        },
        "LoopMode": {
          "type": "string"
        },
        "TrackCount": {
          "type": "integer"
        },
        "Tracks": {
          "$ref": "#/$defs/System.Collections.Generic.List(com.IvanMurzak.Godot.MCP.Animation.AnimationTrackInfo)"
        }
      },
      "required": [
        "Length",
        "TrackCount"
      ]
    }
  },
  "required": [
    "result"
  ]
}
```

