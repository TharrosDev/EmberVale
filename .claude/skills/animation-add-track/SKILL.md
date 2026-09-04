---
name: animation-add-track
description: "Add a track to an existing Animation on an AnimationPlayer (addressed by 'nodePath', relative to the edited scene root; 'animationName' is the qualified clip name). 'trackType' is 'value', 'position-3d', 'rotation-3d' or 'scale-3d'. 'targetPath' is the animated node's path (relative to the player's root node). For a 'value' track, also pass 'property' (the animated sub-property, e.g. 'modulate' or 'position:x') — the track path becomes 'targetPath:property'. Returns the animation's updated config (including the new track's index)."
---

# Animation / Add Track

Add a track to an existing Animation on an AnimationPlayer (addressed by 'nodePath', relative to the edited scene root; 'animationName' is the qualified clip name). 'trackType' is 'value', 'position-3d', 'rotation-3d' or 'scale-3d'. 'targetPath' is the animated node's path (relative to the player's root node). For a 'value' track, also pass 'property' (the animated sub-property, e.g. 'modulate' or 'position:x') — the track path becomes 'targetPath:property'. Returns the animation's updated config (including the new track's index).

## How to Call

### HTTP API (Direct Tool Execution)

Execute this tool directly via the MCP Plugin HTTP API:

```bash
curl -X POST http://localhost:23630/api/tools/animation-add-track \
  -H "Content-Type: application/json" \
  -d '{
  "nodePath": "string_value",
  "animationName": "string_value",
  "trackType": "string_value",
  "targetPath": "string_value",
  "property": "string_value"
}'
```

> For complex input (multi-line strings, code), save the JSON to a file and use `-d @args.json`.
>
> Or pipe via stdin:
> ```bash
> curl -X POST http://localhost:23630/api/tools/animation-add-track -H "Content-Type: application/json" -d @- <<'EOF'
> {"param": "value"}
> EOF
> ```

#### With Authorization (if required)

```bash
curl -X POST http://localhost:23630/api/tools/animation-add-track \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer YOUR_TOKEN" \
  -d '{
  "nodePath": "string_value",
  "animationName": "string_value",
  "trackType": "string_value",
  "targetPath": "string_value",
  "property": "string_value"
}'
```

## Input

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `nodePath` | `string` | Yes | Node path (relative to the edited scene root) of the AnimationPlayer. |
| `animationName` | `string` | Yes | Qualified animation name ('name' for the global library, or 'library/name'). |
| `trackType` | `string` | Yes | Track kind: 'value', 'position-3d', 'rotation-3d' or 'scale-3d'. |
| `targetPath` | `string` | Yes | Path of the node the track animates (relative to the player's root node). |
| `property` | `string` | No | Sub-property for a 'value' track (e.g. 'modulate', 'position:x'); required for 'value' tracks, ignored for the 3D transform tracks. |

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
    "trackType": {
      "type": "string",
      "description": "Track kind: 'value', 'position-3d', 'rotation-3d' or 'scale-3d'."
    },
    "targetPath": {
      "type": "string",
      "description": "Path of the node the track animates (relative to the player's root node)."
    },
    "property": {
      "type": "string",
      "description": "Sub-property for a 'value' track (e.g. 'modulate', 'position:x'); required for 'value' tracks, ignored for the 3D transform tracks."
    }
  },
  "required": [
    "nodePath",
    "animationName",
    "trackType",
    "targetPath"
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

