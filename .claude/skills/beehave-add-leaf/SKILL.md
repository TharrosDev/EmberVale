---
name: beehave-add-leaf
description: "Add a Beehave leaf PLACEHOLDER under a composite or decorator in the edited scene. 'kind' is 'action' (an ActionLeaf — does work) or 'condition' (a ConditionLeaf — gates a branch). 'parentPath' (relative to the scene root) is the composite/decorator to add it under (defaults to the scene root). Beehave leaves are ABSTRACT: this creates the base node as a scaffold — attach your own GDScript to implement tick(actor, blackboard) -> SUCCESS|RUNNING|FAILURE. Optional 'name' renames the node. Requires the Beehave addon; returns { Installed: false } with a hint when absent."
---

# Beehave / Add Leaf

Add a Beehave leaf PLACEHOLDER under a composite or decorator in the edited scene. 'kind' is 'action' (an ActionLeaf — does work) or 'condition' (a ConditionLeaf — gates a branch). 'parentPath' (relative to the scene root) is the composite/decorator to add it under (defaults to the scene root). Beehave leaves are ABSTRACT: this creates the base node as a scaffold — attach your own GDScript to implement tick(actor, blackboard) -> SUCCESS|RUNNING|FAILURE. Optional 'name' renames the node. Requires the Beehave addon; returns { Installed: false } with a hint when absent.

## How to Call

### HTTP API (Direct Tool Execution)

Execute this tool directly via the MCP Plugin HTTP API:

```bash
curl -X POST http://localhost:23630/api/tools/beehave-add-leaf \
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
> curl -X POST http://localhost:23630/api/tools/beehave-add-leaf -H "Content-Type: application/json" -d @- <<'EOF'
> {"param": "value"}
> EOF
> ```

#### With Authorization (if required)

```bash
curl -X POST http://localhost:23630/api/tools/beehave-add-leaf \
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
| `kind` | `string` | Yes | Leaf kind: 'action' or 'condition'. |
| `parentPath` | `string` | No | Node path (relative to the edited scene root) of the parent composite/decorator to add the leaf under. Defaults to the scene root. |
| `name` | `string` | No | Name for the new leaf node. When omitted, Godot's default name is used. |

### Input JSON Schema

```json
{
  "type": "object",
  "properties": {
    "kind": {
      "type": "string",
      "description": "Leaf kind: 'action' or 'condition'."
    },
    "parentPath": {
      "type": "string",
      "description": "Node path (relative to the edited scene root) of the parent composite/decorator to add the leaf under. Defaults to the scene root."
    },
    "name": {
      "type": "string",
      "description": "Name for the new leaf node. When omitted, Godot's default name is used."
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

