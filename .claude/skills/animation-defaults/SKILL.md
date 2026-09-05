---
name: animation-defaults
description: "Return the recommended starter configuration (length, loop mode, no tracks) for a new Godot Animation resource. Pure-managed: touches no scene, so it is safe to call any time to discover sane defaults before creating a real animation with 'animation-create'. 'loopMode' accepts 'none', 'linear' or 'pingpong' (default 'none')."
---

# Animation / Defaults

Return the recommended starter configuration (length, loop mode, no tracks) for a new Godot Animation resource. Pure-managed: touches no scene, so it is safe to call any time to discover sane defaults before creating a real animation with 'animation-create'. 'loopMode' accepts 'none', 'linear' or 'pingpong' (default 'none').

## How to Call

### HTTP API (Direct Tool Execution)

Execute this tool directly via the MCP Plugin HTTP API:

```bash
curl -X POST http://localhost:23630/api/tools/animation-defaults \
  -H "Content-Type: application/json" \
  -d '{
  "loopMode": "string_value"
}'
```

> For complex input (multi-line strings, code), save the JSON to a file and use `-d @args.json`.
>
> Or pipe via stdin:
> ```bash
> curl -X POST http://localhost:23630/api/tools/animation-defaults -H "Content-Type: application/json" -d @- <<'EOF'
> {"param": "value"}
> EOF
> ```

#### With Authorization (if required)

```bash
curl -X POST http://localhost:23630/api/tools/animation-defaults \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer YOUR_TOKEN" \
  -d '{
  "loopMode": "string_value"
}'
```

## Input

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `loopMode` | `string` | No | Loop mode for the starter config: 'none', 'linear' or 'pingpong'. Defaults to 'none'. |

### Input JSON Schema

```json
{
  "type": "object",
  "properties": {
    "loopMode": {
      "type": "string",
      "description": "Loop mode for the starter config: 'none', 'linear' or 'pingpong'. Defaults to 'none'."
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

