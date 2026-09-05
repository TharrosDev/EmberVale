---
name: beehave-add-composite
description: "Add a Beehave composite node under an existing tree or composite in the edited scene. 'kind' is 'selector' (runs children until one does NOT fail) or 'sequence' (runs children until one fails). 'parentPath' (relative to the scene root) is the node to add it under — typically a BeehaveTree or another composite (defaults to the scene root). Optional 'name' renames the node. Requires the Beehave addon; returns { Installed: false } with a hint when it is absent."
---

# Beehave / Add Composite

Add a Beehave composite node under an existing tree or composite in the edited scene. 'kind' is 'selector' (runs children until one does NOT fail) or 'sequence' (runs children until one fails). 'parentPath' (relative to the scene root) is the node to add it under — typically a BeehaveTree or another composite (defaults to the scene root). Optional 'name' renames the node. Requires the Beehave addon; returns { Installed: false } with a hint when it is absent.

## How to Call

### HTTP API (Direct Tool Execution)

Execute this tool directly via the MCP Plugin HTTP API:

```bash
curl -X POST http://localhost:23630/api/tools/beehave-add-composite \
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
> curl -X POST http://localhost:23630/api/tools/beehave-add-composite -H "Content-Type: application/json" -d @- <<'EOF'
> {"param": "value"}
> EOF
> ```

#### With Authorization (if required)

```bash
curl -X POST http://localhost:23630/api/tools/beehave-add-composite \
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
| `kind` | `string` | Yes | Composite kind: 'selector' or 'sequence'. |
| `parentPath` | `string` | No | Node path (relative to the edited scene root) of the parent to add the composite under (a BeehaveTree or another composite). Defaults to the scene root. |
| `name` | `string` | No | Name for the new composite node. When omitted, Godot's default name is used. |

### Input JSON Schema

```json
{
  "type": "object",
  "properties": {
    "kind": {
      "type": "string",
      "description": "Composite kind: 'selector' or 'sequence'."
    },
    "parentPath": {
      "type": "string",
      "description": "Node path (relative to the edited scene root) of the parent to add the composite under (a BeehaveTree or another composite). Defaults to the scene root."
    },
    "name": {
      "type": "string",
      "description": "Name for the new composite node. When omitted, Godot's default name is used."
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

