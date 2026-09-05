---
name: beehave-get
description: "Read a Beehave 'BeehaveTree' node addressed by 'nodePath' (relative to the edited scene root): its scalar config (enabled, tick rate, actor, child count) plus a depth-first dump of the tree structure (each descendant's name, Beehave class, path, depth). Read-only. Requires the Beehave addon; returns { Installed: false } with a hint when it is absent."
---

# Beehave / Get

Read a Beehave 'BeehaveTree' node addressed by 'nodePath' (relative to the edited scene root): its scalar config (enabled, tick rate, actor, child count) plus a depth-first dump of the tree structure (each descendant's name, Beehave class, path, depth). Read-only. Requires the Beehave addon; returns { Installed: false } with a hint when it is absent.

## How to Call

### HTTP API (Direct Tool Execution)

Execute this tool directly via the MCP Plugin HTTP API:

```bash
curl -X POST http://localhost:23630/api/tools/beehave-get \
  -H "Content-Type: application/json" \
  -d '{
  "nodePath": "string_value"
}'
```

> For complex input (multi-line strings, code), save the JSON to a file and use `-d @args.json`.
>
> Or pipe via stdin:
> ```bash
> curl -X POST http://localhost:23630/api/tools/beehave-get -H "Content-Type: application/json" -d @- <<'EOF'
> {"param": "value"}
> EOF
> ```

#### With Authorization (if required)

```bash
curl -X POST http://localhost:23630/api/tools/beehave-get \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer YOUR_TOKEN" \
  -d '{
  "nodePath": "string_value"
}'
```

## Input

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `nodePath` | `string` | Yes | Node path (relative to the edited scene root) of the BeehaveTree to read. |

### Input JSON Schema

```json
{
  "type": "object",
  "properties": {
    "nodePath": {
      "type": "string",
      "description": "Node path (relative to the edited scene root) of the BeehaveTree to read."
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

