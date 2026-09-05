---
name: dialogic-character-create
description: "Create a Dialogic character resource (.dch) at the given res:// path. '.dch' is appended to 'resourcePath' if missing. 'displayName' is required; 'color' is an optional '#rrggbb' hex (default a friendly blue) and 'description' is optional. Returns the structured result (Installed, ResourcePath, DisplayName, Color, Description). If the Dialogic addon is not installed, returns Installed:false with an install hint instead of crashing."
---

# Dialogic / Character Create

Create a Dialogic character resource (.dch) at the given res:// path. '.dch' is appended to 'resourcePath' if missing. 'displayName' is required; 'color' is an optional '#rrggbb' hex (default a friendly blue) and 'description' is optional. Returns the structured result (Installed, ResourcePath, DisplayName, Color, Description). If the Dialogic addon is not installed, returns Installed:false with an install hint instead of crashing.

## How to Call

### HTTP API (Direct Tool Execution)

Execute this tool directly via the MCP Plugin HTTP API:

```bash
curl -X POST http://localhost:23630/api/tools/dialogic-character-create \
  -H "Content-Type: application/json" \
  -d '{
  "resourcePath": "string_value",
  "displayName": "string_value",
  "color": "string_value",
  "description": "string_value"
}'
```

> For complex input (multi-line strings, code), save the JSON to a file and use `-d @args.json`.
>
> Or pipe via stdin:
> ```bash
> curl -X POST http://localhost:23630/api/tools/dialogic-character-create -H "Content-Type: application/json" -d @- <<'EOF'
> {"param": "value"}
> EOF
> ```

#### With Authorization (if required)

```bash
curl -X POST http://localhost:23630/api/tools/dialogic-character-create \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer YOUR_TOKEN" \
  -d '{
  "resourcePath": "string_value",
  "displayName": "string_value",
  "color": "string_value",
  "description": "string_value"
}'
```

## Input

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `resourcePath` | `string` | Yes | res:// path for the new character, e.g. 'res://characters/hero.dch' ('.dch' appended if missing). |
| `displayName` | `string` | Yes | The character's display name (required). |
| `color` | `string` | No | Optional color as '#rrggbb' hex (e.g. '#e91e63'). Invalid/empty falls back to a default blue. |
| `description` | `string` | No | Optional character description. |

### Input JSON Schema

```json
{
  "type": "object",
  "properties": {
    "resourcePath": {
      "type": "string",
      "description": "res:// path for the new character, e.g. 'res://characters/hero.dch' ('.dch' appended if missing)."
    },
    "displayName": {
      "type": "string",
      "description": "The character's display name (required)."
    },
    "color": {
      "type": "string",
      "description": "Optional color as '#rrggbb' hex (e.g. '#e91e63'). Invalid/empty falls back to a default blue."
    },
    "description": {
      "type": "string",
      "description": "Optional character description."
    }
  },
  "required": [
    "resourcePath",
    "displayName"
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

