---
name: csg-box-create
description: Create a CsgBox3D primitive in the currently edited Godot scene and return its structured config. Optionally pass 'parentPath' (a node path relative to the scene root) to parent it (defaults to the scene root, or place it under a CsgCombiner3D to take part in a boolean op), 'name' to rename it, and 'sizeX'/'sizeY'/'sizeZ' to seed its extents (each clamped to a positive value; default 2). The new node's owner is set to the scene root so it is saved with the scene.
---

# CSG / Box Create

Create a CsgBox3D primitive in the currently edited Godot scene and return its structured config. Optionally pass 'parentPath' (a node path relative to the scene root) to parent it (defaults to the scene root, or place it under a CsgCombiner3D to take part in a boolean op), 'name' to rename it, and 'sizeX'/'sizeY'/'sizeZ' to seed its extents (each clamped to a positive value; default 2). The new node's owner is set to the scene root so it is saved with the scene.

## How to Call

### HTTP API (Direct Tool Execution)

Execute this tool directly via the MCP Plugin HTTP API:

```bash
curl -X POST http://localhost:23630/api/tools/csg-box-create \
  -H "Content-Type: application/json" \
  -d '{
  "name": "string_value",
  "parentPath": "string_value",
  "sizeX": "string_value",
  "sizeY": "string_value",
  "sizeZ": "string_value"
}'
```

> For complex input (multi-line strings, code), save the JSON to a file and use `-d @args.json`.
>
> Or pipe via stdin:
> ```bash
> curl -X POST http://localhost:23630/api/tools/csg-box-create -H "Content-Type: application/json" -d @- <<'EOF'
> {"param": "value"}
> EOF
> ```

#### With Authorization (if required)

```bash
curl -X POST http://localhost:23630/api/tools/csg-box-create \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer YOUR_TOKEN" \
  -d '{
  "name": "string_value",
  "parentPath": "string_value",
  "sizeX": "string_value",
  "sizeY": "string_value",
  "sizeZ": "string_value"
}'
```

## Input

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `name` | `string` | No | Name for the new node. When omitted, Godot's default name for the type is used. |
| `parentPath` | `string` | No | Node path (relative to the edited scene root) of the parent. When omitted, the node is parented to the scene root. |
| `sizeX` | `any` | No | Box width along X (Godot 'size.x'); clamped to > 0. Defaults to 2. |
| `sizeY` | `any` | No | Box height along Y (Godot 'size.y'); clamped to > 0. Defaults to 2. |
| `sizeZ` | `any` | No | Box depth along Z (Godot 'size.z'); clamped to > 0. Defaults to 2. |

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
    "sizeX": {
      "$ref": "#/$defs/System.Double",
      "description": "Box width along X (Godot 'size.x'); clamped to > 0. Defaults to 2."
    },
    "sizeY": {
      "$ref": "#/$defs/System.Double",
      "description": "Box height along Y (Godot 'size.y'); clamped to > 0. Defaults to 2."
    },
    "sizeZ": {
      "$ref": "#/$defs/System.Double",
      "description": "Box depth along Z (Godot 'size.z'); clamped to > 0. Defaults to 2."
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
      "$ref": "#/$defs/com.IvanMurzak.Godot.MCP.CSG.CsgShapeInfo"
    }
  },
  "$defs": {
    "com.IvanMurzak.Godot.MCP.CSG.CsgShapeInfo": {
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
        "Operation": {
          "type": "string"
        },
        "SizeX": {
          "type": "number"
        },
        "SizeY": {
          "type": "number"
        },
        "SizeZ": {
          "type": "number"
        },
        "Radius": {
          "type": "number"
        },
        "Height": {
          "type": "number"
        },
        "RadialSegments": {
          "type": "integer"
        }
      },
      "required": [
        "SizeX",
        "SizeY",
        "SizeZ",
        "Radius",
        "Height",
        "RadialSegments"
      ]
    }
  },
  "required": [
    "result"
  ]
}
```

