---
name: terrain3d-get
description: "Read the scalar config (data directory, region size, version, mesh LODs, mesh size, vertex spacing, whether a material is assigned, region count) of an existing Terrain3D node, addressed by 'nodePath' (relative to the edited scene root). Read-only: does not modify the scene. Requires the Terrain3D addon (returns 'Installed: false' with an install hint when absent)."
---

# Terrain3D / Get

Read the scalar config (data directory, region size, version, mesh LODs, mesh size, vertex spacing, whether a material is assigned, region count) of an existing Terrain3D node, addressed by 'nodePath' (relative to the edited scene root). Read-only: does not modify the scene. Requires the Terrain3D addon (returns 'Installed: false' with an install hint when absent).

## How to Call

### HTTP API (Direct Tool Execution)

Execute this tool directly via the MCP Plugin HTTP API:

```bash
curl -X POST http://localhost:23630/api/tools/terrain3d-get \
  -H "Content-Type: application/json" \
  -d '{
  "nodePath": "string_value"
}'
```

> For complex input (multi-line strings, code), save the JSON to a file and use `-d @args.json`.
>
> Or pipe via stdin:
> ```bash
> curl -X POST http://localhost:23630/api/tools/terrain3d-get -H "Content-Type: application/json" -d @- <<'EOF'
> {"param": "value"}
> EOF
> ```

#### With Authorization (if required)

```bash
curl -X POST http://localhost:23630/api/tools/terrain3d-get \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer YOUR_TOKEN" \
  -d '{
  "nodePath": "string_value"
}'
```

## Input

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `nodePath` | `string` | Yes | Node path (relative to the edited scene root) of the Terrain3D node to read. |

### Input JSON Schema

```json
{
  "type": "object",
  "properties": {
    "nodePath": {
      "type": "string",
      "description": "Node path (relative to the edited scene root) of the Terrain3D node to read."
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
      "$ref": "#/$defs/com.IvanMurzak.Godot.MCP.Terrain3D.Terrain3DNodeInfo"
    }
  },
  "$defs": {
    "com.IvanMurzak.Godot.MCP.Terrain3D.Terrain3DNodeInfo": {
      "type": "object",
      "properties": {
        "Installed": {
          "type": "boolean"
        },
        "Addon": {
          "type": "string"
        },
        "MissingClass": {
          "type": "string"
        },
        "Hint": {
          "type": "string"
        },
        "NodePath": {
          "type": "string"
        },
        "TypeName": {
          "type": "string"
        },
        "DataDirectory": {
          "type": "string"
        },
        "RegionSize": {
          "type": "integer"
        },
        "Version": {
          "type": "string"
        },
        "HasMaterial": {
          "type": "boolean"
        },
        "MeshLods": {
          "type": "integer"
        },
        "MeshSize": {
          "type": "integer"
        },
        "VertexSpacing": {
          "type": "number"
        },
        "RegionCount": {
          "type": "integer"
        }
      },
      "required": [
        "Installed",
        "RegionSize",
        "HasMaterial",
        "MeshLods",
        "MeshSize",
        "VertexSpacing",
        "RegionCount"
      ]
    }
  },
  "required": [
    "result"
  ]
}
```

