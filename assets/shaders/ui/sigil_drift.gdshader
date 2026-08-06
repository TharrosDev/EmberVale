shader_type canvas_item;
render_mode unshaded;

// Phase 37.5A — the spellbook's floating sigil field.
//
// A sparse scatter of small glyph rings drifting upward behind the page, each fading in and
// out on its own phase. Ambient only: `alpha_max` is capped low on purpose, because this sits
// under body text and the brief's rule is elegant and readable over flashy.
//
// ColorRect only — see rune_circle.gdshader for why UV matters here.
//
// Each cell of a grid either holds a sigil or does not, decided by a hash of the cell, so the
// scatter is stable frame to frame instead of boiling. Drift moves the *sampling* grid rather
// than the marks, which is what lets marks leave the top and reappear at the bottom with no
// bookkeeping.

uniform vec4 sigil_color : source_color = vec4(0.62, 0.70, 0.86, 1.0);
uniform float motion : hint_range(0.0, 1.0) = 1.0;
uniform float density : hint_range(2.0, 24.0) = 9.0;
uniform float speed : hint_range(0.0, 0.2) = 0.015;

// Ceiling on any one sigil's opacity. Ambient means ambient.
uniform float alpha_max : hint_range(0.0, 0.5) = 0.10;

// Fraction of grid cells that hold a sigil at all.
uniform float occupancy : hint_range(0.0, 1.0) = 0.38;

float hash21(vec2 p)
{
    return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453123);
}

void fragment()
{
    // Aspect-correct the grid so sigils stay round on a wide panel.
    vec2 uv = UV;
    vec2 grid = vec2(density, density);

    float drift = TIME * speed * motion;
    vec2 p = vec2(uv.x, uv.y + drift) * grid;

    vec2 cell = floor(p);
    vec2 f = fract(p) - 0.5;

    float pick = hash21(cell);
    if (pick > occupancy)
    {
        COLOR = vec4(sigil_color.rgb, 0.0);
    }
    else
    {
        // Per-cell jitter, size and blink phase, all from independent hashes of the cell.
        vec2 jitter = vec2(hash21(cell + 11.0), hash21(cell + 23.0)) - 0.5;
        float radius = mix(0.10, 0.22, hash21(cell + 37.0));
        float phase = hash21(cell + 53.0) * TAU;

        float d = length(f - jitter * 0.5);

        // A thin ring, not a dot — closer to a written mark than a particle.
        float mark = smoothstep(0.035, 0.0, abs(d - radius));

        // Slow breathe. Held still (but still visible) under reduced motion.
        float blink = mix(0.65, 1.0, 0.5 + 0.5 * sin(TIME * 0.6 * motion + phase));

        COLOR = vec4(sigil_color.rgb, mark * blink * alpha_max * sigil_color.a);
    }
}
