shader_type canvas_item;
render_mode unshaded;

// Phase 37.5A — the spellbook's rotating rune diagram.
//
// Belongs on a ColorRect, never on a PanelContainer: a ColorRect's UV is guaranteed to span
// 0..1 over its rect, which this shader's polar maths depends on. A rounded stylebox's UV is
// not, and the ring would drift off centre as the panel resized.
//
// Read: two counter-rotating tick bands between three engraved hairlines. Counter-rotation is
// the whole trick — one band spinning reads as a decal, two reads as a mechanism doing work.
// It is deliberately thin and cold; the brief asks for arcane scholarship, not a light show.
//
// `motion` is driven from UiTheme.MotionEnabled. At 0 the diagram holds its start angle rather
// than vanishing — reduced motion should remove movement, not remove the art.

uniform vec4 ring_color : source_color = vec4(0.62, 0.70, 0.86, 1.0);
uniform float motion : hint_range(0.0, 1.0) = 1.0;
uniform float speed : hint_range(0.0, 0.5) = 0.06;
uniform float tick_count : hint_range(4.0, 64.0) = 24.0;

// Soft fill inside the innermost ring, so the diagram sits on something.
uniform float core_glow : hint_range(0.0, 1.0) = 0.16;

// Overall opacity ceiling. This sits behind readable text — it must never compete.
uniform float intensity : hint_range(0.0, 1.0) = 0.55;

float ring(float r, float radius, float width)
{
    return smoothstep(width, 0.0, abs(r - radius));
}

// A band of radial tick marks between r0 and r1, `count` of them, each `duty` of its slot wide.
float ticks(float a, float r, float r0, float r1, float count, float duty)
{
    float band = smoothstep(0.0, 0.015, r - r0) * smoothstep(0.0, 0.015, r1 - r);
    if (band <= 0.0)
    {
        return 0.0;
    }

    float seg = fract(a / TAU * count);
    float d = min(seg, 1.0 - seg);
    return smoothstep(duty * 0.5, 0.0, d) * band;
}

void fragment()
{
    vec2 p = UV - vec2(0.5);
    float r = length(p);
    float a = atan(p.y, p.x);
    float t = TIME * speed * motion;

    float acc = 0.0;

    // The skeleton: three engraved hairlines plus a companion rule outside the last.
    acc += ring(r, 0.185, 0.0035);
    acc += ring(r, 0.300, 0.0022);
    acc += ring(r, 0.455, 0.0035);
    acc += ring(r, 0.472, 0.0014);

    // Outer band turns one way, inner band the other and faster.
    acc += ticks(a + t * TAU, r, 0.300, 0.455, tick_count, 0.14) * 0.85;
    acc += ticks(a - t * TAU * 1.7, r, 0.185, 0.300, max(4.0, tick_count * 0.5), 0.22) * 0.65;

    // Core: a soft disc under the innermost ring.
    acc += smoothstep(0.185, 0.0, r) * core_glow;

    // Fade everything at the very edge so the diagram dissolves instead of being cropped
    // square by the ColorRect it lives on.
    acc *= smoothstep(0.5, 0.44, r);

    float alpha = clamp(acc, 0.0, 1.0) * ring_color.a * intensity;
    COLOR = vec4(ring_color.rgb, alpha);
}
