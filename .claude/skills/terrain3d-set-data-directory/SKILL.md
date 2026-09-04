---
name: terrain3d-set-data-directory
description: "Set the data directory of an existing Terrain3D node, addressed by 'nodePath' (relative to the edited scene root). 'dataDirectory' is a res://-relative path where Terrain3D persists and loads region data on disk — it must be writable. Requires the Terrain3D addon (returns 'Installed: false' with an install hint when absent). Returns the terrain's updated structured config."
---

# Terrain3D / Set Data Directory

Set the data directory of an existing Terrain3D node, addressed by 'nodePath' (relative to the edited scene root). 'dataDirectory' is a res://-relative path where Terrain3D persists and loads region data on disk — it must be writable. Requires the Terrain3D addon (returns 'Installed: false' with an install hint when absent). Returns the terrain's updated structured config.

## How to Call

### HTTP API (Direct Tool Execution)

Execute this tool directly via the MCP Plugin HTTP API:

```bash
curl -X POST http://localhost:23630/api/tools/terrain3d-set-data-directory \
  -H "Content-Type: application/json" \
  -d '{
  "nodePath": "string_value",
  "dataDirectory": "string_value"
}'
```

> For complex input (multi-line strings, code), save the JSON to a file and use `-d @args.json`.
>
> Or pipe via stdin:
> ```bash
> curl -X POST http://localhost:23630/api/tools/terrain3d-set-data-directory -H "Content-Type: application/json" -d @- <<'EOF'
> {"param": "value"}
> EOF
> ```

#### With Authorization (if required)

```bash
curl -X POST http://localhost:23630/api/tools/terrain3d-set-data-directory \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer YOUR_TOKEN" \
  -d '{
  "nodePath": "string_value",
  "dataDirectory": "string_value"
}'
```

## Input

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `nodePath` | `string` | Yes | Node path (relative to the edited scene root) of the Terrain3D node to update. |
| `dataDirectory` | `string` | Yes | The res:// data directory Terrain3D should persist/load region data from (must be writable). |

### Input JSON Schema

```json
{
  "type": "object",
  "properties": {
    "nodePath": {
      "type": "string",
      "description": "Node path (relative to the edited scene root) of the Terrain3D node to update."
    },
    "dataDirectory": {
      "type": "string",
      "description": "The res:// data directory Terrain3D should persist/load region data from (must be writable)."
    }
  },
  "required": [
    "nodePath",
    "dataDirectory"
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

