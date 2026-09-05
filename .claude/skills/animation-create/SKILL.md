---
name: animation-create
description: Create a new Animation in an AnimationPlayer (addressed by 'nodePath', relative to the edited scene root) and return its config. 'name' is the animation name. Optional 'libraryName' selects/creates the owning library (default '' = the global library; the clip is then addressed as 'name', or 'libraryName/name' for a named library). Optional 'length' (seconds, clamped > 0, default 1.0) and 'loopMode' ('none'/'linear'/'pingpong', default 'none').
---

# Animation / Create

Create a new Animation in an AnimationPlayer (addressed by 'nodePath', relative to the edited scene root) and return its config. 'name' is the animation name. Optional 'libraryName' selects/creates the owning library (default '' = the global library; the clip is then addressed as 'name', or 'libraryName/name' for a named library). Optional 'length' (seconds, clamped > 0, default 1.0) and 'loopMode' ('none'/'linear'/'pingpong', default 'none').

## How to Call

### HTTP API (Direct Tool Execution)

Execute this tool directly via the MCP Plugin HTTP API:

```bash
curl -X POST http://localhost:23630/api/tools/animation-create \
  -H "Content-Type: application/json" \
  -d '{
  "nodePath": "string_value",
  "name": "string_value",
  "libraryName": "string_value",
  "length": "string_value",
  "loopMode": "string_value"
}'
```

> For complex input (multi-line strings, code), save the JSON to a file and use `-d @args.json`.
>
> Or pipe via stdin:
> ```bash
> curl -X POST http://localhost:23630/api/tools/animation-create -H "Content-Type: application/json" -d @- <<'EOF'
> {"param": "value"}
> EOF
> ```

#### With Authorization (if required)

```bash
curl -X POST http://localhost:23630/api/tools/animation-create \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer YOUR_TOKEN" \
  -d '{
  "nodePath": "string_value",
  "name": "string_value",
  "libraryName": "string_value",
  "length": "string_value",
  "loopMode": "string_value"
}'
```

## Input

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `nodePath` | `string` | Yes | Node path (relative to the edited scene root) of the AnimationPlayer. |
| `name` | `string` | Yes | Name for the new animation (within its library). |
| `libraryName` | `string` | No | Owning library name. Defaults to '' (the global/default library); auto-created if missing. |
| `length` | `any` | No | Animation length in seconds (Godot 'length'); clamped to > 0. Defaults to 1.0. |
| `loopMode` | `string` | No | Loop mode: 'none', 'linear' or 'pingpong'. Defaults to 'none'. |

### Input JSON Schema

```json
{
  "type": "object",
  "properties": {
    "nodePath": {
      "type": "string",
      "description": "Node path (relative to the edited scene root) of the AnimationPlayer."
    },
    "name": {
      "type": "string",
      "description": "Name for the new animation (within its library)."
    },
    "libraryName": {
      "type": "string",
      "description": "Owning library name. Defaults to '' (the global/default library); auto-created if missing."
    },
    "length": {
      "$ref": "#/$defs/System.Double",
      "description": "Animation length in seconds (Godot 'length'); clamped to > 0. Defaults to 1.0."
    },
    "loopMode": {
      "type": "string",
      "description": "Loop mode: 'none', 'linear' or 'pingpong'. Defaults to 'none'."
    }
  },
  "$defs": {
    "System.Double": {
      "type": "number"
    }
  },
  "required": [
    "nodePath",
    "name"
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

