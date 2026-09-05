---
name: particles-configure
description: Update scalar properties of an existing GpuParticles2D/GpuParticles3D node, addressed by 'nodePath' (relative to the edited scene root). Only the arguments you supply are changed; each is clamped to a valid range (amount >= 1, lifetime > 0, explosiveness/randomness in 0..1, speed scale and preprocess >= 0). Returns the node's updated config.
---

# Particles / Configure

Update scalar properties of an existing GpuParticles2D/GpuParticles3D node, addressed by 'nodePath' (relative to the edited scene root). Only the arguments you supply are changed; each is clamped to a valid range (amount >= 1, lifetime > 0, explosiveness/randomness in 0..1, speed scale and preprocess >= 0). Returns the node's updated config.

## How to Call

### HTTP API (Direct Tool Execution)

Execute this tool directly via the MCP Plugin HTTP API:

```bash
curl -X POST http://localhost:23630/api/tools/particles-configure \
  -H "Content-Type: application/json" \
  -d '{
  "nodePath": "string_value",
  "amount": "string_value",
  "lifetime": "string_value",
  "oneShot": "string_value",
  "explosiveness": "string_value",
  "randomness": "string_value",
  "speedScale": "string_value",
  "preprocess": "string_value",
  "localCoords": "string_value"
}'
```

> For complex input (multi-line strings, code), save the JSON to a file and use `-d @args.json`.
>
> Or pipe via stdin:
> ```bash
> curl -X POST http://localhost:23630/api/tools/particles-configure -H "Content-Type: application/json" -d @- <<'EOF'
> {"param": "value"}
> EOF
> ```

#### With Authorization (if required)

```bash
curl -X POST http://localhost:23630/api/tools/particles-configure \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer YOUR_TOKEN" \
  -d '{
  "nodePath": "string_value",
  "amount": "string_value",
  "lifetime": "string_value",
  "oneShot": "string_value",
  "explosiveness": "string_value",
  "randomness": "string_value",
  "speedScale": "string_value",
  "preprocess": "string_value",
  "localCoords": "string_value"
}'
```

## Input

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `nodePath` | `string` | Yes | Node path (relative to the edited scene root) of the GpuParticles node to configure. |
| `amount` | `any` | No | New particle count (Godot 'amount'); clamped to >= 1. |
| `lifetime` | `any` | No | New particle lifetime in seconds (Godot 'lifetime'); clamped to > 0. |
| `oneShot` | `any` | No | New one-shot flag (Godot 'one_shot'): emit a single burst then stop. |
| `explosiveness` | `any` | No | New emission burst ratio (Godot 'explosiveness'); clamped to 0..1. |
| `randomness` | `any` | No | New emission timing randomness (Godot 'randomness'); clamped to 0..1. |
| `speedScale` | `any` | No | New simulation speed multiplier (Godot 'speed_scale'); clamped to >= 0. |
| `preprocess` | `any` | No | New pre-simulation seconds applied on start (Godot 'preprocess'); clamped to >= 0. |
| `localCoords` | `any` | No | New local-space simulation flag (Godot 'local_coords'). |

### Input JSON Schema

```json
{
  "type": "object",
  "properties": {
    "nodePath": {
      "type": "string",
      "description": "Node path (relative to the edited scene root) of the GpuParticles node to configure."
    },
    "amount": {
      "$ref": "#/$defs/System.Int32",
      "description": "New particle count (Godot 'amount'); clamped to >= 1."
    },
    "lifetime": {
      "$ref": "#/$defs/System.Double",
      "description": "New particle lifetime in seconds (Godot 'lifetime'); clamped to > 0."
    },
    "oneShot": {
      "$ref": "#/$defs/System.Boolean",
      "description": "New one-shot flag (Godot 'one_shot'): emit a single burst then stop."
    },
    "explosiveness": {
      "$ref": "#/$defs/System.Single",
      "description": "New emission burst ratio (Godot 'explosiveness'); clamped to 0..1."
    },
    "randomness": {
      "$ref": "#/$defs/System.Single",
      "description": "New emission timing randomness (Godot 'randomness'); clamped to 0..1."
    },
    "speedScale": {
      "$ref": "#/$defs/System.Double",
      "description": "New simulation speed multiplier (Godot 'speed_scale'); clamped to >= 0."
    },
    "preprocess": {
      "$ref": "#/$defs/System.Double",
      "description": "New pre-simulation seconds applied on start (Godot 'preprocess'); clamped to >= 0."
    },
    "localCoords": {
      "$ref": "#/$defs/System.Boolean",
      "description": "New local-space simulation flag (Godot 'local_coords')."
    }
  },
  "$defs": {
    "System.Int32": {
      "type": "integer"
    },
    "System.Double": {
      "type": "number"
    },
    "System.Boolean": {
      "type": "boolean"
    },
    "System.Single": {
      "type": "number"
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

