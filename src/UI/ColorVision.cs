using System;

namespace Embervale.UI;

/// <summary>The colour-vision deficiencies the UI can adapt for (Phase 37.5G).</summary>
// APPEND ONLY: the ordinal is persisted in settings.
public enum ColorVisionMode
{
    None,

    /// <summary>Green-weak/blind. The most common deficiency by a wide margin.</summary>
    Deuteranopia,

    /// <summary>Red-weak/blind.</summary>
    Protanopia,

    /// <summary>Blue-yellow. Rare, and the one the ash/ember palette is most exposed to.</summary>
    Tritanopia,
}

/// <summary>
/// Colour-vision adaptation for the UI's semantic palette (Phase 37.5G).
///
/// **This daltonizes; it does not simulate.** Simulation shows a trichromat what a colourblind
/// viewer sees, which is a diagnostic tool and actively the wrong thing to render — it would make
/// the UI *less* distinguishable for the person who needs help. Daltonization does the opposite: it
/// measures the information a deficient viewer loses, then redistributes that error into the
/// channels they can still discriminate, so two colours that would have collapsed together stay
/// apart.
///
/// The pipeline is the standard one: sRGB → linear → LMS cone response → project onto the
/// deficient plane → back to linear RGB, take the error, redistribute, add. Every step is pure and
/// unit-tested, and the test that matters asserts the property rather than the arithmetic — after
/// adaptation, a confusable pair must be *further apart under simulation* than it was before.
///
/// ⚠️ **UI only, never world art.** This is applied in <see cref="UiTheme"/>'s builders and
/// semantic accessors. The world-space users of the same ramps — the item drop glow, the trophy
/// stand tint, spell projectiles, impact flashes — deliberately do not route through it: recolouring
/// the world is a different and much larger decision than recolouring a label, and a fire spell that
/// stops looking like fire is a worse outcome than a hard-to-read chip.
/// </summary>
public static class ColorVision
{
    // --- Colour space -------------------------------------------------------
    // Hunt-Pointer-Estevez LMS transform, the matrix the daltonization literature standardises on.

    private static readonly float[,] RgbToLms =
    {
        { 0.31399022f, 0.63951294f, 0.04649755f },
        { 0.15537241f, 0.75789446f, 0.08670142f },
        { 0.01775239f, 0.10944209f, 0.87256922f },
    };

    private static readonly float[,] LmsToRgb =
    {
        { 5.47221206f, -4.6419601f, 0.16963708f },
        { -1.1252419f, 2.29317094f, -0.1678952f },
        { 0.02980165f, -0.19318073f, 1.16364789f },
    };

    // Projections onto the plane each deficiency collapses colour onto.
    private static readonly float[,] Deuteran =
    {
        { 1f, 0f, 0f },
        { 0.9513092f, 0f, 0.04866992f },
        { 0f, 0f, 1f },
    };

    private static readonly float[,] Protan =
    {
        { 0f, 1.05118294f, -0.05116099f },
        { 0f, 1f, 0f },
        { 0f, 0f, 1f },
    };

    private static readonly float[,] Tritan =
    {
        { 1f, 0f, 0f },
        { 0f, 1f, 0f },
        { -0.86744736f, 1.86727089f, 0f },
    };

    /// <summary>
    /// How the lost error is fed back into channels the viewer can still see. Red-green
    /// deficiencies keep blue intact, so the error is pushed there; tritanopia is the mirror case.
    /// These are the conventional redistribution weights and are deliberately gentle — a stronger
    /// shift separates colours better and makes the palette stop looking like the game.
    /// </summary>
    private static readonly float[,] ShiftRedGreen =
    {
        { 0f, 0f, 0f },
        { 0.7f, 1f, 0f },
        { 0.7f, 0f, 1f },
    };

    private static readonly float[,] ShiftBlueYellow =
    {
        { 1f, 0f, 0.7f },
        { 0f, 1f, 0.7f },
        { 0f, 0f, 0f },
    };

    /// <summary>What <paramref name="color"/> looks like to a viewer with <paramref name="mode"/>.
    /// Diagnostic only — <see cref="Daltonize"/> is what the UI renders.</summary>
    public static Godot.Color Simulate(Godot.Color color, ColorVisionMode mode)
    {
        if (mode == ColorVisionMode.None)
        {
            return color;
        }

        (float r, float g, float b) = (Linear(color.R), Linear(color.G), Linear(color.B));
        (float l, float m, float s) = Apply(RgbToLms, r, g, b);
        (float l2, float m2, float s2) = Apply(Plane(mode), l, m, s);
        (float r2, float g2, float b2) = Apply(LmsToRgb, l2, m2, s2);

        return new Godot.Color(Srgb(r2), Srgb(g2), Srgb(b2), color.A);
    }

    /// <summary>
    /// <paramref name="color"/> adjusted so a viewer with <paramref name="mode"/> can still tell it
    /// apart from the colours it would otherwise collapse into.
    /// </summary>
    public static Godot.Color Daltonize(Godot.Color color, ColorVisionMode mode)
    {
        if (mode == ColorVisionMode.None)
        {
            return color;
        }

        (float r, float g, float b) = (Linear(color.R), Linear(color.G), Linear(color.B));

        // What the viewer loses.
        (float l, float m, float s) = Apply(RgbToLms, r, g, b);
        (float l2, float m2, float s2) = Apply(Plane(mode), l, m, s);
        (float sr, float sg, float sb) = Apply(LmsToRgb, l2, m2, s2);

        // Redistribute that loss into channels they retain.
        float[,] shift = mode == ColorVisionMode.Tritanopia ? ShiftBlueYellow : ShiftRedGreen;
        (float dr, float dg, float db) = Apply(shift, r - sr, g - sg, b - sb);

        return new Godot.Color(Srgb(r + dr), Srgb(g + dg), Srgb(b + db), color.A);
    }

    /// <summary>
    /// Perceptual-ish distance between two colours, used by the tests to assert that adaptation
    /// actually separates a confusable pair. Weighted toward green because luminance is where the
    /// eye discriminates best, and because a pair separated only in blue is barely separated at all.
    /// </summary>
    public static float Distance(Godot.Color a, Godot.Color b)
    {
        float dr = a.R - b.R;
        float dg = a.G - b.G;
        float db = a.B - b.B;
        return MathF.Sqrt((2f * dr * dr) + (4f * dg * dg) + (3f * db * db));
    }

    private static float[,] Plane(ColorVisionMode mode) => mode switch
    {
        ColorVisionMode.Protanopia => Protan,
        ColorVisionMode.Tritanopia => Tritan,
        _ => Deuteran,
    };

    private static (float, float, float) Apply(float[,] m, float x, float y, float z) =>
        ((m[0, 0] * x) + (m[0, 1] * y) + (m[0, 2] * z),
         (m[1, 0] * x) + (m[1, 1] * y) + (m[1, 2] * z),
         (m[2, 0] * x) + (m[2, 1] * y) + (m[2, 2] * z));

    private static float Linear(float channel) =>
        channel <= 0.04045f ? channel / 12.92f : MathF.Pow((channel + 0.055f) / 1.055f, 2.4f);

    private static float Srgb(float linear)
    {
        float clamped = linear < 0f ? 0f : linear > 1f ? 1f : linear;
        return clamped <= 0.0031308f ? clamped * 12.92f : (1.055f * MathF.Pow(clamped, 1f / 2.4f)) - 0.055f;
    }
}
