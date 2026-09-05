---
name: dialogic-timeline-create
description: "Create a Dialogic timeline resource (.dtl) at the given res:// path, seeded with one text event. '.dtl' is appended to 'resourcePath' if missing. Pass 'text' (and optional 'speaker') for the first line; when 'text' is omitted a 2-line starter timeline is written. Returns the structured result (Installed, ResourcePath, EventCount, TimelineText). If the Dialogic addon is not installed, returns Installed:false with an install hint instead of crashing."
---

# Dialogic / Timeline Create

Create a Dialogic timeline resource (.dtl) at the given res:// path, seeded with one text event. '.dtl' is appended to 'resourcePath' if missing. Pass 'text' (and optional 'speaker') for the first line; when 'text' is omitted a 2-line starter timeline is written. Returns the structured result (Installed, ResourcePath, EventCount, TimelineText). If the Dialogic addon is not installed, returns Installed:false with an install hint instead of crashing.

## How to Call

### HTTP API (Direct Tool Execution)

Execute this tool directly via the MCP Plugin HTTP API:

```bash
curl -X POST http://localhost:23630/api/tools/dialogic-timeline-create \
  -H "Content-Type: application/json" \
  -d '{
  "resourcePath": "string_value",
  "text": "string_value",
  "speaker": "string_value"
}'
```

> For complex input (multi-line strings, code), save the JSON to a file and use `-d @args.json`.
>
> Or pipe via stdin:
> ```bash
> curl -X POST http://localhost:23630/api/tools/dialogic-timeline-create -H "Content-Type: application/json" -d @- <<'EOF'
> {"param": "value"}
> EOF
> ```

#### With Authorization (if required)

```bash
curl -X POST http://localhost:23630/api/tools/dialogic-timeline-create \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer YOUR_TOKEN" \
  -d '{
  "resourcePath": "string_value",
  "text": "string_value",
  "speaker": "string_value"
}'
```

## Input

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `resourcePath` | `string` | Yes | res:// path for the new timeline, e.g. 'res://timelines/intro.dtl' ('.dtl' appended if missing). |
| `text` | `string` | No | Optional text for the first text event. When omitted, a starter timeline is written. |
| `speaker` | `string` | No | Optional speaking character name for the first line. When omitted, the line has no speaker. |

### Input JSON Schema

```json
{
  "type": "object",
  "properties": {
    "resourcePath": {
      "type": "string",
      "description": "res:// path for the new timeline, e.g. 'res://timelines/intro.dtl' ('.dtl' appended if missing)."
    },
    "text": {
      "type": "string",
      "description": "Optional text for the first text event. When omitted, a starter timeline is written."
    },
    "speaker": {
      "type": "string",
      "description": "Optional speaking character name for the first line. When omitted, the line has no speaker."
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

