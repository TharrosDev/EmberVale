shader_type canvas_item;
render_mode unshaded;

// Phase 37.5A — the illuminated-heading highlight.
//
// A single soft band that sweeps across a heading every `period` seconds, the way light moves
// over gold leaf when the page tilts. Rationed hard: this belongs on the spellbook's school
// heading and on the main menu title, and nowhere else. If every header shimmered, the effect
// would read as a loading state.
//
// ColorRect, sat behind or over the label. Under reduced motion the sweep parks off-screen and
// the band never appears — unlike the rune circle, a *travelling* highlight has nothing
// meaningful to show frozen, so it simply stops.

uniform vec4 shimmer_color : source_color = vec4(0.85, 0.64, 0.25, 1.0);
uniform float motion : hint_range(0.0, 1.0) = 1.0;

// Seconds between sweeps. Long by default — this is punctuation, not a pulse.
uniform float period : hint_range(1.0, 30.0) = 7.0;

// Fraction of the width the band occupies.
uniform float band_width : hint_range(0.02, 0.6) = 0.16;

// Shear, so the band rakes rather than crossing dead vertical.
uniform float skew : hint_range(-1.0, 1.0) = 0.35;

uniform float intensity : hint_range(0.0, 1.0) = 0.5;

void fragment()
{
    if (motion <= 0.0)
    {
        COLOR = vec4(shimmer_color.rgb, 0.0);
    }
    else
    {
        // Sweep travels from -band_width to 1+band_width so it enters and leaves cleanly.
        float t = fract(TIME / period);
        float head = mix(-band_width, 1.0 + band_width, t);

        float x = UV.x + (UV.y - 0.5) * skew;
        float d = abs(x - head) / band_width;

        // Soft-edged band; squared so the falloff is gentle rather than a hard stripe.
        float band = smoothstep(1.0, 0.0, d);
        band *= band;

        // Vertical taper so the band is strongest through the middle of the text.
        band *= smoothstep(0.0, 0.25, UV.y) * smoothstep(1.0, 0.75, UV.y);

        COLOR = vec4(shimmer_color.rgb, band * intensity * shimmer_color.a);
    }
}
