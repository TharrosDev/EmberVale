---
name: gridmap-set-cell
description: Set a single cell of a GridMap (addressed by 'nodePath', relative to the edited scene root) at grid coordinates ('cellX','cellY','cellZ') to MeshLibrary item 'item' (clamped to >= 0). Optionally pass 'orientation', an orthogonal orientation index 0..23 (clamped). To clear a cell instead, use gridmap-clear-cell. Returns the resulting cell (coordinates, item, orientation).
---

# GridMap / Set Cell

Set a single cell of a GridMap (addressed by 'nodePath', relative to the edited scene root) at grid coordinates ('cellX','cellY','cellZ') to MeshLibrary item 'item' (clamped to >= 0). Optionally pass 'orientation', an orthogonal orientation index 0..23 (clamped). To clear a cell instead, use gridmap-clear-cell. Returns the resulting cell (coordinates, item, orientation).

## How to Call

### HTTP API (Direct Tool Execution)

Execute this tool directly via the MCP Plugin HTTP API:

```bash
curl -X POST http://localhost:23630/api/tools/gridmap-set-cell \
  -H "Content-Type: application/json" \
  -d '{
  "nodePath": "string_value",
  "cellX": 0,
  "cellY": 0,
  "cellZ": 0,
  "item": 0,
  "orientation": 0
}'
```

> For complex input (multi-line strings, code), save the JSON to a file and use `-d @args.json`.
>
> Or pipe via stdin:
> ```bash
> curl -X POST http://localhost:23630/api/tools/gridmap-set-cell -H "Content-Type: application/json" -d @- <<'EOF'
> {"param": "value"}
> EOF
> ```

#### With Authorization (if required)

```bash
curl -X POST http://localhost:23630/api/tools/gridmap-set-cell \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer YOUR_TOKEN" \
  -d '{
  "nodePath": "string_value",
  "cellX": 0,
  "cellY": 0,
  "cellZ": 0,
  "item": 0,
  "orientation": 0
}'
```

## Input

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `nodePath` | `string` | Yes | Node path (relative to the edited scene root) of the GridMap node. |
| `cellX` | `integer` | Yes | Cell X coordinate (grid space). |
| `cellY` | `integer` | Yes | Cell Y coordinate (grid space). |
| `cellZ` | `integer` | Yes | Cell Z coordinate (grid space). |
| `item` | `integer` | Yes | MeshLibrary item id to place in the cell; clamped to >= 0. |
| `orientation` | `integer` | No | Optional orthogonal orientation index (0..23); clamped into range. Defaults to 0. |

### Input JSON Schema

```json
{
  "type": "object",
  "properties": {
    "nodePath": {
      "type": "string",
      "description": "Node path (relative to the edited scene root) of the GridMap node."
    },
    "cellX": {
      "type": "integer",
      "description": "Cell X coordinate (grid space)."
    },
    "cellY": {
      "type": "integer",
      "description": "Cell Y coordinate (grid space)."
    },
    "cellZ": {
      "type": "integer",
      "description": "Cell Z coordinate (grid space)."
    },
    "item": {
      "type": "integer",
      "description": "MeshLibrary item id to place in the cell; clamped to >= 0."
    },
    "orientation": {
      "type": "integer",
      "description": "Optional orthogonal orientation index (0..23); clamped into range. Defaults to 0."
    }
  },
  "required": [
    "nodePath",
    "cellX",
    "cellY",
    "cellZ",
    "item"
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
      "$ref": "#/$defs/com.IvanMurzak.Godot.MCP.GridMap.GridMapCellInfo"
    }
  },
  "$defs": {
    "com.IvanMurzak.Godot.MCP.GridMap.GridMapCellInfo": {
      "type": "object",
      "properties": {
        "NodePath": {
          "type": "string"
        },
        "CellX": {
          "type": "integer"
        },
        "CellY": {
          "type": "integer"
        },
        "CellZ": {
          "type": "integer"
        },
        "Item": {
          "type": "integer"
        },
        "Orientation": {
          "type": "integer"
        },
        "IsEmpty": {
          "type": "boolean"
        }
      },
      "required": [
        "CellX",
        "CellY",
        "CellZ",
        "Item",
        "Orientation"
      ]
    }
  },
  "required": [
    "result"
  ]
}
```

