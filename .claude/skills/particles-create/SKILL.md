---
name: particles-create
description: Create a GpuParticles emitter in the currently edited Godot scene and return its structured config. 'dimension' is '2D' (GpuParticles2D) or '3D' (GpuParticles3D), default '3D'. Optionally pass 'parentPath' (a node path relative to the scene root) to parent it (defaults to the scene root), 'name' to rename it, and 'amount'/'lifetime' to seed its config (both clamped to valid ranges). The new node's owner is set to the scene root so it is saved with the scene.
---

# Particles / Create

Create a GpuParticles emitter in the currently edited Godot scene and return its structured config. 'dimension' is '2D' (GpuParticles2D) or '3D' (GpuParticles3D), default '3D'. Optionally pass 'parentPath' (a node path relative to the scene root) to parent it (defaults to the scene root), 'name' to rename it, and 'amount'/'lifetime' to seed its config (both clamped to valid ranges). The new node's owner is set to the scene root so it is saved with the scene.

## How to Call

### HTTP API (Direct Tool Execution)

Execute this tool directly via the MCP Plugin HTTP API:

```bash
curl -X POST http://localhost:23630/api/tools/particles-create \
  -H "Content-Type: application/json" \
  -d '{
  "dimension": "string_value",
  "name": "string_value",
  "parentPath": "string_value",
  "amount": "string_value",
  "lifetime": "string_value"
}'
```

> For complex input (multi-line strings, code), save the JSON to a file and use `-d @args.json`.
>
> Or pipe via stdin:
> ```bash
> curl -X POST http://localhost:23630/api/tools/particles-create -H "Content-Type: application/json" -d @- <<'EOF'
> {"param": "value"}
> EOF
> ```

#### With Authorization (if required)

```bash
curl -X POST http://localhost:23630/api/tools/particles-create \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer YOUR_TOKEN" \
  -d '{
  "dimension": "string_value",
  "name": "string_value",
  "parentPath": "string_value",
  "amount": "string_value",
  "lifetime": "string_value"
}'
```

## Input

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `dimension` | `string` | No | Particle dimension: '2D' (GpuParticles2D) or '3D' (GpuParticles3D). Defaults to '3D'. |
| `name` | `string` | No | Name for the new node. When omitted, Godot's default name for the type is used. |
| `parentPath` | `string` | No | Node path (relative to the edited scene root) of the parent. When omitted, the node is parented to the scene root. |
| `amount` | `any` | No | Optional initial particle count (Godot 'amount'); clamped to >= 1. |
| `lifetime` | `any` | No | Optional initial particle lifetime in seconds (Godot 'lifetime'); clamped to > 0. |

### Input JSON Schema

```json
{
  "type": "object",
  "properties": {
    "dimension": {
      "type": "string",
      "description": "Particle dimension: '2D' (GpuParticles2D) or '3D' (GpuParticles3D). Defaults to '3D'."
    },
    "name": {
      "type": "string",
      "description": "Name for the new node. When omitted, Godot's default name for the type is used."
    },
    "parentPath": {
      "type": "string",
      "description": "Node path (relative to the edited scene root) of the parent. When omitted, the node is parented to the scene root."
    },
    "amount": {
      "$ref": "#/$defs/System.Int32",
      "description": "Optional initial particle count (Godot 'amount'); clamped to >= 1."
    },
    "lifetime": {
      "$ref": "#/$defs/System.Double",
      "description": "Optional initial particle lifetime in seconds (Godot 'lifetime'); clamped to > 0."
    }
  },
  "$defs": {
    "System.Int32": {
      "type": "integer"
    },
    "System.Double": {
      "type": "number"
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

