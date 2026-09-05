---
name: animation-player-create
description: Create an AnimationPlayer node in the currently edited Godot scene and return its structured config (node path, libraries, animation list). Optionally pass 'parentPath' (a node path relative to the scene root) to parent it (defaults to the scene root) and 'name' to rename it. The new node's owner is set to the scene root so it is saved with the scene.
---

# Animation / Player Create

Create an AnimationPlayer node in the currently edited Godot scene and return its structured config (node path, libraries, animation list). Optionally pass 'parentPath' (a node path relative to the scene root) to parent it (defaults to the scene root) and 'name' to rename it. The new node's owner is set to the scene root so it is saved with the scene.

## How to Call

### HTTP API (Direct Tool Execution)

Execute this tool directly via the MCP Plugin HTTP API:

```bash
curl -X POST http://localhost:23630/api/tools/animation-player-create \
  -H "Content-Type: application/json" \
  -d '{
  "name": "string_value",
  "parentPath": "string_value"
}'
```

> For complex input (multi-line strings, code), save the JSON to a file and use `-d @args.json`.
>
> Or pipe via stdin:
> ```bash
> curl -X POST http://localhost:23630/api/tools/animation-player-create -H "Content-Type: application/json" -d @- <<'EOF'
> {"param": "value"}
> EOF
> ```

#### With Authorization (if required)

```bash
curl -X POST http://localhost:23630/api/tools/animation-player-create \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer YOUR_TOKEN" \
  -d '{
  "name": "string_value",
  "parentPath": "string_value"
}'
```

## Input

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `name` | `string` | No | Name for the new AnimationPlayer node. When omitted, Godot's default name is used. |
| `parentPath` | `string` | No | Node path (relative to the edited scene root) of the parent. When omitted, the node is parented to the scene root. |

### Input JSON Schema

```json
{
  "type": "object",
  "properties": {
    "name": {
      "type": "string",
      "description": "Name for the new AnimationPlayer node. When omitted, Godot's default name is used."
    },
    "parentPath": {
      "type": "string",
      "description": "Node path (relative to the edited scene root) of the parent. When omitted, the node is parented to the scene root."
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

