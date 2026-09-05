---
name: animation-library-add
description: Add a new empty AnimationLibrary to an existing AnimationPlayer, addressed by 'nodePath' (relative to the edited scene root). 'libraryName' is the (non-empty) library name; animations added to it are addressed as 'libraryName/animationName'. Fails if a library with that name already exists. Returns the player's updated config.
---

# Animation / Library Add

Add a new empty AnimationLibrary to an existing AnimationPlayer, addressed by 'nodePath' (relative to the edited scene root). 'libraryName' is the (non-empty) library name; animations added to it are addressed as 'libraryName/animationName'. Fails if a library with that name already exists. Returns the player's updated config.

## How to Call

### HTTP API (Direct Tool Execution)

Execute this tool directly via the MCP Plugin HTTP API:

```bash
curl -X POST http://localhost:23630/api/tools/animation-library-add \
  -H "Content-Type: application/json" \
  -d '{
  "nodePath": "string_value",
  "libraryName": "string_value"
}'
```

> For complex input (multi-line strings, code), save the JSON to a file and use `-d @args.json`.
>
> Or pipe via stdin:
> ```bash
> curl -X POST http://localhost:23630/api/tools/animation-library-add -H "Content-Type: application/json" -d @- <<'EOF'
> {"param": "value"}
> EOF
> ```

#### With Authorization (if required)

```bash
curl -X POST http://localhost:23630/api/tools/animation-library-add \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer YOUR_TOKEN" \
  -d '{
  "nodePath": "string_value",
  "libraryName": "string_value"
}'
```

## Input

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `nodePath` | `string` | Yes | Node path (relative to the edited scene root) of the AnimationPlayer. |
| `libraryName` | `string` | Yes | Name for the new animation library (must be non-empty and not already present). |

### Input JSON Schema

```json
{
  "type": "object",
  "properties": {
    "nodePath": {
      "type": "string",
      "description": "Node path (relative to the edited scene root) of the AnimationPlayer."
    },
    "libraryName": {
      "type": "string",
      "description": "Name for the new animation library (must be non-empty and not already present)."
    }
  },
  "required": [
    "nodePath",
    "libraryName"
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

