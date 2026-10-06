using System;

namespace Embervale.UI;

/// <summary>
/// HUD widget widths derived from the width the HUD has to lay out in, replacing the literals each
/// widget carried (vitals 286, tracker 280, compass 460, boss bar 520). Pure.
///
/// Every width is its old literal at the 1280-wide reference and below, so nothing moves at the
/// sizes the HUD was tuned at; a wider layout lets each grow by the same proportion up to a cap.
/// The boss bar is the one that changes at the reference: it is a share of the width with a floor
/// and a ceiling, where the literal overran a narrow screen and looked small on a wide one.
/// </summary>
public static class HudMetrics
{
    /// <summary>The layout width the old literals were tuned at.</summary>
    public const float ReferenceWidth = 1280f;

    public const float VitalsMin = 286f;
    public const float VitalsMax = 380f;
    public const float TrackerMin = 280f;
    public const float TrackerMax = 380f;
    public const float CompassMin = 460f;
    public const float CompassMax = 720f;
    public const float BossBarShare = 0.42f;
    public const float BossBarMin = 420f;
    public const float BossBarMax = 720f;

    public static float VitalsWidth(float layoutWidth) => Scaled(layoutWidth, VitalsMin, VitalsMax);

    public static float TrackerWidth(float layoutWidth) => Scaled(layoutWidth, TrackerMin, TrackerMax);

    public static float CompassWidth(float layoutWidth) => Scaled(layoutWidth, CompassMin, CompassMax);

    public static float BossBarWidth(float layoutWidth) =>
        Clamp(BossBarShare * layoutWidth, BossBarMin, BossBarMax);

    /// <summary>The width the scaled HUD lays out in, in its own units: the viewport less the safe
    /// zone on both sides, divided by the HUD scale.</summary>
    public static float LayoutWidth(float viewportWidth, float hudScale, float safeZone) =>
        hudScale > 0f ? viewportWidth * (1f - 2f * safeZone) / hudScale : viewportWidth;

    /// <summary>
    /// The anchors that make a control scaled by <paramref name="hudScale"/> about its top-left
    /// corner cover its parent less a <paramref name="safeZone"/> fraction on every side. At scale 1
    /// and no safe zone this is the full rect.
    /// </summary>
    public static (float Left, float Top, float Right, float Bottom) ScaledAnchors(float hudScale, float safeZone)
    {
        float scale = hudScale > 0f ? hudScale : 1f;
        float far = safeZone + (1f - 2f * safeZone) / scale;
        return (safeZone, safeZone, far, far);
    }

    /// <summary>
    /// How far above the screen's bottom edge a line sits that the scaled HUD draws
    /// <paramref name="clearance"/> of its own units above its bottom edge. Something outside the
    /// scaled HUD that has to clear the hotbar (the placement strip) anchors here.
    /// </summary>
    public static float ScreenClearance(float clearance, float viewportHeight, float hudScale, float safeZone) =>
        clearance * (hudScale > 0f ? hudScale : 1f) + safeZone * viewportHeight;

    private static float Scaled(float layoutWidth, float atReference, float max) =>
        Clamp(atReference * layoutWidth / ReferenceWidth, atReference, max);

    private static float Clamp(float value, float min, float max) =>
        float.IsFinite(value) ? MathF.Round(Math.Clamp(value, min, max)) : min;
}
