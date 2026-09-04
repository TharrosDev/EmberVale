---
name: navigation-agent-configure
description: Update scalar properties of an existing NavigationAgent2D/3D node, addressed by 'nodePath' (relative to the edited scene root). Only the arguments you supply are changed; each numeric value is clamped to a valid range (radius > 0, path/target desired distance >= 0, max speed >= 0). Returns the agent's updated config.
---

# Navigation / Agent Configure

Update scalar properties of an existing NavigationAgent2D/3D node, addressed by 'nodePath' (relative to the edited scene root). Only the arguments you supply are changed; each numeric value is clamped to a valid range (radius > 0, path/target desired distance >= 0, max speed >= 0). Returns the agent's updated config.

## How to Call

### HTTP API (Direct Tool Execution)

Execute this tool directly via the MCP Plugin HTTP API:

```bash
curl -X POST http://localhost:23630/api/tools/navigation-agent-configure \
  -H "Content-Type: application/json" \
  -d '{
  "nodePath": "string_value",
  "radius": "string_value",
  "pathDesiredDistance": "string_value",
  "targetDesiredDistance": "string_value",
  "maxSpeed": "string_value",
  "avoidanceEnabled": "string_value"
}'
```

> For complex input (multi-line strings, code), save the JSON to a file and use `-d @args.json`.
>
> Or pipe via stdin:
> ```bash
> curl -X POST http://localhost:23630/api/tools/navigation-agent-configure -H "Content-Type: application/json" -d @- <<'EOF'
> {"param": "value"}
> EOF
> ```

#### With Authorization (if required)

```bash
curl -X POST http://localhost:23630/api/tools/navigation-agent-configure \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer YOUR_TOKEN" \
  -d '{
  "nodePath": "string_value",
  "radius": "string_value",
  "pathDesiredDistance": "string_value",
  "targetDesiredDistance": "string_value",
  "maxSpeed": "string_value",
  "avoidanceEnabled": "string_value"
}'
```

## Input

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `nodePath` | `string` | Yes | Node path (relative to the edited scene root) of the NavigationAgent to configure. |
| `radius` | `any` | No | New avoidance radius (Godot 'radius'); clamped to > 0. |
| `pathDesiredDistance` | `any` | No | New distance at which the next path point counts as reached (Godot 'path_desired_distance'); clamped to >= 0. |
| `targetDesiredDistance` | `any` | No | New distance at which the final target counts as reached (Godot 'target_desired_distance'); clamped to >= 0. |
| `maxSpeed` | `any` | No | New maximum movement speed used for avoidance (Godot 'max_speed'); clamped to >= 0. |
| `avoidanceEnabled` | `any` | No | New RVO avoidance toggle (Godot 'avoidance_enabled'). |

### Input JSON Schema

```json
{
  "type": "object",
  "properties": {
    "nodePath": {
      "type": "string",
      "description": "Node path (relative to the edited scene root) of the NavigationAgent to configure."
    },
    "radius": {
      "$ref": "#/$defs/System.Single",
      "description": "New avoidance radius (Godot 'radius'); clamped to > 0."
    },
    "pathDesiredDistance": {
      "$ref": "#/$defs/System.Single",
      "description": "New distance at which the next path point counts as reached (Godot 'path_desired_distance'); clamped to >= 0."
    },
    "targetDesiredDistance": {
      "$ref": "#/$defs/System.Single",
      "description": "New distance at which the final target counts as reached (Godot 'target_desired_distance'); clamped to >= 0."
    },
    "maxSpeed": {
      "$ref": "#/$defs/System.Single",
      "description": "New maximum movement speed used for avoidance (Godot 'max_speed'); clamped to >= 0."
    },
    "avoidanceEnabled": {
      "$ref": "#/$defs/System.Boolean",
      "description": "New RVO avoidance toggle (Godot 'avoidance_enabled')."
    }
  },
  "$defs": {
    "System.Single": {
      "type": "number"
    },
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

