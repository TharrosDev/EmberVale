---
name: gridmap-set-mesh-library
description: "Assign a MeshLibrary resource to an existing GridMap node, addressed by 'nodePath' (relative to the edited scene root). 'resourcePath' is the res:// path of the MeshLibrary resource (e.g. 'res://tiles.meshlib' or a '.tres'); it must exist and be a MeshLibrary. The MeshLibrary's item ids are what gridmap-set-cell places. Returns the GridMap's updated config."
---

# GridMap / Set Mesh Library

Assign a MeshLibrary resource to an existing GridMap node, addressed by 'nodePath' (relative to the edited scene root). 'resourcePath' is the res:// path of the MeshLibrary resource (e.g. 'res://tiles.meshlib' or a '.tres'); it must exist and be a MeshLibrary. The MeshLibrary's item ids are what gridmap-set-cell places. Returns the GridMap's updated config.

## How to Call

### HTTP API (Direct Tool Execution)

Execute this tool directly via the MCP Plugin HTTP API:

```bash
curl -X POST http://localhost:23630/api/tools/gridmap-set-mesh-library \
  -H "Content-Type: application/json" \
  -d '{
  "nodePath": "string_value",
  "resourcePath": "string_value"
}'
```

> For complex input (multi-line strings, code), save the JSON to a file and use `-d @args.json`.
>
> Or pipe via stdin:
> ```bash
> curl -X POST http://localhost:23630/api/tools/gridmap-set-mesh-library -H "Content-Type: application/json" -d @- <<'EOF'
> {"param": "value"}
> EOF
> ```

#### With Authorization (if required)

```bash
curl -X POST http://localhost:23630/api/tools/gridmap-set-mesh-library \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer YOUR_TOKEN" \
  -d '{
  "nodePath": "string_value",
  "resourcePath": "string_value"
}'
```

## Input

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `nodePath` | `string` | Yes | Node path (relative to the edited scene root) of the GridMap node. |
| `resourcePath` | `string` | Yes | res:// path of the MeshLibrary resource to assign (must exist and be a MeshLibrary). |

### Input JSON Schema

```json
{
  "type": "object",
  "properties": {
    "nodePath": {
      "type": "string",
      "description": "Node path (relative to the edited scene root) of the GridMap node."
    },
    "resourcePath": {
      "type": "string",
      "description": "res:// path of the MeshLibrary resource to assign (must exist and be a MeshLibrary)."
    }
  },
  "required": [
    "nodePath",
    "resourcePath"
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

