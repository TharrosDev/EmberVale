---
name: navigation-agent-create
description: Create a NavigationAgent (a pathfinding + avoidance helper, usually a child of a moving body) in the currently edited Godot scene and return its structured config. 'dimension' is '2D' (NavigationAgent2D) or '3D' (NavigationAgent3D), default '3D'. Optionally pass 'parentPath' (a node path relative to the scene root) to parent it under a specific node (defaults to the scene root) and 'name' to rename it. Configure it afterwards with navigation-agent-configure.
---

# Navigation / Agent Create

Create a NavigationAgent (a pathfinding + avoidance helper, usually a child of a moving body) in the currently edited Godot scene and return its structured config. 'dimension' is '2D' (NavigationAgent2D) or '3D' (NavigationAgent3D), default '3D'. Optionally pass 'parentPath' (a node path relative to the scene root) to parent it under a specific node (defaults to the scene root) and 'name' to rename it. Configure it afterwards with navigation-agent-configure.

## How to Call

### HTTP API (Direct Tool Execution)

Execute this tool directly via the MCP Plugin HTTP API:

```bash
curl -X POST http://localhost:23630/api/tools/navigation-agent-create \
  -H "Content-Type: application/json" \
  -d '{
  "dimension": "string_value",
  "name": "string_value",
  "parentPath": "string_value"
}'
```

> For complex input (multi-line strings, code), save the JSON to a file and use `-d @args.json`.
>
> Or pipe via stdin:
> ```bash
> curl -X POST http://localhost:23630/api/tools/navigation-agent-create -H "Content-Type: application/json" -d @- <<'EOF'
> {"param": "value"}
> EOF
> ```

#### With Authorization (if required)

```bash
curl -X POST http://localhost:23630/api/tools/navigation-agent-create \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer YOUR_TOKEN" \
  -d '{
  "dimension": "string_value",
  "name": "string_value",
  "parentPath": "string_value"
}'
```

## Input

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `dimension` | `string` | No | Navigation dimension: '2D' (NavigationAgent2D) or '3D' (NavigationAgent3D). Defaults to '3D'. |
| `name` | `string` | No | Name for the new node. When omitted, Godot's default name for the type is used. |
| `parentPath` | `string` | No | Node path (relative to the edited scene root) of the parent node to attach the agent under. When omitted, the agent is parented to the scene root. |

### Input JSON Schema

```json
{
  "type": "object",
  "properties": {
    "dimension": {
      "type": "string",
      "description": "Navigation dimension: '2D' (NavigationAgent2D) or '3D' (NavigationAgent3D). Defaults to '3D'."
    },
    "name": {
      "type": "string",
      "description": "Name for the new node. When omitted, Godot's default name for the type is used."
    },
    "parentPath": {
      "type": "string",
      "description": "Node path (relative to the edited scene root) of the parent node to attach the agent under. When omitted, the agent is parented to the scene root."
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

