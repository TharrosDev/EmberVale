---
name: gridmap-get
description: "Read the scalar config (cell size X/Y/Z, cell scale, octant size, axis centering, assigned MeshLibrary path, used-cell count) of an existing GridMap node, addressed by 'nodePath' (relative to the edited scene root). Read-only: does not modify the scene."
---

# GridMap / Get

Read the scalar config (cell size X/Y/Z, cell scale, octant size, axis centering, assigned MeshLibrary path, used-cell count) of an existing GridMap node, addressed by 'nodePath' (relative to the edited scene root). Read-only: does not modify the scene.

## How to Call

### HTTP API (Direct Tool Execution)

Execute this tool directly via the MCP Plugin HTTP API:

```bash
curl -X POST http://localhost:23630/api/tools/gridmap-get \
  -H "Content-Type: application/json" \
  -d '{
  "nodePath": "string_value"
}'
```

> For complex input (multi-line strings, code), save the JSON to a file and use `-d @args.json`.
>
> Or pipe via stdin:
> ```bash
> curl -X POST http://localhost:23630/api/tools/gridmap-get -H "Content-Type: application/json" -d @- <<'EOF'
> {"param": "value"}
> EOF
> ```

#### With Authorization (if required)

```bash
curl -X POST http://localhost:23630/api/tools/gridmap-get \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer YOUR_TOKEN" \
  -d '{
  "nodePath": "string_value"
}'
```

## Input

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `nodePath` | `string` | Yes | Node path (relative to the edited scene root) of the GridMap node to read. |

### Input JSON Schema

```json
{
  "type": "object",
  "properties": {
    "nodePath": {
      "type": "string",
      "description": "Node path (relative to the edited scene root) of the GridMap node to read."
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
      "$ref": "#/$defs/com.IvanMurzak.Godot.MCP.GridMap.GridMapNodeInfo"
    }
  },
  "$defs": {
    "com.IvanMurzak.Godot.MCP.GridMap.GridMapNodeInfo": {
      "type": "object",
      "properties": {
        "NodePath": {
          "type": "string"
        },
        "Kind": {
          "type": "string"
        },
        "TypeName": {
          "type": "string"
        },
        "CellSizeX": {
          "type": "number"
        },
        "CellSizeY": {
          "type": "number"
        },
        "CellSizeZ": {
          "type": "number"
        },
        "CellScale": {
          "type": "number"
        },
        "CellOctantSize": {
          "type": "integer"
        },
        "CellCenterX": {
          "type": "boolean"
        },
        "CellCenterY": {
          "type": "boolean"
        },
        "CellCenterZ": {
          "type": "boolean"
        },
        "MeshLibraryPath": {
          "type": "string"
        },
        "CellCount": {
          "type": "integer"
        }
      },
      "required": [
        "CellSizeX",
        "CellSizeY",
        "CellSizeZ",
        "CellScale",
        "CellOctantSize",
        "CellCenterX",
        "CellCenterY",
        "CellCenterZ",
        "CellCount"
      ]
    }
  },
  "required": [
    "result"
  ]
}
```

