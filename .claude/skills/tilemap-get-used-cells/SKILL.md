---
name: tilemap-get-used-cells
description: List the used (non-empty) cells of a TileMapLayer — each cell's map coords, TileSet source id, and atlas coords. Read-only.
---

# Tilemap / Get Used Cells

List the used (non-empty) cells of an existing TileMapLayer, addressed by 'nodePath' (relative to the edited scene root). Returns the layer's config including each used cell's map coordinates, TileSet source id, and atlas coordinates. Read-only: does not modify the scene.

Read the used cells of a `TileMapLayer`.

## Inputs

- `nodePath` — required node path (relative to the edited scene root) of the TileMapLayer.

## Behavior

Calls `TileMapLayer.GetUsedCells()` on the editor main thread and returns the layer's config with every used cell expanded (map coords + source id + atlas coords) and a `UsedCellCount`. Read-only: does not modify the scene.

## How to Call

### HTTP API (Direct Tool Execution)

Execute this tool directly via the MCP Plugin HTTP API:

```bash
curl -X POST http://localhost:23630/api/tools/tilemap-get-used-cells \
  -H "Content-Type: application/json" \
  -d '{
  "nodePath": "string_value"
}'
```

> For complex input (multi-line strings, code), save the JSON to a file and use `-d @args.json`.
>
> Or pipe via stdin:
> ```bash
> curl -X POST http://localhost:23630/api/tools/tilemap-get-used-cells -H "Content-Type: application/json" -d @- <<'EOF'
> {"param": "value"}
> EOF
> ```

#### With Authorization (if required)

```bash
curl -X POST http://localhost:23630/api/tools/tilemap-get-used-cells \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer YOUR_TOKEN" \
  -d '{
  "nodePath": "string_value"
}'
```

## Input

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `nodePath` | `string` | Yes | Node path (relative to the edited scene root) of the TileMapLayer to read. |

### Input JSON Schema

```json
{
  "type": "object",
  "properties": {
    "nodePath": {
      "type": "string",
      "description": "Node path (relative to the edited scene root) of the TileMapLayer to read."
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

