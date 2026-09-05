---
name: csg-defaults
description: "Return the recommended starter configuration (size / radius / height / radial segments) for a Godot CSG node of the requested 'kind'. Pure-managed: touches no scene, so it is safe to call any time to discover sane defaults before creating a real CSG node. 'kind' accepts 'box', 'sphere', 'cylinder', or 'combiner' (default 'box')."
---

# CSG / Defaults

Return the recommended starter configuration (size / radius / height / radial segments) for a Godot CSG node of the requested 'kind'. Pure-managed: touches no scene, so it is safe to call any time to discover sane defaults before creating a real CSG node. 'kind' accepts 'box', 'sphere', 'cylinder', or 'combiner' (default 'box').

## How to Call

### HTTP API (Direct Tool Execution)

Execute this tool directly via the MCP Plugin HTTP API:

```bash
curl -X POST http://localhost:23630/api/tools/csg-defaults \
  -H "Content-Type: application/json" \
  -d '{
  "kind": "string_value"
}'
```

> For complex input (multi-line strings, code), save the JSON to a file and use `-d @args.json`.
>
> Or pipe via stdin:
> ```bash
> curl -X POST http://localhost:23630/api/tools/csg-defaults -H "Content-Type: application/json" -d @- <<'EOF'
> {"param": "value"}
> EOF
> ```

#### With Authorization (if required)

```bash
curl -X POST http://localhost:23630/api/tools/csg-defaults \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer YOUR_TOKEN" \
  -d '{
  "kind": "string_value"
}'
```

## Input

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `kind` | `string` | No | CSG kind: 'box' (CsgBox3D), 'sphere' (CsgSphere3D), 'cylinder' (CsgCylinder3D), or 'combiner' (CsgCombiner3D). Defaults to 'box'. |

### Input JSON Schema

```json
{
  "type": "object",
  "properties": {
    "kind": {
      "type": "string",
      "description": "CSG kind: 'box' (CsgBox3D), 'sphere' (CsgSphere3D), 'cylinder' (CsgCylinder3D), or 'combiner' (CsgCombiner3D). Defaults to 'box'."
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

