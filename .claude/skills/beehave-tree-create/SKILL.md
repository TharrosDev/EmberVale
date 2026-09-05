---
name: beehave-tree-create
description: "Create a Beehave 'BeehaveTree' behaviour-tree root in the currently edited Godot scene and return its structured config. Optionally pass 'parentPath' (a node path relative to the scene root) to parent it (defaults to the scene root — Beehave drives the tree's parent as its actor), 'name' to rename it, 'actorPath' to set the tree's actor explicitly, 'enabled' (default true), and 'tickRate' (ticks every N frames, default 1). Requires the third-party Beehave addon to be installed; when it is not, returns { Installed: false } with an install hint instead of crashing."
---

# Beehave / Create Tree

Create a Beehave 'BeehaveTree' behaviour-tree root in the currently edited Godot scene and return its structured config. Optionally pass 'parentPath' (a node path relative to the scene root) to parent it (defaults to the scene root — Beehave drives the tree's parent as its actor), 'name' to rename it, 'actorPath' to set the tree's actor explicitly, 'enabled' (default true), and 'tickRate' (ticks every N frames, default 1). Requires the third-party Beehave addon to be installed; when it is not, returns { Installed: false } with an install hint instead of crashing.

## How to Call

### HTTP API (Direct Tool Execution)

Execute this tool directly via the MCP Plugin HTTP API:

```bash
curl -X POST http://localhost:23630/api/tools/beehave-tree-create \
  -H "Content-Type: application/json" \
  -d '{
  "name": "string_value",
  "parentPath": "string_value",
  "actorPath": "string_value",
  "enabled": false,
  "tickRate": 0
}'
```

> For complex input (multi-line strings, code), save the JSON to a file and use `-d @args.json`.
>
> Or pipe via stdin:
> ```bash
> curl -X POST http://localhost:23630/api/tools/beehave-tree-create -H "Content-Type: application/json" -d @- <<'EOF'
> {"param": "value"}
> EOF
> ```

#### With Authorization (if required)

```bash
curl -X POST http://localhost:23630/api/tools/beehave-tree-create \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer YOUR_TOKEN" \
  -d '{
  "name": "string_value",
  "parentPath": "string_value",
  "actorPath": "string_value",
  "enabled": false,
  "tickRate": 0
}'
```

## Input

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `name` | `string` | No | Name for the new BeehaveTree node. When omitted, Godot's default name is used. |
| `parentPath` | `string` | No | Node path (relative to the edited scene root) of the parent. When omitted, the tree is parented to the scene root. |
| `actorPath` | `string` | No | Optional node path (relative to the scene root) of the actor the tree drives. When omitted, Beehave defaults the actor to the tree's parent. |
| `enabled` | `boolean` | No | Whether the tree is enabled (ticks). Defaults to true. |
| `tickRate` | `integer` | No | Tick rate — the tree ticks once every N frames. Defaults to 1 (every frame). |

### Input JSON Schema

```json
{
  "type": "object",
  "properties": {
    "name": {
      "type": "string",
      "description": "Name for the new BeehaveTree node. When omitted, Godot's default name is used."
    },
    "parentPath": {
      "type": "string",
      "description": "Node path (relative to the edited scene root) of the parent. When omitted, the tree is parented to the scene root."
    },
    "actorPath": {
      "type": "string",
      "description": "Optional node path (relative to the scene root) of the actor the tree drives. When omitted, Beehave defaults the actor to the tree's parent."
    },
    "enabled": {
      "type": "boolean",
      "description": "Whether the tree is enabled (ticks). Defaults to true."
    },
    "tickRate": {
      "type": "integer",
      "description": "Tick rate — the tree ticks once every N frames. Defaults to 1 (every frame)."
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
      "$ref": "#/$defs/com.IvanMurzak.Godot.MCP.Beehave.BeehaveNodeInfo"
    }
  },
  "$defs": {
    "System.Collections.Generic.List(com.IvanMurzak.Godot.MCP.Beehave.BeehaveChildInfo)": {
      "type": "array",
      "items": {
        "$ref": "#/$defs/com.IvanMurzak.Godot.MCP.Beehave.BeehaveChildInfo"
      }
    },
    "com.IvanMurzak.Godot.MCP.Beehave.BeehaveChildInfo": {
      "type": "object",
      "properties": {
        "Name": {
          "type": "string"
        },
        "ClassName": {
          "type": "string"
        },
        "NodePath": {
          "type": "string"
        },
        "Depth": {
          "type": "integer"
        }
      },
      "required": [
        "Depth"
      ]
    },
    "com.IvanMurzak.Godot.MCP.Beehave.BeehaveNodeInfo": {
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
        "ClassName": {
          "type": "string"
        },
        "ParentPath": {
          "type": "string"
        },
        "Enabled": {
          "type": "boolean"
        },
        "TickRate": {
          "type": "integer"
        },
        "ActorPath": {
          "type": "string"
        },
        "ChildCount": {
          "type": "integer"
        },
        "Children": {
          "$ref": "#/$defs/System.Collections.Generic.List(com.IvanMurzak.Godot.MCP.Beehave.BeehaveChildInfo)"
        }
      },
      "required": [
        "Installed",
        "Enabled",
        "TickRate",
        "ChildCount"
      ]
    }
  },
  "required": [
    "result"
  ]
}
```

