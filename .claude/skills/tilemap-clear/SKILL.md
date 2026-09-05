---
name: tilemap-clear
description: Remove all cells from a TileMapLayer (the assigned TileSet is kept). Returns the now-empty layer's config.
---

# Tilemap / Clear

Clear ALL cells on an existing TileMapLayer, addressed by 'nodePath' (relative to the edited scene root). Removes every placed cell; the assigned TileSet is kept. Returns the layer's updated (empty) config.

Erase every cell on a `TileMapLayer`.

## Inputs

- `nodePath` — required node path (relative to the edited scene root) of the TileMapLayer.

## Behavior

Calls `TileMapLayer.Clear()` on the editor main thread (removes all placed cells; the assigned TileSet resource is NOT removed), marks the scene unsaved, and returns the layer's config (now with `UsedCellCount` 0 and an empty cell list).

## How to Call

### HTTP API (Direct Tool Execution)

Execute this tool directly via the MCP Plugin HTTP API:

```bash
curl -X POST http://localhost:23630/api/tools/tilemap-clear \
  -H "Content-Type: application/json" \
  -d '{
  "nodePath": "string_value"
}'
```

> For complex input (multi-line strings, code), save the JSON to a file and use `-d @args.json`.
>
> Or pipe via stdin:
> ```bash
> curl -X POST http://localhost:23630/api/tools/tilemap-clear -H "Content-Type: application/json" -d @- <<'EOF'
> {"param": "value"}
> EOF
> ```

#### With Authorization (if required)

```bash
curl -X POST http://localhost:23630/api/tools/tilemap-clear \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer YOUR_TOKEN" \
  -d '{
  "nodePath": "string_value"
}'
```

## Input

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `nodePath` | `string` | Yes | Node path (relative to the edited scene root) of the TileMapLayer to clear. |

### Input JSON Schema

```json
{
  "type": "object",
  "properties": {
    "nodePath": {
      "type": "string",
      "description": "Node path (relative to the edited scene root) of the TileMapLayer to clear."
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
      "$ref": "#/$defs/com.IvanMurzak.Godot.MCP.Tilemap.TileMapLayerInfo"
    }
  },
  "$defs": {
    "System.Collections.Generic.List(com.IvanMurzak.Godot.MCP.Tilemap.TileCell)": {
      "type": "array",
      "items": {
        "$ref": "#/$defs/com.IvanMurzak.Godot.MCP.Tilemap.TileCell"
      }
    },
    "com.IvanMurzak.Godot.MCP.Tilemap.TileCell": {
      "type": "object",
      "properties": {
        "X": {
          "type": "integer"
        },
        "Y": {
          "type": "integer"
        },
        "SourceId": {
          "type": "integer"
        },
        "AtlasX": {
          "type": "integer"
        },
        "AtlasY": {
          "type": "integer"
        }
      },
      "required": [
        "X",
        "Y",
        "SourceId",
        "AtlasX",
        "AtlasY"
      ]
    },
    "com.IvanMurzak.Godot.MCP.Tilemap.TileMapLayerInfo": {
      "type": "object",
      "properties": {
        "NodePath": {
          "type": "string"
        },
        "TypeName": {
          "type": "string"
        },
        "TileSetPath": {
          "type": "string"
        },
        "UsedCellCount": {
          "type": "integer"
        },
        "UsedCells": {
          "$ref": "#/$defs/System.Collections.Generic.List(com.IvanMurzak.Godot.MCP.Tilemap.TileCell)"
        }
      },
      "required": [
        "UsedCellCount"
      ]
    }
  },
  "required": [
    "result"
  ]
}
```

