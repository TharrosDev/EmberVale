---
name: dialogic-defaults
description: "Return the recommended starter configuration for Dialogic authoring: a 2-line starter timeline (plain .dtl text) and a named, colored starter character. Pure-managed: needs no scene and no installed addon, so it is safe to call any time to discover sane defaults before creating real timeline/character resources. 'kind' optionally narrows the result: 'timeline' or 'character' (default: both)."
---

# Dialogic / Defaults

Return the recommended starter configuration for Dialogic authoring: a 2-line starter timeline (plain .dtl text) and a named, colored starter character. Pure-managed: needs no scene and no installed addon, so it is safe to call any time to discover sane defaults before creating real timeline/character resources. 'kind' optionally narrows the result: 'timeline' or 'character' (default: both).

## How to Call

### HTTP API (Direct Tool Execution)

Execute this tool directly via the MCP Plugin HTTP API:

```bash
curl -X POST http://localhost:23630/api/tools/dialogic-defaults \
  -H "Content-Type: application/json" \
  -d '{
  "kind": "string_value"
}'
```

> For complex input (multi-line strings, code), save the JSON to a file and use `-d @args.json`.
>
> Or pipe via stdin:
> ```bash
> curl -X POST http://localhost:23630/api/tools/dialogic-defaults -H "Content-Type: application/json" -d @- <<'EOF'
> {"param": "value"}
> EOF
> ```

#### With Authorization (if required)

```bash
curl -X POST http://localhost:23630/api/tools/dialogic-defaults \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer YOUR_TOKEN" \
  -d '{
  "kind": "string_value"
}'
```

## Input

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `kind` | `string` | No | Optional filter: 'timeline' (only the starter timeline), 'character' (only the starter character), or omit for both. |

### Input JSON Schema

```json
{
  "type": "object",
  "properties": {
    "kind": {
      "type": "string",
      "description": "Optional filter: 'timeline' (only the starter timeline), 'character' (only the starter character), or omit for both."
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
      "$ref": "#/$defs/com.IvanMurzak.Godot.MCP.Dialogic.DialogicResourceInfo"
    }
  },
  "$defs": {
    "com.IvanMurzak.Godot.MCP.Dialogic.DialogicResourceInfo": {
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
        "ResourceKind": {
          "type": "string"
        },
        "ResourcePath": {
          "type": "string"
        },
        "DisplayName": {
          "type": "string"
        },
        "Color": {
          "type": "string"
        },
        "Description": {
          "type": "string"
        },
        "EventCount": {
          "type": "integer"
        },
        "TimelineText": {
          "type": "string"
        }
      },
      "required": [
        "Installed",
        "EventCount"
      ]
    }
  },
  "required": [
    "result"
  ]
}
```

