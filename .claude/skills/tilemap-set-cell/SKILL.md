---
name: tilemap-set-cell
description: Set one cell on a TileMapLayer to a tile, addressed by map coords (x,y) and the TileSet source id + atlas coords. Returns the layer's updated config.
---

# Tilemap / Set Cell

Set a single cell on an existing TileMapLayer, addressed by 'nodePath' (relative to the edited scene root). The cell at ('x','y') is set to the tile at atlas ('atlasX','atlasY') of TileSet source 'sourceId' (sourceId >= 0, atlas coords >= 0). Returns the layer's updated config.

Place a tile in a single cell of a `TileMapLayer`.

## Inputs

- `nodePath` — required node path (relative to the edited scene root) of the TileMapLayer.
- `x`, `y` — required cell coordinates on the map grid.
- `sourceId` — required TileSet source id (>= 0; the source must exist in the assigned TileSet to render).
- `atlasX`, `atlasY` — required atlas coordinates of the tile within that source (>= 0).

## Behavior

Validates the source id (>= 0) and atlas coords (>= 0), then calls `TileMapLayer.SetCell(coords, sourceId, atlasCoords)` on the editor main thread, marks the scene unsaved, and returns the layer's updated config (including its used cells). Assign a TileSet first with `tilemap-set-tileset`.

## How to Call

### HTTP API (Direct Tool Execution)

Execute this tool directly via the MCP Plugin HTTP API:

```bash
curl -X POST http://localhost:23630/api/tools/tilemap-set-cell \
  -H "Content-Type: application/json" \
  -d '{
  "nodePath": "string_value",
  "x": 0,
  "y": 0,
  "sourceId": 0,
  "atlasX": 0,
  "atlasY": 0
}'
```

> For complex input (multi-line strings, code), save the JSON to a file and use `-d @args.json`.
>
> Or pipe via stdin:
> ```bash
> curl -X POST http://localhost:23630/api/tools/tilemap-set-cell -H "Content-Type: application/json" -d @- <<'EOF'
> {"param": "value"}
> EOF
> ```

#### With Authorization (if required)

```bash
curl -X POST http://localhost:23630/api/tools/tilemap-set-cell \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer YOUR_TOKEN" \
  -d '{
  "nodePath": "string_value",
  "x": 0,
  "y": 0,
  "sourceId": 0,
  "atlasX": 0,
  "atlasY": 0
}'
```

## Input

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `nodePath` | `string` | Yes | Node path (relative to the edited scene root) of the TileMapLayer. |
| `x` | `integer` | Yes | Cell X coordinate on the map grid. |
| `y` | `integer` | Yes | Cell Y coordinate on the map grid. |
| `sourceId` | `integer` | Yes | TileSet source id of the tile to place (>= 0). |
| `atlasX` | `integer` | Yes | Atlas X coordinate of the tile within its source (>= 0). |
| `atlasY` | `integer` | Yes | Atlas Y coordinate of the tile within its source (>= 0). |

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
    },
    "sourceId": {
      "type": "integer",
      "description": "TileSet source id of the tile to place (>= 0)."
    },
    "atlasX": {
      "type": "integer",
      "description": "Atlas X coordinate of the tile within its source (>= 0)."
    },
    "atlasY": {
      "type": "integer",
      "description": "Atlas Y coordinate of the tile within its source (>= 0)."
    }
  },
  "required": [
    "nodePath",
    "x",
    "y",
    "sourceId",
    "atlasX",
    "atlasY"
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

