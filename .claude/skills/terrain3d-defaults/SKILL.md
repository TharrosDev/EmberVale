---
name: terrain3d-defaults
description: "Return the recommended starter configuration (region size, mesh LODs, mesh size, vertex spacing, data directory) for a Godot Terrain3D node. Pure-managed: needs no scene and no addon, so it is safe to call any time to discover sane defaults before creating a real terrain. 'preset' accepts 'small' (region size 256), 'medium' (1024, the default) or 'large' (2048). Note: Terrain3D persists region data to the returned 'DataDirectory' on disk, so that directory must be writable."
---

# Terrain3D / Defaults

Return the recommended starter configuration (region size, mesh LODs, mesh size, vertex spacing, data directory) for a Godot Terrain3D node. Pure-managed: needs no scene and no addon, so it is safe to call any time to discover sane defaults before creating a real terrain. 'preset' accepts 'small' (region size 256), 'medium' (1024, the default) or 'large' (2048). Note: Terrain3D persists region data to the returned 'DataDirectory' on disk, so that directory must be writable.

## How to Call

### HTTP API (Direct Tool Execution)

Execute this tool directly via the MCP Plugin HTTP API:

```bash
curl -X POST http://localhost:23630/api/tools/terrain3d-defaults \
  -H "Content-Type: application/json" \
  -d '{
  "preset": "string_value"
}'
```

> For complex input (multi-line strings, code), save the JSON to a file and use `-d @args.json`.
>
> Or pipe via stdin:
> ```bash
> curl -X POST http://localhost:23630/api/tools/terrain3d-defaults -H "Content-Type: application/json" -d @- <<'EOF'
> {"param": "value"}
> EOF
> ```

#### With Authorization (if required)

```bash
curl -X POST http://localhost:23630/api/tools/terrain3d-defaults \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer YOUR_TOKEN" \
  -d '{
  "preset": "string_value"
}'
```

## Input

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `preset` | `string` | No | Detail preset: 'small' (region size 256), 'medium' (1024) or 'large' (2048). Defaults to 'medium' when omitted. |

### Input JSON Schema

```json
{
  "type": "object",
  "properties": {
    "preset": {
      "type": "string",
      "description": "Detail preset: 'small' (region size 256), 'medium' (1024) or 'large' (2048). Defaults to 'medium' when omitted."
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
      "$ref": "#/$defs/com.IvanMurzak.Godot.MCP.Terrain3D.Terrain3DConfig"
    }
  },
  "$defs": {
    "com.IvanMurzak.Godot.MCP.Terrain3D.Terrain3DConfig": {
      "type": "object",
      "properties": {
        "Preset": {
          "type": "string"
        },
        "RegionSize": {
          "type": "integer"
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
        "DataDirectory": {
          "type": "string"
        },
        "Save16Bit": {
          "type": "boolean"
        }
      },
      "required": [
        "RegionSize",
        "MeshLods",
        "MeshSize",
        "VertexSpacing",
        "Save16Bit"
      ]
    }
  },
  "required": [
    "result"
  ]
}
```

