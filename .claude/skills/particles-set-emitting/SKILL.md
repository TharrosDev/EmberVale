---
name: particles-set-emitting
description: Start or stop emission on an existing GpuParticles2D/GpuParticles3D node, addressed by 'nodePath' (relative to the edited scene root). 'emitting' true starts emission, false stops it. Optionally pass 'restart' true to restart the emission from a clean state first (useful for one-shot bursts). Returns the node's updated config.
---

# Particles / Set Emitting

Start or stop emission on an existing GpuParticles2D/GpuParticles3D node, addressed by 'nodePath' (relative to the edited scene root). 'emitting' true starts emission, false stops it. Optionally pass 'restart' true to restart the emission from a clean state first (useful for one-shot bursts). Returns the node's updated config.

## How to Call

### HTTP API (Direct Tool Execution)

Execute this tool directly via the MCP Plugin HTTP API:

```bash
curl -X POST http://localhost:23630/api/tools/particles-set-emitting \
  -H "Content-Type: application/json" \
  -d '{
  "nodePath": "string_value",
  "emitting": false,
  "restart": false
}'
```

> For complex input (multi-line strings, code), save the JSON to a file and use `-d @args.json`.
>
> Or pipe via stdin:
> ```bash
> curl -X POST http://localhost:23630/api/tools/particles-set-emitting -H "Content-Type: application/json" -d @- <<'EOF'
> {"param": "value"}
> EOF
> ```

#### With Authorization (if required)

```bash
curl -X POST http://localhost:23630/api/tools/particles-set-emitting \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer YOUR_TOKEN" \
  -d '{
  "nodePath": "string_value",
  "emitting": false,
  "restart": false
}'
```

## Input

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `nodePath` | `string` | Yes | Node path (relative to the edited scene root) of the GpuParticles node. |
| `emitting` | `boolean` | Yes | True to start emitting, false to stop (Godot 'emitting'). |
| `restart` | `boolean` | No | When true, restart the emission from a clean state before applying 'emitting'. |

### Input JSON Schema

```json
{
  "type": "object",
  "properties": {
    "nodePath": {
      "type": "string",
      "description": "Node path (relative to the edited scene root) of the GpuParticles node."
    },
    "emitting": {
      "type": "boolean",
      "description": "True to start emitting, false to stop (Godot 'emitting')."
    },
    "restart": {
      "type": "boolean",
      "description": "When true, restart the emission from a clean state before applying 'emitting'."
    }
  },
  "required": [
    "nodePath",
    "emitting"
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
      "$ref": "#/$defs/com.IvanMurzak.Godot.MCP.Particles.ParticlesNodeInfo"
    }
  },
  "$defs": {
    "com.IvanMurzak.Godot.MCP.Particles.ParticlesNodeInfo": {
      "type": "object",
      "properties": {
        "NodePath": {
          "type": "string"
        },
        "Dimension": {
          "type": "string"
        },
        "TypeName": {
          "type": "string"
        },
        "Amount": {
          "type": "integer"
        },
        "Lifetime": {
          "type": "number"
        },
        "OneShot": {
          "type": "boolean"
        },
        "Emitting": {
          "type": "boolean"
        },
        "Explosiveness": {
          "type": "number"
        },
        "Randomness": {
          "type": "number"
        },
        "SpeedScale": {
          "type": "number"
        },
        "Preprocess": {
          "type": "number"
        },
        "LocalCoords": {
          "type": "boolean"
        }
      },
      "required": [
        "Amount",
        "Lifetime",
        "OneShot",
        "Emitting",
        "Explosiveness",
        "Randomness",
        "SpeedScale",
        "Preprocess",
        "LocalCoords"
      ]
    }
  },
  "required": [
    "result"
  ]
}
```

