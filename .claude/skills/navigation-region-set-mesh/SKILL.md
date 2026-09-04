---
name: navigation-region-set-mesh
description: "Assign the navigation resource of an existing NavigationRegion, addressed by 'nodePath' (relative to the edited scene root): a NavigationPolygon for a NavigationRegion2D, or a NavigationMesh for a NavigationRegion3D. Pass 'resourcePath' (a 'res://' path) to assign an existing resource — it is validated to match the region's dimension. Omit 'resourcePath' to attach a fresh empty resource of the correct type (which you can bake later). Returns the region's updated config including the assigned resource type."
---

# Navigation / Region Set Mesh

Assign the navigation resource of an existing NavigationRegion, addressed by 'nodePath' (relative to the edited scene root): a NavigationPolygon for a NavigationRegion2D, or a NavigationMesh for a NavigationRegion3D. Pass 'resourcePath' (a 'res://' path) to assign an existing resource — it is validated to match the region's dimension. Omit 'resourcePath' to attach a fresh empty resource of the correct type (which you can bake later). Returns the region's updated config including the assigned resource type.

## How to Call

### HTTP API (Direct Tool Execution)

Execute this tool directly via the MCP Plugin HTTP API:

```bash
curl -X POST http://localhost:23630/api/tools/navigation-region-set-mesh \
  -H "Content-Type: application/json" \
  -d '{
  "nodePath": "string_value",
  "resourcePath": "string_value"
}'
```

> For complex input (multi-line strings, code), save the JSON to a file and use `-d @args.json`.
>
> Or pipe via stdin:
> ```bash
> curl -X POST http://localhost:23630/api/tools/navigation-region-set-mesh -H "Content-Type: application/json" -d @- <<'EOF'
> {"param": "value"}
> EOF
> ```

#### With Authorization (if required)

```bash
curl -X POST http://localhost:23630/api/tools/navigation-region-set-mesh \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer YOUR_TOKEN" \
  -d '{
  "nodePath": "string_value",
  "resourcePath": "string_value"
}'
```

## Input

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `nodePath` | `string` | Yes | Node path (relative to the edited scene root) of the NavigationRegion to set the resource on. |
| `resourcePath` | `string` | No | Optional 'res://' path of an existing NavigationPolygon (2D) / NavigationMesh (3D) to assign. When omitted, a fresh empty resource of the matching type is attached. |

### Input JSON Schema

```json
{
  "type": "object",
  "properties": {
    "nodePath": {
      "type": "string",
      "description": "Node path (relative to the edited scene root) of the NavigationRegion to set the resource on."
    },
    "resourcePath": {
      "type": "string",
      "description": "Optional 'res://' path of an existing NavigationPolygon (2D) / NavigationMesh (3D) to assign. When omitted, a fresh empty resource of the matching type is attached."
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
      "$ref": "#/$defs/com.IvanMurzak.Godot.MCP.Navigation.NavigationNodeInfo"
    }
  },
  "$defs": {
    "com.IvanMurzak.Godot.MCP.Navigation.NavigationNodeInfo": {
      "type": "object",
      "properties": {
        "NodePath": {
          "type": "string"
        },
        "Dimension": {
          "type": "string"
        },
        "Kind": {
          "type": "string"
        },
        "TypeName": {
          "type": "string"
        },
        "Enabled": {
          "type": "boolean"
        },
        "ResourcePath": {
          "type": "string"
        },
        "ResourceType": {
          "type": "string"
        },
        "Radius": {
          "type": "number"
        },
        "PathDesiredDistance": {
          "type": "number"
        },
        "TargetDesiredDistance": {
          "type": "number"
        },
        "MaxSpeed": {
          "type": "number"
        },
        "AvoidanceEnabled": {
          "type": "boolean"
        },
        "StartX": {
          "type": "number"
        },
        "StartY": {
          "type": "number"
        },
        "StartZ": {
          "type": "number"
        },
        "EndX": {
          "type": "number"
        },
        "EndY": {
          "type": "number"
        },
        "EndZ": {
          "type": "number"
        },
        "Bidirectional": {
          "type": "boolean"
        }
      },
      "required": [
        "Enabled",
        "Radius",
        "PathDesiredDistance",
        "TargetDesiredDistance",
        "MaxSpeed",
        "AvoidanceEnabled",
        "StartX",
        "StartY",
        "StartZ",
        "EndX",
        "EndY",
        "EndZ",
        "Bidirectional"
      ]
    }
  },
  "required": [
    "result"
  ]
}
```

