---
name: particles-defaults
description: "Return the recommended starter configuration (amount, lifetime, explosiveness, speed scale, …) for a Godot GpuParticles emitter of the requested dimension. Pure-managed: touches no scene, so it is safe to call any time to discover sane defaults before creating or configuring a real emitter. 'dimension' accepts '2D' or '3D' (default '3D')."
---

# Particles / Defaults

Return the recommended starter configuration (amount, lifetime, explosiveness, speed scale, …) for a Godot GpuParticles emitter of the requested dimension. Pure-managed: touches no scene, so it is safe to call any time to discover sane defaults before creating or configuring a real emitter. 'dimension' accepts '2D' or '3D' (default '3D').

## How to Call

### HTTP API (Direct Tool Execution)

Execute this tool directly via the MCP Plugin HTTP API:

```bash
curl -X POST http://localhost:23630/api/tools/particles-defaults \
  -H "Content-Type: application/json" \
  -d '{
  "dimension": "string_value"
}'
```

> For complex input (multi-line strings, code), save the JSON to a file and use `-d @args.json`.
>
> Or pipe via stdin:
> ```bash
> curl -X POST http://localhost:23630/api/tools/particles-defaults -H "Content-Type: application/json" -d @- <<'EOF'
> {"param": "value"}
> EOF
> ```

#### With Authorization (if required)

```bash
curl -X POST http://localhost:23630/api/tools/particles-defaults \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer YOUR_TOKEN" \
  -d '{
  "dimension": "string_value"
}'
```

## Input

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `dimension` | `string` | No | Particle dimension: '2D' (GpuParticles2D) or '3D' (GpuParticles3D). Defaults to '3D'. |

### Input JSON Schema

```json
{
  "type": "object",
  "properties": {
    "dimension": {
      "type": "string",
      "description": "Particle dimension: '2D' (GpuParticles2D) or '3D' (GpuParticles3D). Defaults to '3D'."
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

