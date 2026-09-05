---
name: terrain3d-set-region-size
description: "Set the region size of an existing Terrain3D node, addressed by 'nodePath' (relative to the edited scene root). 'regionSize' is snapped to the nearest valid Terrain3D RegionSize (64/128/256/512/1024/2048). Calls the addon's change_region_size (which re-slices existing region data). Requires the Terrain3D addon (returns 'Installed: false' with an install hint when absent). Returns the terrain's updated structured config (RegionSize reflects the snapped value)."
---

# Terrain3D / Set Region Size

Set the region size of an existing Terrain3D node, addressed by 'nodePath' (relative to the edited scene root). 'regionSize' is snapped to the nearest valid Terrain3D RegionSize (64/128/256/512/1024/2048). Calls the addon's change_region_size (which re-slices existing region data). Requires the Terrain3D addon (returns 'Installed: false' with an install hint when absent). Returns the terrain's updated structured config (RegionSize reflects the snapped value).

## How to Call

### HTTP API (Direct Tool Execution)

Execute this tool directly via the MCP Plugin HTTP API:

```bash
curl -X POST http://localhost:23630/api/tools/terrain3d-set-region-size \
  -H "Content-Type: application/json" \
  -d '{
  "nodePath": "string_value",
  "regionSize": 0
}'
```

> For complex input (multi-line strings, code), save the JSON to a file and use `-d @args.json`.
>
> Or pipe via stdin:
> ```bash
> curl -X POST http://localhost:23630/api/tools/terrain3d-set-region-size -H "Content-Type: application/json" -d @- <<'EOF'
> {"param": "value"}
> EOF
> ```

#### With Authorization (if required)

```bash
curl -X POST http://localhost:23630/api/tools/terrain3d-set-region-size \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer YOUR_TOKEN" \
  -d '{
  "nodePath": "string_value",
  "regionSize": 0
}'
```

## Input

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `nodePath` | `string` | Yes | Node path (relative to the edited scene root) of the Terrain3D node to update. |
| `regionSize` | `integer` | Yes | Requested region size; snapped to the nearest valid Terrain3D RegionSize (64/128/256/512/1024/2048). |

### Input JSON Schema

```json
{
  "type": "object",
  "properties": {
    "nodePath": {
      "type": "string",
      "description": "Node path (relative to the edited scene root) of the Terrain3D node to update."
    },
    "regionSize": {
      "type": "integer",
      "description": "Requested region size; snapped to the nearest valid Terrain3D RegionSize (64/128/256/512/1024/2048)."
    }
  },
  "required": [
    "nodePath",
    "regionSize"
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

