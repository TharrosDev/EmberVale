---
name: gridmap-defaults
description: "Return the recommended starter configuration (cell size, cell scale, octant size, centering) for a new Godot GridMap node. Pure-managed: touches no scene, so it is safe to call any time to discover sane defaults before creating a real GridMap. Optionally pass 'cellSize' to override the uniform cell-size dimension (clamped to > 0); when omitted a unit cube (1) is used."
---

# GridMap / Defaults

Return the recommended starter configuration (cell size, cell scale, octant size, centering) for a new Godot GridMap node. Pure-managed: touches no scene, so it is safe to call any time to discover sane defaults before creating a real GridMap. Optionally pass 'cellSize' to override the uniform cell-size dimension (clamped to > 0); when omitted a unit cube (1) is used.

## How to Call

### HTTP API (Direct Tool Execution)

Execute this tool directly via the MCP Plugin HTTP API:

```bash
curl -X POST http://localhost:23630/api/tools/gridmap-defaults \
  -H "Content-Type: application/json" \
  -d '{
  "cellSize": "string_value"
}'
```

> For complex input (multi-line strings, code), save the JSON to a file and use `-d @args.json`.
>
> Or pipe via stdin:
> ```bash
> curl -X POST http://localhost:23630/api/tools/gridmap-defaults -H "Content-Type: application/json" -d @- <<'EOF'
> {"param": "value"}
> EOF
> ```

#### With Authorization (if required)

```bash
curl -X POST http://localhost:23630/api/tools/gridmap-defaults \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer YOUR_TOKEN" \
  -d '{
  "cellSize": "string_value"
}'
```

## Input

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `cellSize` | `any` | No | Optional uniform cell-size dimension to apply to X/Y/Z; clamped to > 0. When null, the default cell size (1) is returned. |

### Input JSON Schema

```json
{
  "type": "object",
  "properties": {
    "cellSize": {
      "$ref": "#/$defs/System.Double",
      "description": "Optional uniform cell-size dimension to apply to X/Y/Z; clamped to > 0. When null, the default cell size (1) is returned."
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
      "$ref": "#/$defs/com.IvanMurzak.Godot.MCP.GridMap.GridMapNodeInfo"
    }
  },
  "$defs": {
    "com.IvanMurzak.Godot.MCP.GridMap.GridMapNodeInfo": {
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
        "CellSizeX": {
          "type": "number"
        },
        "CellSizeY": {
          "type": "number"
        },
        "CellSizeZ": {
          "type": "number"
        },
        "CellScale": {
          "type": "number"
        },
        "CellOctantSize": {
          "type": "integer"
        },
        "CellCenterX": {
          "type": "boolean"
        },
        "CellCenterY": {
          "type": "boolean"
        },
        "CellCenterZ": {
          "type": "boolean"
        },
        "MeshLibraryPath": {
          "type": "string"
        },
        "CellCount": {
          "type": "integer"
        }
      },
      "required": [
        "CellSizeX",
        "CellSizeY",
        "CellSizeZ",
        "CellScale",
        "CellOctantSize",
        "CellCenterX",
        "CellCenterY",
        "CellCenterZ",
        "CellCount"
      ]
    }
  },
  "required": [
    "result"
  ]
}
```

