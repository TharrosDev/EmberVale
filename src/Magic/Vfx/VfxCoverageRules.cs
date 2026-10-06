using System;

namespace Embervale.Magic.Vfx;

/// <summary>
/// The coverage and opacity governor: the one rule every large effect element passes through before
/// it is drawn. A blast is metres across and, close to the camera, a soft glow or a ball of fire that
/// size is the whole frame in one flat colour, which is neither an explosion nor cheap (it is the
/// main fill-rate cost of the layer). So an element's opacity is cut by how much of the frame it
/// covers, its soft glow may only span so much of the frame at all, and the white-hot heart of a
/// flash is capped in size and gone in <see cref="CoreSeconds"/>.
///
/// <para>Pure, so the promise is a test: an element that covers <see cref="ShareLimit"/> of the frame
/// or more is never drawn above <see cref="Faint"/> once its first frames (<see cref="PopSeconds"/>)
/// are over, on any tier, and the lower tiers allow less.</para>
///
/// <para>Who passes through it: a flare's halo, core and rays (<c>VfxFlare</c>), the body of a ball
/// of fire or a breath (<c>VfxShell</c>), and the soft body of a ground disc (<c>VfxDisc</c>). Thin
/// things (a shock ring's front, a rune's lines, a bolt, a rim-lit shell) do not: a line cannot white
/// out a frame.</para>
/// </summary>
public static class VfxCoverageRules
{
    /// <summary>The tangent of half the vertical field of view the estimate assumes (70 degrees).</summary>
    public const float TanHalfFov = 0.7f;

    /// <summary>The frame's width over its height the estimate assumes.</summary>
    public const float Aspect = 16f / 9f;

    /// <summary>The nearest an element is ever assumed to be: closer than this it fills the frame anyway.</summary>
    public const float MinDistance = 0.5f;

    /// <summary>The share of the frame at and past which an element is only ever faint (top tier).</summary>
    public const float MaxShare = 0.35f;

    /// <summary>The same on the Performance tier.</summary>
    public const float PerformanceShare = 0.2f;

    /// <summary>The share of the frame an element may cover at its full opacity.</summary>
    public const float FreeShare = 0.045f;

    /// <summary>The most opacity an element at its tier's share limit is drawn with.</summary>
    public const float Faint = 0.12f;

    /// <summary>Seconds at the start of an element's life in which it may be brighter: the pop.</summary>
    public const float PopSeconds = 0.08f;

    /// <summary>How much brighter the pop may be.</summary>
    public const float PopBoost = 2f;

    /// <summary>Seconds in which the white-hot core of a large flash has decayed to nothing.</summary>
    public const float CoreSeconds = 0.15f;

    /// <summary>The flare radius up to which a core is drawn at its own size and its own pace.</summary>
    public const float SmallCore = 0.6f;

    /// <summary>
    /// How much of the frame's height a sphere of <paramref name="radius"/> metres spans,
    /// <paramref name="distance"/> metres from the camera (1 = top to bottom; it can exceed 1).
    /// </summary>
    public static float Span(float radius, float distance) =>
        MathF.Max(0f, radius) / (MathF.Max(MinDistance, distance) * TanHalfFov);

    /// <summary>The share of the frame (0..1) a disc of that radius at that distance covers.</summary>
    public static float Share(float radius, float distance)
    {
        float span = Span(radius, distance);
        return Math.Clamp(MathF.PI * 0.25f * span * span / Aspect, 0f, 1f);
    }

    /// <summary>The share of the frame past which an element is only ever faint at a tier.</summary>
    public static float ShareLimit(VfxTier tier)
    {
        float t = Math.Clamp((int)tier, 0, (int)VfxTier.Ultra) / (float)(int)VfxTier.Ultra;
        return PerformanceShare + ((MaxShare - PerformanceShare) * t);
    }

    /// <summary>
    /// The opacity multiplier (0..1) for an element covering <paramref name="share"/> of the frame,
    /// <paramref name="age"/> seconds into its life. 1 up to <see cref="FreeShare"/>, falling to
    /// <see cref="Faint"/> at the tier's <see cref="ShareLimit"/> and on toward nothing past it.
    /// </summary>
    public static float Opacity(float share, VfxTier tier, double age = 1d)
    {
        share = Math.Clamp(share, 0f, 1f);
        if (share <= FreeShare)
        {
            return 1f;
        }

        float steep = ((1f / Faint) - 1f) / (ShareLimit(tier) - FreeShare);
        float opacity = 1f / (1f + (steep * (share - FreeShare)));
        if (age >= 0d && age < PopSeconds)
        {
            opacity *= PopBoost;
        }

        return Math.Clamp(opacity, 0f, 1f);
    }

    /// <summary>The opacity multiplier for a soft element of <paramref name="radius"/> metres,
    /// <paramref name="distance"/> metres from the camera.</summary>
    public static float Opacity(float radius, float distance, VfxTier tier, double age) =>
        Opacity(Share(radius, distance), tier, age);

    /// <summary>The most of the frame's height a soft glow may span at a tier. The quad is shrunk to
    /// this, because a quad costs its pixels however faint it is drawn.</summary>
    public static float MaxSpan(VfxTier tier) => tier switch
    {
        VfxTier.Performance => 0.45f,
        VfxTier.Low => 0.6f,
        VfxTier.Medium => 0.8f,
        VfxTier.High => 1f,
        _ => 1.2f,
    };

    /// <summary>A soft glow's radius, shrunk so it spans no more of the frame than its tier allows.</summary>
    public static float ClampRadius(float radius, float distance, VfxTier tier) =>
        MathF.Min(MathF.Max(0f, radius), MaxSpan(tier) * MathF.Max(MinDistance, distance) * TanHalfFov);

    /// <summary>
    /// The radius of a flash's white-hot core for a flare of <paramref name="radius"/> metres. A
    /// small hit's core is the flare; a blast's grows only with the root of its size, so the heart of
    /// a six-metre nova is about a metre across and never a white dome.
    /// </summary>
    public static float CoreRadius(float radius)
    {
        radius = MathF.Max(0f, radius);
        return radius <= SmallCore
            ? radius
            : MathF.Min(radius, SmallCore + (0.3f * MathF.Sqrt(radius - SmallCore)));
    }

    /// <summary>
    /// A one-shot core's alpha <paramref name="age"/> seconds into a flash of <paramref name="life"/>
    /// seconds: a small one fades across its life, a large one is gone within <see cref="CoreSeconds"/>.
    /// </summary>
    public static float CoreAlpha(double age, float life, float radius)
    {
        float span = radius <= SmallCore ? MathF.Max(0.05f, life) : MathF.Min(MathF.Max(0.05f, life), CoreSeconds);
        float t = Math.Clamp((float)(age / span), 0f, 1f);
        float eased = Math.Clamp((t - 0.12f) / 0.88f, 0f, 1f);
        return 1f - (eased * eased * (3f - (2f * eased)));
    }
}
