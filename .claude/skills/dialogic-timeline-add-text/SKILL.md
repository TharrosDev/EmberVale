---
name: dialogic-timeline-add-text
description: "Append a text event to an existing Dialogic timeline (.dtl). 'resourcePath' is the timeline to extend ('.dtl' appended if missing); 'text' is the new line and 'speaker' (optional) names the speaking character. Returns the structured result (Installed, ResourcePath, EventCount, TimelineText). Errors if the timeline does not exist. If the Dialogic addon is not installed, returns Installed:false with an install hint instead of crashing."
---

# Dialogic / Timeline Add Text

Append a text event to an existing Dialogic timeline (.dtl). 'resourcePath' is the timeline to extend ('.dtl' appended if missing); 'text' is the new line and 'speaker' (optional) names the speaking character. Returns the structured result (Installed, ResourcePath, EventCount, TimelineText). Errors if the timeline does not exist. If the Dialogic addon is not installed, returns Installed:false with an install hint instead of crashing.

## How to Call

### HTTP API (Direct Tool Execution)

Execute this tool directly via the MCP Plugin HTTP API:

```bash
curl -X POST http://localhost:23630/api/tools/dialogic-timeline-add-text \
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
> curl -X POST http://localhost:23630/api/tools/dialogic-timeline-add-text -H "Content-Type: application/json" -d @- <<'EOF'
> {"param": "value"}
> EOF
> ```

#### With Authorization (if required)

```bash
curl -X POST http://localhost:23630/api/tools/dialogic-timeline-add-text \
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
| `resourcePath` | `string` | Yes | res:// path of the existing timeline to extend, e.g. 'res://timelines/intro.dtl'. |
| `text` | `string` | Yes | The text of the event to append. |
| `speaker` | `string` | No | Optional speaking character name. When omitted, the appended line has no speaker. |

### Input JSON Schema

```json
{
  "type": "object",
  "properties": {
    "resourcePath": {
      "type": "string",
      "description": "res:// path of the existing timeline to extend, e.g. 'res://timelines/intro.dtl'."
    },
    "text": {
      "type": "string",
      "description": "The text of the event to append."
    },
    "speaker": {
      "type": "string",
      "description": "Optional speaking character name. When omitted, the appended line has no speaker."
    }
  },
  "required": [
    "resourcePath",
    "text"
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

