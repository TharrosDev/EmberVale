---
name: tilemap-set-tileset
description: "Assign a TileSet resource (by res:// path) to an existing TileMapLayer so its cells can render. Returns the layer's updated config."
---

# Tilemap / Set TileSet

Assign a TileSet resource to an existing TileMapLayer, addressed by 'nodePath' (relative to the edited scene root). 'tileSetPath' is a Godot resource path (res://…) to a .tres TileSet. Returns the layer's updated config.

Load a `TileSet` resource and assign it to a `TileMapLayer`.

## Inputs

- `nodePath` — required node path (relative to the edited scene root) of the TileMapLayer.
- `tileSetPath` — required `res://` path to a `.tres` TileSet resource.

## Behavior

Validates that `tileSetPath` is a `res://` path, loads it as a `TileSet`, assigns it to the layer, marks the scene unsaved, and returns the layer's updated config. Errors clearly when the path is not a resource path or does not resolve to a TileSet.

## How to Call

### HTTP API (Direct Tool Execution)

Execute this tool directly via the MCP Plugin HTTP API:

```bash
curl -X POST http://localhost:23630/api/tools/tilemap-set-tileset \
  -H "Content-Type: application/json" \
  -d '{
  "nodePath": "string_value",
  "tileSetPath": "string_value"
}'
```

> For complex input (multi-line strings, code), save the JSON to a file and use `-d @args.json`.
>
> Or pipe via stdin:
> ```bash
> curl -X POST http://localhost:23630/api/tools/tilemap-set-tileset -H "Content-Type: application/json" -d @- <<'EOF'
> {"param": "value"}
> EOF
> ```

#### With Authorization (if required)

```bash
curl -X POST http://localhost:23630/api/tools/tilemap-set-tileset \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer YOUR_TOKEN" \
  -d '{
  "nodePath": "string_value",
  "tileSetPath": "string_value"
}'
```

## Input

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `nodePath` | `string` | Yes | Node path (relative to the edited scene root) of the TileMapLayer to configure. |
| `tileSetPath` | `string` | Yes | Godot resource path (res://…) to the TileSet (.tres) resource to assign. |

### Input JSON Schema

```json
{
  "type": "object",
  "properties": {
    "nodePath": {
      "type": "string",
      "description": "Node path (relative to the edited scene root) of the TileMapLayer to configure."
    },
    "tileSetPath": {
      "type": "string",
      "description": "Godot resource path (res://…) to the TileSet (.tres) resource to assign."
    }
  },
  "required": [
    "nodePath",
    "tileSetPath"
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

