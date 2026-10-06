using System;

namespace Embervale.Settings;

/// <summary>
/// The player's per-control departures from a quality preset. Every field has a "follow the preset"
/// sentinel, and that sentinel is also the field's absent-default in <see cref="Settings"/>, so a
/// settings file written before these existed resolves to exactly the preset it named.
/// </summary>
/// <param name="RenderScale">0 = preset; otherwise the 3D resolution scale, 0.5..1.</param>
/// <param name="ScalingMode">-1 = preset; 0 bilinear, 1 FSR 1.0, 2 FSR 2.2.</param>
/// <param name="AntiAliasing">-1 = preset; 0 off, 1 FXAA, 2 MSAA 2x, 3 MSAA 4x, 4 TAA.</param>
/// <param name="ShadowQuality">-1 = preset; 0 off; 1..5 = the shadow bundle of the preset at that
/// position in <see cref="GraphicsMath.UiOrder"/> (1 = Performance … 5 = Ultra).</param>
/// <param name="AmbientOcclusion">-1 = preset; 0 off, 1 on.</param>
/// <param name="VolumetricFog">-1 = preset; 0 off, 1 on.</param>
/// <param name="Glow">-1 = preset; 0 off, 1 on.</param>
public readonly record struct GraphicsOverrides(
    float RenderScale, int ScalingMode, int AntiAliasing, int ShadowQuality,
    int AmbientOcclusion, int VolumetricFog, int Glow)
{
    public static GraphicsOverrides None => new(0f, -1, -1, -1, -1, -1, -1);

    /// <summary>True when any control departs from the preset: the UI then names the preset "Custom".</summary>
    public bool IsCustom => this != None;
}

/// <summary>
/// Pure rules behind the graphics options: how the saved quality int maps to a preset, how an
/// override resolves against one, and what the frame cap is. Godot-free so it is unit-testable.
/// </summary>
public static class GraphicsMath
{
    // ⚠️ SAVED VALUES. These ints are what settings.tres stores, so they are append-only: a new tier
    // takes the next number wherever it sits in the UI. Performance is the lowest tier and is 4.
    public const int Low = 0;
    public const int Medium = 1;
    public const int High = 2;
    public const int Ultra = 3;
    public const int Performance = 4;
    public const int TierCount = 5;

    /// <summary>Preset order as the player reads it, cheapest first. Values are saved tier ints.</summary>
    public static readonly int[] UiOrder = { Performance, Low, Medium, High, Ultra };

    private static readonly string[] Names = { "Low", "Medium", "High", "Ultra", "Performance" };

    public const float MinRenderScale = 0.5f;
    public const float MaxRenderScale = 1f;

    /// <summary>The frame cap a menu falls back to when nothing else paces the frame.</summary>
    public const int MenuFpsCap = 60;

    /// <summary>A saved value outside the known tiers reads as Medium, the class default.</summary>
    public static int ClampTier(int tier) => tier is >= 0 and < TierCount ? tier : Medium;

    /// <summary>The preset's resource file stem under <c>data/rendering/</c>.</summary>
    public static string TierName(int tier) => Names[ClampTier(tier)];

    public static int UiIndexOfTier(int tier) => Array.IndexOf(UiOrder, ClampTier(tier));

    public static int TierFromUiIndex(int index) => UiOrder[Math.Clamp(index, 0, UiOrder.Length - 1)];

    // --- Override resolution ------------------------------------------------

    public static bool Resolve(int flag, bool preset) => flag < 0 ? preset : flag != 0;

    public static int Resolve(int choice, int preset) => choice < 0 ? preset : choice;

    public static float ResolveScale(float scale, float preset) =>
        Math.Clamp(scale <= 0f ? preset : scale, MinRenderScale, MaxRenderScale);

    /// <summary>What to store for a toggle: the sentinel when it matches the preset, so setting a
    /// control back to the preset's value stops the preset reading as Custom.</summary>
    public static int Override(bool value, bool preset) => value == preset ? -1 : value ? 1 : 0;

    public static int Override(int value, int preset) => value == preset ? -1 : value;

    public static float OverrideScale(float value, float preset) =>
        MathF.Abs(value - preset) < 0.005f ? 0f : Math.Clamp(value, MinRenderScale, MaxRenderScale);

    // --- Shadows ------------------------------------------------------------

    /// <summary>The shadow-quality choice a preset shows when nothing overrides it.</summary>
    public static int PresetShadowChoice(int tier) => UiIndexOfTier(tier) + 1;

    public static bool ShadowsEnabled(int shadowQuality) => shadowQuality != 0;

    /// <summary>The tier whose shadow bundle applies. Off still names the active tier so the
    /// non-shadow values read from one resource.</summary>
    public static int ShadowSourceTier(int shadowQuality, int tier) =>
        shadowQuality <= 0 ? ClampTier(tier) : TierFromUiIndex(shadowQuality - 1);

    // --- Frame pacing -------------------------------------------------------

    /// <summary>
    /// The engine frame cap (0 = uncapped). A saved cap is always honoured. With none, a menu that
    /// V-Sync is not pacing either (<paramref name="unpacedMenu"/>) falls back to
    /// <see cref="MenuFpsCap"/>, because it would otherwise run the GPU flat out to draw a static panel.
    /// </summary>
    public static int FpsCap(int saved, bool unpacedMenu)
    {
        int cap = saved < 0 ? 0 : saved;
        return cap == 0 && unpacedMenu ? MenuFpsCap : cap;
    }
}
