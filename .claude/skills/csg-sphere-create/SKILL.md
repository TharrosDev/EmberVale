---
name: csg-sphere-create
description: Create a CsgSphere3D primitive in the currently edited Godot scene and return its structured config. Optionally pass 'parentPath' (a node path relative to the scene root) to parent it (defaults to the scene root, or place it under a CsgCombiner3D for a boolean op), 'name' to rename it, 'radius' (clamped to > 0; default 0.5), and 'radialSegments' (clamped to >= 4; default 12). The new node's owner is set to the scene root so it is saved with the scene.
---

# CSG / Sphere Create

Create a CsgSphere3D primitive in the currently edited Godot scene and return its structured config. Optionally pass 'parentPath' (a node path relative to the scene root) to parent it (defaults to the scene root, or place it under a CsgCombiner3D for a boolean op), 'name' to rename it, 'radius' (clamped to > 0; default 0.5), and 'radialSegments' (clamped to >= 4; default 12). The new node's owner is set to the scene root so it is saved with the scene.

## How to Call

### HTTP API (Direct Tool Execution)

Execute this tool directly via the MCP Plugin HTTP API:

```bash
curl -X POST http://localhost:23630/api/tools/csg-sphere-create \
  -H "Content-Type: application/json" \
  -d '{
  "name": "string_value",
  "parentPath": "string_value",
  "radius": "string_value",
  "radialSegments": "string_value"
}'
```

> For complex input (multi-line strings, code), save the JSON to a file and use `-d @args.json`.
>
> Or pipe via stdin:
> ```bash
> curl -X POST http://localhost:23630/api/tools/csg-sphere-create -H "Content-Type: application/json" -d @- <<'EOF'
> {"param": "value"}
> EOF
> ```

#### With Authorization (if required)

```bash
curl -X POST http://localhost:23630/api/tools/csg-sphere-create \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer YOUR_TOKEN" \
  -d '{
  "name": "string_value",
  "parentPath": "string_value",
  "radius": "string_value",
  "radialSegments": "string_value"
}'
```

## Input

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `name` | `string` | No | Name for the new node. When omitted, Godot's default name for the type is used. |
| `parentPath` | `string` | No | Node path (relative to the edited scene root) of the parent. When omitted, the node is parented to the scene root. |
| `radius` | `any` | No | Sphere radius (Godot 'radius'); clamped to > 0. Defaults to 0.5. |
| `radialSegments` | `any` | No | Number of radial segments (Godot 'radial_segments'); clamped to >= 4. Defaults to 12. |

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
      "description": "Sphere radius (Godot 'radius'); clamped to > 0. Defaults to 0.5."
    },
    "radialSegments": {
      "$ref": "#/$defs/System.Int32",
      "description": "Number of radial segments (Godot 'radial_segments'); clamped to >= 4. Defaults to 12."
    }
  },
  "$defs": {
    "System.Double": {
      "type": "number"
    },
    "System.Int32": {
      "type": "integer"
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

