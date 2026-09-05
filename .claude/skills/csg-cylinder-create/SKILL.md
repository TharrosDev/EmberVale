---
name: csg-cylinder-create
description: Create a CsgCylinder3D primitive in the currently edited Godot scene and return its structured config. Optionally pass 'parentPath' (a node path relative to the scene root) to parent it (defaults to the scene root, or place it under a CsgCombiner3D for a boolean op), 'name' to rename it, 'radius' (clamped to > 0; default 0.5), and 'height' (clamped to > 0; default 2). The new node's owner is set to the scene root so it is saved with the scene.
---

# CSG / Cylinder Create

Create a CsgCylinder3D primitive in the currently edited Godot scene and return its structured config. Optionally pass 'parentPath' (a node path relative to the scene root) to parent it (defaults to the scene root, or place it under a CsgCombiner3D for a boolean op), 'name' to rename it, 'radius' (clamped to > 0; default 0.5), and 'height' (clamped to > 0; default 2). The new node's owner is set to the scene root so it is saved with the scene.

## How to Call

### HTTP API (Direct Tool Execution)

Execute this tool directly via the MCP Plugin HTTP API:

```bash
curl -X POST http://localhost:23630/api/tools/csg-cylinder-create \
  -H "Content-Type: application/json" \
  -d '{
  "name": "string_value",
  "parentPath": "string_value",
  "radius": "string_value",
  "height": "string_value"
}'
```

> For complex input (multi-line strings, code), save the JSON to a file and use `-d @args.json`.
>
> Or pipe via stdin:
> ```bash
> curl -X POST http://localhost:23630/api/tools/csg-cylinder-create -H "Content-Type: application/json" -d @- <<'EOF'
> {"param": "value"}
> EOF
> ```

#### With Authorization (if required)

```bash
curl -X POST http://localhost:23630/api/tools/csg-cylinder-create \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer YOUR_TOKEN" \
  -d '{
  "name": "string_value",
  "parentPath": "string_value",
  "radius": "string_value",
  "height": "string_value"
}'
```

## Input

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `name` | `string` | No | Name for the new node. When omitted, Godot's default name for the type is used. |
| `parentPath` | `string` | No | Node path (relative to the edited scene root) of the parent. When omitted, the node is parented to the scene root. |
| `radius` | `any` | No | Cylinder radius (Godot 'radius'); clamped to > 0. Defaults to 0.5. |
| `height` | `any` | No | Cylinder height (Godot 'height'); clamped to > 0. Defaults to 2. |

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
    "radius": {
      "$ref": "#/$defs/System.Double",
      "description": "Cylinder radius (Godot 'radius'); clamped to > 0. Defaults to 0.5."
    },
    "height": {
      "$ref": "#/$defs/System.Double",
      "description": "Cylinder height (Godot 'height'); clamped to > 0. Defaults to 2."
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

