---
name: beehave-add-decorator
description: "Add a Beehave decorator node under an existing tree or composite in the edited scene. 'kind' is 'inverter' (flips its child's SUCCESS/FAILURE) or 'limiter' (caps how often its child may run). 'parentPath' (relative to the scene root) is the node to add it under (defaults to the scene root). A decorator is expected to have exactly one child — add a composite or leaf under it next. Optional 'name' renames the node. Requires the Beehave addon; returns { Installed: false } with a hint when it is absent."
---

# Beehave / Add Decorator

Add a Beehave decorator node under an existing tree or composite in the edited scene. 'kind' is 'inverter' (flips its child's SUCCESS/FAILURE) or 'limiter' (caps how often its child may run). 'parentPath' (relative to the scene root) is the node to add it under (defaults to the scene root). A decorator is expected to have exactly one child — add a composite or leaf under it next. Optional 'name' renames the node. Requires the Beehave addon; returns { Installed: false } with a hint when it is absent.

## How to Call

### HTTP API (Direct Tool Execution)

Execute this tool directly via the MCP Plugin HTTP API:

```bash
curl -X POST http://localhost:23630/api/tools/beehave-add-decorator \
  -H "Content-Type: application/json" \
  -d '{
  "kind": "string_value",
  "parentPath": "string_value",
  "name": "string_value"
}'
```

> For complex input (multi-line strings, code), save the JSON to a file and use `-d @args.json`.
>
> Or pipe via stdin:
> ```bash
> curl -X POST http://localhost:23630/api/tools/beehave-add-decorator -H "Content-Type: application/json" -d @- <<'EOF'
> {"param": "value"}
> EOF
> ```

#### With Authorization (if required)

```bash
curl -X POST http://localhost:23630/api/tools/beehave-add-decorator \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer YOUR_TOKEN" \
  -d '{
  "kind": "string_value",
  "parentPath": "string_value",
  "name": "string_value"
}'
```

## Input

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `kind` | `string` | Yes | Decorator kind: 'inverter' or 'limiter'. |
| `parentPath` | `string` | No | Node path (relative to the edited scene root) of the parent to add the decorator under (a BeehaveTree or composite). Defaults to the scene root. |
| `name` | `string` | No | Name for the new decorator node. When omitted, Godot's default name is used. |

### Input JSON Schema

```json
{
  "type": "object",
  "properties": {
    "kind": {
      "type": "string",
      "description": "Decorator kind: 'inverter' or 'limiter'."
    },
    "parentPath": {
      "type": "string",
      "description": "Node path (relative to the edited scene root) of the parent to add the decorator under (a BeehaveTree or composite). Defaults to the scene root."
    },
    "name": {
      "type": "string",
      "description": "Name for the new decorator node. When omitted, Godot's default name is used."
    }
  },
  "required": [
    "kind"
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

