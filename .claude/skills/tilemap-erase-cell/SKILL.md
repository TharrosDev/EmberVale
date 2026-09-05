---
name: tilemap-erase-cell
description: Erase one cell on a TileMapLayer (set it back to empty), addressed by map coords (x,y). Returns the layer's updated config.
---

# Tilemap / Erase Cell

Erase a single cell on an existing TileMapLayer, addressed by 'nodePath' (relative to the edited scene root). The cell at ('x','y') is set back to empty. Returns the layer's updated config.

Clear a single cell of a `TileMapLayer`.

## Inputs

- `nodePath` — required node path (relative to the edited scene root) of the TileMapLayer.
- `x`, `y` — required cell coordinates on the map grid.

## Behavior

Calls `TileMapLayer.EraseCell(coords)` on the editor main thread (equivalent to setting the cell's source to -1), marks the scene unsaved, and returns the layer's updated config. Erasing an already-empty cell is a no-op.

## How to Call

### HTTP API (Direct Tool Execution)

Execute this tool directly via the MCP Plugin HTTP API:

```bash
curl -X POST http://localhost:23630/api/tools/tilemap-erase-cell \
  -H "Content-Type: application/json" \
  -d '{
  "nodePath": "string_value",
  "x": 0,
  "y": 0
}'
```

> For complex input (multi-line strings, code), save the JSON to a file and use `-d @args.json`.
>
> Or pipe via stdin:
> ```bash
> curl -X POST http://localhost:23630/api/tools/tilemap-erase-cell -H "Content-Type: application/json" -d @- <<'EOF'
> {"param": "value"}
> EOF
> ```

#### With Authorization (if required)

```bash
curl -X POST http://localhost:23630/api/tools/tilemap-erase-cell \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer YOUR_TOKEN" \
  -d '{
  "nodePath": "string_value",
  "x": 0,
  "y": 0
}'
```

## Input

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `nodePath` | `string` | Yes | Node path (relative to the edited scene root) of the TileMapLayer. |
| `x` | `integer` | Yes | Cell X coordinate on the map grid. |
| `y` | `integer` | Yes | Cell Y coordinate on the map grid. |

### Input JSON Schema

```json
{
  "type": "object",
  "properties": {
    "nodePath": {
      "type": "string",
      "description": "Node path (relative to the edited scene root) of the TileMapLayer."
    },
    "x": {
      "type": "integer",
      "description": "Cell X coordinate on the map grid."
    },
    "y": {
      "type": "integer",
      "description": "Cell Y coordinate on the map grid."
    }
  },
  "required": [
    "nodePath",
    "x",
    "y"
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

