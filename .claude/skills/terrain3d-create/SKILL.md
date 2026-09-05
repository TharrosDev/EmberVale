---
name: terrain3d-create
description: "Create a Terrain3D node in the currently edited Godot scene and return its structured config. Requires the Terrain3D GDExtension addon (returns 'Installed: false' with an install hint when absent). Optionally pass 'name' to rename it, 'parentPath' (a node path relative to the scene root) to parent it (defaults to the scene root), 'regionSize' to seed the region size (snapped to a valid Terrain3D size: 64/128/256/512/1024/2048), and 'dataDirectory' (a res:// path) to seed where the terrain persists region data on disk. The new node's owner is set to the scene root so it is saved with the scene."
---

# Terrain3D / Create

Create a Terrain3D node in the currently edited Godot scene and return its structured config. Requires the Terrain3D GDExtension addon (returns 'Installed: false' with an install hint when absent). Optionally pass 'name' to rename it, 'parentPath' (a node path relative to the scene root) to parent it (defaults to the scene root), 'regionSize' to seed the region size (snapped to a valid Terrain3D size: 64/128/256/512/1024/2048), and 'dataDirectory' (a res:// path) to seed where the terrain persists region data on disk. The new node's owner is set to the scene root so it is saved with the scene.

## How to Call

### HTTP API (Direct Tool Execution)

Execute this tool directly via the MCP Plugin HTTP API:

```bash
curl -X POST http://localhost:23630/api/tools/terrain3d-create \
  -H "Content-Type: application/json" \
  -d '{
  "name": "string_value",
  "parentPath": "string_value",
  "regionSize": "string_value",
  "dataDirectory": "string_value"
}'
```

> For complex input (multi-line strings, code), save the JSON to a file and use `-d @args.json`.
>
> Or pipe via stdin:
> ```bash
> curl -X POST http://localhost:23630/api/tools/terrain3d-create -H "Content-Type: application/json" -d @- <<'EOF'
> {"param": "value"}
> EOF
> ```

#### With Authorization (if required)

```bash
curl -X POST http://localhost:23630/api/tools/terrain3d-create \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer YOUR_TOKEN" \
  -d '{
  "name": "string_value",
  "parentPath": "string_value",
  "regionSize": "string_value",
  "dataDirectory": "string_value"
}'
```

## Input

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `name` | `string` | No | Name for the new Terrain3D node. When omitted, Godot's default name for the type is used. |
| `parentPath` | `string` | No | Node path (relative to the edited scene root) of the parent. When omitted, the node is parented to the scene root. |
| `regionSize` | `any` | No | Optional initial region size; snapped to a valid Terrain3D RegionSize (64/128/256/512/1024/2048). When omitted, the addon default (256) is kept. |
| `dataDirectory` | `string` | No | Optional initial res:// data directory where Terrain3D persists region data (must be writable). When omitted, no data directory is set. |

### Input JSON Schema

```json
{
  "type": "object",
  "properties": {
    "name": {
      "type": "string",
      "description": "Name for the new Terrain3D node. When omitted, Godot's default name for the type is used."
    },
    "parentPath": {
      "type": "string",
      "description": "Node path (relative to the edited scene root) of the parent. When omitted, the node is parented to the scene root."
    },
    "regionSize": {
      "$ref": "#/$defs/System.Int32",
      "description": "Optional initial region size; snapped to a valid Terrain3D RegionSize (64/128/256/512/1024/2048). When omitted, the addon default (256) is kept."
    },
    "dataDirectory": {
      "type": "string",
      "description": "Optional initial res:// data directory where Terrain3D persists region data (must be writable). When omitted, no data directory is set."
    }
  },
  "$defs": {
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

