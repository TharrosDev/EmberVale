---
name: tilemap-create
description: Create a Godot TileMapLayer node (4.3+, the modern replacement for TileMap) in the currently edited scene, optionally renamed and parented to a chosen node. Returns the new layer's structured config.
---

# Tilemap / Create

Create a TileMapLayer node in the currently edited Godot scene and return its structured config. Optionally pass 'name' to rename it and 'parentPath' (a node path relative to the scene root) to parent it (defaults to the scene root). The new node's owner is set to the scene root so it is saved with the scene.

Create a `TileMapLayer` node in the edited scene and return its structured config.

## Inputs

- `name` — optional node name; when omitted Godot's default name for the type is used.
- `parentPath` — optional node path (relative to the edited scene root) to parent the new layer under; when omitted the layer is parented to the scene root.

## Behavior

Adds a new `TileMapLayer` under the resolved parent, sets its owner to the scene root (so it is saved with the scene), marks the scene unsaved, selects it in the editor, and returns its config (node path, type name, assigned TileSet path, used-cell count). The layer starts with no TileSet and no cells — call `tilemap-set-tileset` then `tilemap-set-cell` to populate it.

## How to Call

### HTTP API (Direct Tool Execution)

Execute this tool directly via the MCP Plugin HTTP API:

```bash
curl -X POST http://localhost:23630/api/tools/tilemap-create \
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
> curl -X POST http://localhost:23630/api/tools/tilemap-create -H "Content-Type: application/json" -d @- <<'EOF'
> {"param": "value"}
> EOF
> ```

#### With Authorization (if required)

```bash
curl -X POST http://localhost:23630/api/tools/tilemap-create \
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
| `name` | `string` | No | Name for the new TileMapLayer node. When omitted, Godot's default name is used. |
| `parentPath` | `string` | No | Node path (relative to the edited scene root) of the parent. When omitted, the node is parented to the scene root. |

### Input JSON Schema

```json
{
  "type": "object",
  "properties": {
    "name": {
      "type": "string",
      "description": "Name for the new TileMapLayer node. When omitted, Godot's default name is used."
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

