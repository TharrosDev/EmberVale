---
name: gridmap-clear-cell
description: Clear a single cell of a GridMap (addressed by 'nodePath', relative to the edited scene root) at grid coordinates ('cellX','cellY','cellZ') — i.e. set it empty. Returns the resulting cell, whose item is -1 (empty). To clear the whole grid, use gridmap-clear.
---

# GridMap / Clear Cell

Clear a single cell of a GridMap (addressed by 'nodePath', relative to the edited scene root) at grid coordinates ('cellX','cellY','cellZ') — i.e. set it empty. Returns the resulting cell, whose item is -1 (empty). To clear the whole grid, use gridmap-clear.

## How to Call

### HTTP API (Direct Tool Execution)

Execute this tool directly via the MCP Plugin HTTP API:

```bash
curl -X POST http://localhost:23630/api/tools/gridmap-clear-cell \
  -H "Content-Type: application/json" \
  -d '{
  "nodePath": "string_value",
  "cellX": 0,
  "cellY": 0,
  "cellZ": 0
}'
```

> For complex input (multi-line strings, code), save the JSON to a file and use `-d @args.json`.
>
> Or pipe via stdin:
> ```bash
> curl -X POST http://localhost:23630/api/tools/gridmap-clear-cell -H "Content-Type: application/json" -d @- <<'EOF'
> {"param": "value"}
> EOF
> ```

#### With Authorization (if required)

```bash
curl -X POST http://localhost:23630/api/tools/gridmap-clear-cell \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer YOUR_TOKEN" \
  -d '{
  "nodePath": "string_value",
  "cellX": 0,
  "cellY": 0,
  "cellZ": 0
}'
```

## Input

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `nodePath` | `string` | Yes | Node path (relative to the edited scene root) of the GridMap node. |
| `cellX` | `integer` | Yes | Cell X coordinate (grid space). |
| `cellY` | `integer` | Yes | Cell Y coordinate (grid space). |
| `cellZ` | `integer` | Yes | Cell Z coordinate (grid space). |

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
    }
  },
  "required": [
    "nodePath",
    "cellX",
    "cellY",
    "cellZ"
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

