---
name: navigation-defaults
description: "Return the recommended starter configuration (radius, path/target desired distance, max speed) for a Godot NavigationAgent of the requested dimension. Pure-managed: touches no scene, so it is safe to call any time to discover sane defaults before creating or configuring a real agent. The defaults differ by dimension (2D works in pixels, 3D in metres). 'dimension' accepts '2D' or '3D' (default '3D')."
---

# Navigation / Defaults

Return the recommended starter configuration (radius, path/target desired distance, max speed) for a Godot NavigationAgent of the requested dimension. Pure-managed: touches no scene, so it is safe to call any time to discover sane defaults before creating or configuring a real agent. The defaults differ by dimension (2D works in pixels, 3D in metres). 'dimension' accepts '2D' or '3D' (default '3D').

## How to Call

### HTTP API (Direct Tool Execution)

Execute this tool directly via the MCP Plugin HTTP API:

```bash
curl -X POST http://localhost:23630/api/tools/navigation-defaults \
  -H "Content-Type: application/json" \
  -d '{
  "dimension": "string_value"
}'
```

> For complex input (multi-line strings, code), save the JSON to a file and use `-d @args.json`.
>
> Or pipe via stdin:
> ```bash
> curl -X POST http://localhost:23630/api/tools/navigation-defaults -H "Content-Type: application/json" -d @- <<'EOF'
> {"param": "value"}
> EOF
> ```

#### With Authorization (if required)

```bash
curl -X POST http://localhost:23630/api/tools/navigation-defaults \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer YOUR_TOKEN" \
  -d '{
  "dimension": "string_value"
}'
```

## Input

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `dimension` | `string` | No | Navigation dimension: '2D' (NavigationAgent2D) or '3D' (NavigationAgent3D). Defaults to '3D'. |

### Input JSON Schema

```json
{
  "type": "object",
  "properties": {
    "dimension": {
      "type": "string",
      "description": "Navigation dimension: '2D' (NavigationAgent2D) or '3D' (NavigationAgent3D). Defaults to '3D'."
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

