---
name: animation-get
description: "Read an AnimationPlayer's config — its libraries and the qualified names of all its animations — addressed by 'nodePath' (relative to the edited scene root). When 'animationName' (a qualified clip name) is supplied, also returns that animation's full config (length, loop mode, tracks) under 'SelectedAnimation'. Read-only: does not modify the scene."
---

# Animation / Get

Read an AnimationPlayer's config — its libraries and the qualified names of all its animations — addressed by 'nodePath' (relative to the edited scene root). When 'animationName' (a qualified clip name) is supplied, also returns that animation's full config (length, loop mode, tracks) under 'SelectedAnimation'. Read-only: does not modify the scene.

## How to Call

### HTTP API (Direct Tool Execution)

Execute this tool directly via the MCP Plugin HTTP API:

```bash
curl -X POST http://localhost:23630/api/tools/animation-get \
  -H "Content-Type: application/json" \
  -d '{
  "nodePath": "string_value",
  "animationName": "string_value"
}'
```

> For complex input (multi-line strings, code), save the JSON to a file and use `-d @args.json`.
>
> Or pipe via stdin:
> ```bash
> curl -X POST http://localhost:23630/api/tools/animation-get -H "Content-Type: application/json" -d @- <<'EOF'
> {"param": "value"}
> EOF
> ```

#### With Authorization (if required)

```bash
curl -X POST http://localhost:23630/api/tools/animation-get \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer YOUR_TOKEN" \
  -d '{
  "nodePath": "string_value",
  "animationName": "string_value"
}'
```

## Input

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `nodePath` | `string` | Yes | Node path (relative to the edited scene root) of the AnimationPlayer. |
| `animationName` | `string` | No | Optional qualified animation name to read in detail ('name' or 'library/name'). When omitted, only the player's library + animation list is returned. |

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
      "description": "Optional qualified animation name to read in detail ('name' or 'library/name'). When omitted, only the player's library + animation list is returned."
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
      "$ref": "#/$defs/com.IvanMurzak.Godot.MCP.Animation.AnimationPlayerInfo"
    }
  },
  "$defs": {
    "System.Collections.Generic.List(System.String)": {
      "type": "array",
      "items": {
        "type": "string"
      }
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
    },
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
    "com.IvanMurzak.Godot.MCP.Animation.AnimationPlayerInfo": {
      "type": "object",
      "properties": {
        "NodePath": {
          "type": "string"
        },
        "TypeName": {
          "type": "string"
        },
        "Libraries": {
          "$ref": "#/$defs/System.Collections.Generic.List(System.String)"
        },
        "LibraryCount": {
          "type": "integer"
        },
        "Animations": {
          "$ref": "#/$defs/System.Collections.Generic.List(System.String)"
        },
        "AnimationCount": {
          "type": "integer"
        },
        "SelectedAnimation": {
          "$ref": "#/$defs/com.IvanMurzak.Godot.MCP.Animation.AnimationClipInfo"
        }
      },
      "required": [
        "LibraryCount",
        "AnimationCount"
      ]
    }
  },
  "required": [
    "result"
  ]
}
```

