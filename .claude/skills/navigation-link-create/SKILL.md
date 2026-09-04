---
name: navigation-link-create
description: Create a NavigationLink (an off-mesh connection between two points, e.g. a jump or a ladder) in the currently edited Godot scene and return its structured config. 'dimension' is '2D' (NavigationLink2D, uses X/Y) or '3D' (NavigationLink3D, uses X/Y/Z), default '3D'. Provide the start and end positions via 'startX/startY/startZ' and 'endX/endY/endZ' (Z is ignored in 2D). Optionally pass 'parentPath' and 'name'. The new node's owner is set to the scene root so it is saved with the scene.
---

# Navigation / Link Create

Create a NavigationLink (an off-mesh connection between two points, e.g. a jump or a ladder) in the currently edited Godot scene and return its structured config. 'dimension' is '2D' (NavigationLink2D, uses X/Y) or '3D' (NavigationLink3D, uses X/Y/Z), default '3D'. Provide the start and end positions via 'startX/startY/startZ' and 'endX/endY/endZ' (Z is ignored in 2D). Optionally pass 'parentPath' and 'name'. The new node's owner is set to the scene root so it is saved with the scene.

## How to Call

### HTTP API (Direct Tool Execution)

Execute this tool directly via the MCP Plugin HTTP API:

```bash
curl -X POST http://localhost:23630/api/tools/navigation-link-create \
  -H "Content-Type: application/json" \
  -d '{
  "dimension": "string_value",
  "name": "string_value",
  "parentPath": "string_value",
  "startX": 0,
  "startY": 0,
  "startZ": 0,
  "endX": 0,
  "endY": 0,
  "endZ": 0
}'
```

> For complex input (multi-line strings, code), save the JSON to a file and use `-d @args.json`.
>
> Or pipe via stdin:
> ```bash
> curl -X POST http://localhost:23630/api/tools/navigation-link-create -H "Content-Type: application/json" -d @- <<'EOF'
> {"param": "value"}
> EOF
> ```

#### With Authorization (if required)

```bash
curl -X POST http://localhost:23630/api/tools/navigation-link-create \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer YOUR_TOKEN" \
  -d '{
  "dimension": "string_value",
  "name": "string_value",
  "parentPath": "string_value",
  "startX": 0,
  "startY": 0,
  "startZ": 0,
  "endX": 0,
  "endY": 0,
  "endZ": 0
}'
```

## Input

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `dimension` | `string` | No | Navigation dimension: '2D' (NavigationLink2D) or '3D' (NavigationLink3D). Defaults to '3D'. |
| `name` | `string` | No | Name for the new node. When omitted, Godot's default name for the type is used. |
| `parentPath` | `string` | No | Node path (relative to the edited scene root) of the parent. When omitted, the node is parented to the scene root. |
| `startX` | `number` | No | Link start position X (Godot 'start_position'). Defaults to 0. |
| `startY` | `number` | No | Link start position Y (Godot 'start_position'). Defaults to 0. |
| `startZ` | `number` | No | Link start position Z (Godot 'start_position', 3D only; ignored in 2D). Defaults to 0. |
| `endX` | `number` | No | Link end position X (Godot 'end_position'). Defaults to 0. |
| `endY` | `number` | No | Link end position Y (Godot 'end_position'). Defaults to 0. |
| `endZ` | `number` | No | Link end position Z (Godot 'end_position', 3D only; ignored in 2D). Defaults to 0. |

### Input JSON Schema

```json
{
  "type": "object",
  "properties": {
    "dimension": {
      "type": "string",
      "description": "Navigation dimension: '2D' (NavigationLink2D) or '3D' (NavigationLink3D). Defaults to '3D'."
    },
    "name": {
      "type": "string",
      "description": "Name for the new node. When omitted, Godot's default name for the type is used."
    },
    "parentPath": {
      "type": "string",
      "description": "Node path (relative to the edited scene root) of the parent. When omitted, the node is parented to the scene root."
    },
    "startX": {
      "type": "number",
      "description": "Link start position X (Godot 'start_position'). Defaults to 0."
    },
    "startY": {
      "type": "number",
      "description": "Link start position Y (Godot 'start_position'). Defaults to 0."
    },
    "startZ": {
      "type": "number",
      "description": "Link start position Z (Godot 'start_position', 3D only; ignored in 2D). Defaults to 0."
    },
    "endX": {
      "type": "number",
      "description": "Link end position X (Godot 'end_position'). Defaults to 0."
    },
    "endY": {
      "type": "number",
      "description": "Link end position Y (Godot 'end_position'). Defaults to 0."
    },
    "endZ": {
      "type": "number",
      "description": "Link end position Z (Godot 'end_position', 3D only; ignored in 2D). Defaults to 0."
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

