---
name: gridmap-create
description: Create a GridMap node in the currently edited Godot scene and return its structured config. Optionally pass 'parentPath' (a node path relative to the scene root) to parent it (defaults to the scene root), 'name' to rename it, and 'cellSizeX'/'cellSizeY'/'cellSizeZ' to set the cell dimensions (each clamped to > 0; unspecified axes keep the engine default). The new node's owner is set to the scene root so it is saved with the scene. Assign a MeshLibrary and place cells with the other gridmap-* tools.
---

# GridMap / Create

Create a GridMap node in the currently edited Godot scene and return its structured config. Optionally pass 'parentPath' (a node path relative to the scene root) to parent it (defaults to the scene root), 'name' to rename it, and 'cellSizeX'/'cellSizeY'/'cellSizeZ' to set the cell dimensions (each clamped to > 0; unspecified axes keep the engine default). The new node's owner is set to the scene root so it is saved with the scene. Assign a MeshLibrary and place cells with the other gridmap-* tools.

## How to Call

### HTTP API (Direct Tool Execution)

Execute this tool directly via the MCP Plugin HTTP API:

```bash
curl -X POST http://localhost:23630/api/tools/gridmap-create \
  -H "Content-Type: application/json" \
  -d '{
  "name": "string_value",
  "parentPath": "string_value",
  "cellSizeX": "string_value",
  "cellSizeY": "string_value",
  "cellSizeZ": "string_value"
}'
```

> For complex input (multi-line strings, code), save the JSON to a file and use `-d @args.json`.
>
> Or pipe via stdin:
> ```bash
> curl -X POST http://localhost:23630/api/tools/gridmap-create -H "Content-Type: application/json" -d @- <<'EOF'
> {"param": "value"}
> EOF
> ```

#### With Authorization (if required)

```bash
curl -X POST http://localhost:23630/api/tools/gridmap-create \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer YOUR_TOKEN" \
  -d '{
  "name": "string_value",
  "parentPath": "string_value",
  "cellSizeX": "string_value",
  "cellSizeY": "string_value",
  "cellSizeZ": "string_value"
}'
```

## Input

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `name` | `string` | No | Name for the new node. When omitted, Godot's default name for the type is used. |
| `parentPath` | `string` | No | Node path (relative to the edited scene root) of the parent. When omitted, the node is parented to the scene root. |
| `cellSizeX` | `any` | No | Optional cell size on the X axis (Godot 'cell_size.x'); clamped to > 0. |
| `cellSizeY` | `any` | No | Optional cell size on the Y axis (Godot 'cell_size.y'); clamped to > 0. |
| `cellSizeZ` | `any` | No | Optional cell size on the Z axis (Godot 'cell_size.z'); clamped to > 0. |

### Input JSON Schema

```json
{
  "type": "object",
  "properties": {
    "name": {
      "type": "string",
      "description": "Name for the new node. When omitted, Godot's default name for the type is used."
    },
    "parentPath": {
      "type": "string",
      "description": "Node path (relative to the edited scene root) of the parent. When omitted, the node is parented to the scene root."
    },
    "cellSizeX": {
      "$ref": "#/$defs/System.Double",
      "description": "Optional cell size on the X axis (Godot 'cell_size.x'); clamped to > 0."
    },
    "cellSizeY": {
      "$ref": "#/$defs/System.Double",
      "description": "Optional cell size on the Y axis (Godot 'cell_size.y'); clamped to > 0."
    },
    "cellSizeZ": {
      "$ref": "#/$defs/System.Double",
      "description": "Optional cell size on the Z axis (Godot 'cell_size.z'); clamped to > 0."
    }
  },
  "$defs": {
    "System.Double": {
      "type": "number"
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

