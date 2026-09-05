---
name: terrain3d-set-material
description: "Assign a Terrain3DMaterial to an existing Terrain3D node, addressed by 'nodePath' (relative to the edited scene root). When the terrain has no material a fresh Terrain3DMaterial is created and assigned; pass 'replace: true' to always create+assign a fresh one. Requires the Terrain3D addon (returns 'Installed: false' with an install hint when absent). Returns the terrain's updated structured config (HasMaterial is true on success)."
---

# Terrain3D / Set Material

Assign a Terrain3DMaterial to an existing Terrain3D node, addressed by 'nodePath' (relative to the edited scene root). When the terrain has no material a fresh Terrain3DMaterial is created and assigned; pass 'replace: true' to always create+assign a fresh one. Requires the Terrain3D addon (returns 'Installed: false' with an install hint when absent). Returns the terrain's updated structured config (HasMaterial is true on success).

## How to Call

### HTTP API (Direct Tool Execution)

Execute this tool directly via the MCP Plugin HTTP API:

```bash
curl -X POST http://localhost:23630/api/tools/terrain3d-set-material \
  -H "Content-Type: application/json" \
  -d '{
  "nodePath": "string_value",
  "replace": "string_value"
}'
```

> For complex input (multi-line strings, code), save the JSON to a file and use `-d @args.json`.
>
> Or pipe via stdin:
> ```bash
> curl -X POST http://localhost:23630/api/tools/terrain3d-set-material -H "Content-Type: application/json" -d @- <<'EOF'
> {"param": "value"}
> EOF
> ```

#### With Authorization (if required)

```bash
curl -X POST http://localhost:23630/api/tools/terrain3d-set-material \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer YOUR_TOKEN" \
  -d '{
  "nodePath": "string_value",
  "replace": "string_value"
}'
```

## Input

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `nodePath` | `string` | Yes | Node path (relative to the edited scene root) of the Terrain3D node to update. |
| `replace` | `any` | No | When true, always create and assign a fresh Terrain3DMaterial even if one is already assigned. When omitted/false, an existing material is left in place. |

### Input JSON Schema

```json
{
  "type": "object",
  "properties": {
    "nodePath": {
      "type": "string",
      "description": "Node path (relative to the edited scene root) of the Terrain3D node to update."
    },
    "replace": {
      "$ref": "#/$defs/System.Boolean",
      "description": "When true, always create and assign a fresh Terrain3DMaterial even if one is already assigned. When omitted/false, an existing material is left in place."
    }
  },
  "$defs": {
    "System.Boolean": {
      "type": "boolean"
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

