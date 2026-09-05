---
name: particles-get
description: "Read the scalar config (amount, lifetime, one-shot, emitting, explosiveness, randomness, speed scale, preprocess, local coords) of an existing GpuParticles2D/GpuParticles3D node, addressed by 'nodePath' (relative to the edited scene root). Read-only: does not modify the scene."
---

# Particles / Get

Read the scalar config (amount, lifetime, one-shot, emitting, explosiveness, randomness, speed scale, preprocess, local coords) of an existing GpuParticles2D/GpuParticles3D node, addressed by 'nodePath' (relative to the edited scene root). Read-only: does not modify the scene.

## How to Call

### HTTP API (Direct Tool Execution)

Execute this tool directly via the MCP Plugin HTTP API:

```bash
curl -X POST http://localhost:23630/api/tools/particles-get \
  -H "Content-Type: application/json" \
  -d '{
  "nodePath": "string_value"
}'
```

> For complex input (multi-line strings, code), save the JSON to a file and use `-d @args.json`.
>
> Or pipe via stdin:
> ```bash
> curl -X POST http://localhost:23630/api/tools/particles-get -H "Content-Type: application/json" -d @- <<'EOF'
> {"param": "value"}
> EOF
> ```

#### With Authorization (if required)

```bash
curl -X POST http://localhost:23630/api/tools/particles-get \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer YOUR_TOKEN" \
  -d '{
  "nodePath": "string_value"
}'
```

## Input

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `nodePath` | `string` | Yes | Node path (relative to the edited scene root) of the GpuParticles node to read. |

### Input JSON Schema

```json
{
  "type": "object",
  "properties": {
    "nodePath": {
      "type": "string",
      "description": "Node path (relative to the edited scene root) of the GpuParticles node to read."
    }
  },
  "required": [
    "nodePath"
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

