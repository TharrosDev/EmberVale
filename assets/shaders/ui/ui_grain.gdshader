shader_type canvas_item;
render_mode unshaded;

// Phase 37.5A — the material layer under every framed surface.
//
// This is what turns UiTheme's flat StyleBoxFlat fills into scorched parchment and worn
// leather. It deliberately does NOT draw the frame: corners, borders and content margins stay
// the stylebox's job, because Godot already solves rounding and 9-slicing correctly and a
// shader reimplementation would be a worse copy that also has to be told the rect size.
//
// Grain is sampled in SCREEN pixels (FRAGCOORD), not UV. A Control's UV does not reliably span
// its rect once a StyleBoxFlat rounds the corners, and even where it does, UV-space grain
// stretches — a toast and a full-screen panel would show two different fibre densities, which
// is the exact tell that reads as "stretched texture" rather than "material". Screen-space
// keeps one density everywhere, at every UI scale and resolution.
//
// Alpha is never touched: panel transparency belongs to the token (UiTheme.PanelBg.a), and a
// shader that dimmed it would silently defeat the high-contrast accessibility setting.

// How hard the per-pixel tooth bites. The floor of the effect — at 0 the surface is plastic.
uniform float grain_strength : hint_range(0.0, 1.0) = 0.35;

// The directional lay of the fibre (pressed pulp, tanned hide). Stretched along X.
uniform float fibre_strength : hint_range(0.0, 1.0) = 0.18;

// Slow uneven darkening — where the sheet aged badly. The thing that stops the surface
// reading as uniform, at a scale the eye reads as "old" rather than "noisy".
uniform float mottle_strength : hint_range(0.0, 1.0) = 0.22;

// Screen pixels per grain cell. Lower = finer tooth.
uniform float grain_scale : hint_range(1.0, 64.0) = 5.0;

// The colour the lift is tinted toward, so highlights warm rather than grey out.
uniform vec3 weather_tint : source_color = vec3(0.55, 0.44, 0.28);

float hash21(vec2 p)
{
    return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453123);
}

float value_noise(vec2 p)
{
    vec2 i = floor(p);
    vec2 f = fract(p);
    vec2 u = f * f * (3.0 - 2.0 * f);
    return mix(mix(hash21(i), hash21(i + vec2(1.0, 0.0)), u.x),
               mix(hash21(i + vec2(0.0, 1.0)), hash21(i + vec2(1.0, 1.0)), u.x), u.y);
}

float fbm(vec2 p)
{
    float v = 0.0;
    float a = 0.5;
    for (int i = 0; i < 4; i++)
    {
        v += a * value_noise(p);
        p *= 2.03;
        a *= 0.5;
    }
    return v;
}

void fragment()
{
    vec2 px = FRAGCOORD.xy / grain_scale;

    // Fine tooth: unfiltered per-pixel speckle.
    float grain = hash21(floor(FRAGCOORD.xy)) - 0.5;

    // Fibre: noise squashed along X so the surface has a direction to it.
    float fibre = fbm(vec2(px.x * 0.25, px.y * 3.0)) - 0.5;

    // Mottle: broad, slow blotching.
    float mottle = fbm(px * 0.09) - 0.5;

    float lift = grain * grain_strength * 0.16
               + fibre * fibre_strength * 0.10
               + mottle * mottle_strength * 0.22;

    vec3 tinted = mix(vec3(1.0), weather_tint, 0.5);
    COLOR.rgb = clamp(COLOR.rgb + lift * tinted, vec3(0.0), vec3(1.0));
}
