---
name: beehave-defaults
description: "Return the recommended starter SKELETON for a Beehave behaviour tree (root class, tick rate, a top-level composite, and the leaf placeholders to fill in). Pure-managed: touches no scene and needs no addon installed, so it is safe to call any time to discover a sane tree shape before scaffolding the real nodes. 'compact' (default false) is reserved for trimming the guidance note."
---

# Beehave / Defaults

Return the recommended starter SKELETON for a Beehave behaviour tree (root class, tick rate, a top-level composite, and the leaf placeholders to fill in). Pure-managed: touches no scene and needs no addon installed, so it is safe to call any time to discover a sane tree shape before scaffolding the real nodes. 'compact' (default false) is reserved for trimming the guidance note.

## How to Call

### HTTP API (Direct Tool Execution)

Execute this tool directly via the MCP Plugin HTTP API:

```bash
curl -X POST http://localhost:23630/api/tools/beehave-defaults \
  -H "Content-Type: application/json" \
  -d '{
  "compact": false
}'
```

> For complex input (multi-line strings, code), save the JSON to a file and use `-d @args.json`.
>
> Or pipe via stdin:
> ```bash
> curl -X POST http://localhost:23630/api/tools/beehave-defaults -H "Content-Type: application/json" -d @- <<'EOF'
> {"param": "value"}
> EOF
> ```

#### With Authorization (if required)

```bash
curl -X POST http://localhost:23630/api/tools/beehave-defaults \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer YOUR_TOKEN" \
  -d '{
  "compact": false
}'
```

## Input

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `compact` | `boolean` | No | When true, omit the long guidance note from the result. Defaults to false. |

### Input JSON Schema

```json
{
  "type": "object",
  "properties": {
    "compact": {
      "type": "boolean",
      "description": "When true, omit the long guidance note from the result. Defaults to false."
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
      "$ref": "#/$defs/com.IvanMurzak.Godot.MCP.Beehave.BeehaveSkeleton"
    }
  },
  "$defs": {
    "System.Collections.Generic.List(System.String)": {
      "type": "array",
      "items": {
        "type": "string"
      }
    },
    "com.IvanMurzak.Godot.MCP.Beehave.BeehaveSkeleton": {
      "type": "object",
      "properties": {
        "RootClass": {
          "type": "string"
        },
        "TickRate": {
          "type": "integer"
        },
        "CompositeKind": {
          "type": "string"
        },
        "CompositeClass": {
          "type": "string"
        },
        "Leaves": {
          "$ref": "#/$defs/System.Collections.Generic.List(System.String)"
        },
        "Note": {
          "type": "string"
        }
      },
      "required": [
        "TickRate"
      ]
    }
  },
  "required": [
    "result"
  ]
}
```

