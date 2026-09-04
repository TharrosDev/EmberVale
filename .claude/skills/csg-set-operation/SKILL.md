---
name: csg-set-operation
description: Set the boolean operation of an existing CSG node (CsgBox3D/CsgSphere3D/CsgCylinder3D/CsgCombiner3D), addressed by 'nodePath' (relative to the edited scene root). 'operation' is 'Union' (merge), 'Intersection' (keep the overlap), or 'Subtraction' (carve away). The operation resolves against the node's CSG siblings under the same CSG parent/combiner. Returns the node's updated config.
---

# CSG / Set Operation

Set the boolean operation of an existing CSG node (CsgBox3D/CsgSphere3D/CsgCylinder3D/CsgCombiner3D), addressed by 'nodePath' (relative to the edited scene root). 'operation' is 'Union' (merge), 'Intersection' (keep the overlap), or 'Subtraction' (carve away). The operation resolves against the node's CSG siblings under the same CSG parent/combiner. Returns the node's updated config.

## How to Call

### HTTP API (Direct Tool Execution)

Execute this tool directly via the MCP Plugin HTTP API:

```bash
curl -X POST http://localhost:23630/api/tools/csg-set-operation \
  -H "Content-Type: application/json" \
  -d '{
  "nodePath": "string_value",
  "operation": "string_value"
}'
```

> For complex input (multi-line strings, code), save the JSON to a file and use `-d @args.json`.
>
> Or pipe via stdin:
> ```bash
> curl -X POST http://localhost:23630/api/tools/csg-set-operation -H "Content-Type: application/json" -d @- <<'EOF'
> {"param": "value"}
> EOF
> ```

#### With Authorization (if required)

```bash
curl -X POST http://localhost:23630/api/tools/csg-set-operation \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer YOUR_TOKEN" \
  -d '{
  "nodePath": "string_value",
  "operation": "string_value"
}'
```

## Input

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `nodePath` | `string` | Yes | Node path (relative to the edited scene root) of the CSG node to modify. |
| `operation` | `string` | Yes | The boolean operation: 'Union', 'Intersection', or 'Subtraction'. |

### Input JSON Schema

```json
{
  "type": "object",
  "properties": {
    "nodePath": {
      "type": "string",
      "description": "Node path (relative to the edited scene root) of the CSG node to modify."
    },
    "operation": {
      "type": "string",
      "description": "The boolean operation: 'Union', 'Intersection', or 'Subtraction'."
    }
  },
  "required": [
    "nodePath",
    "operation"
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

