---
name: dialogic-get
description: "Read a Dialogic timeline (.dtl) or character (.dch) resource and return its scalar config: for a timeline, EventCount + TimelineText; for a character, DisplayName + Color + Description. The kind is chosen by the file extension. Read-only. If the Dialogic addon is not installed, returns Installed:false with an install hint instead of crashing."
---

# Dialogic / Get

Read a Dialogic timeline (.dtl) or character (.dch) resource and return its scalar config: for a timeline, EventCount + TimelineText; for a character, DisplayName + Color + Description. The kind is chosen by the file extension. Read-only. If the Dialogic addon is not installed, returns Installed:false with an install hint instead of crashing.

## How to Call

### HTTP API (Direct Tool Execution)

Execute this tool directly via the MCP Plugin HTTP API:

```bash
curl -X POST http://localhost:23630/api/tools/dialogic-get \
  -H "Content-Type: application/json" \
  -d '{
  "resourcePath": "string_value"
}'
```

> For complex input (multi-line strings, code), save the JSON to a file and use `-d @args.json`.
>
> Or pipe via stdin:
> ```bash
> curl -X POST http://localhost:23630/api/tools/dialogic-get -H "Content-Type: application/json" -d @- <<'EOF'
> {"param": "value"}
> EOF
> ```

#### With Authorization (if required)

```bash
curl -X POST http://localhost:23630/api/tools/dialogic-get \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer YOUR_TOKEN" \
  -d '{
  "resourcePath": "string_value"
}'
```

## Input

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `resourcePath` | `string` | Yes | res:// path of a Dialogic timeline (.dtl) or character (.dch) to read. |

### Input JSON Schema

```json
{
  "type": "object",
  "properties": {
    "resourcePath": {
      "type": "string",
      "description": "res:// path of a Dialogic timeline (.dtl) or character (.dch) to read."
    }
  },
  "required": [
    "resourcePath"
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

