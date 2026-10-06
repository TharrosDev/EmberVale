using System;
using System.Collections.Generic;

namespace Embervale.Magic.Vfx;

/// <summary>The spell-effect quality tiers, in the order the player reads them (cheapest first).
/// These are also the values <c>Settings.SpellEffects</c> saves (with -1 for "follow the preset"), so
/// the numbers are append-only.</summary>
public enum VfxTier
{
    Performance = 0,
    Low = 1,
    Medium = 2,
    High = 3,
    Ultra = 4,
}

/// <summary>How much of a projectile's trail a tier draws.</summary>
public enum VfxTrail
{
    CoreAndHalo,
    Short,
    Normal,
    Long,
    LongWithSparks,
}

/// <summary>How much of an effect is drawn at a distance from the camera.</summary>
public enum VfxDetail
{
    /// <summary>Everything the recipe asks for.</summary>
    Full,

    /// <summary>Past the full-detail distance: the flare alone.</summary>
    FlareOnly,

    /// <summary>Past one and a half times that distance: nothing spawns.</summary>
    None,
}

/// <summary>What one tier allows. Every number is a ceiling the director spends against.</summary>
/// <param name="ParticleMultiplier">Scales every particle amount (through <c>AmountRatio</c>).</param>
/// <param name="MaxLights">Spell lights alive at once.</param>
/// <param name="ShadowedLights">How many of those may cast shadows.</param>
/// <param name="Distortion">Whether refraction shells draw.</param>
/// <param name="GroundMarks">Scorch, frost and rune decals alive at once; 0 = none.</param>
/// <param name="SecondaryDebris">Whether smoke and debris follow a burst.</param>
/// <param name="DebrisMultiplier">Scales that debris (2 on Ultra, 0 when off).</param>
/// <param name="BoltSegments">Segments in a lightning ribbon.</param>
/// <param name="BoltBranches">Side branches a bolt may fork.</param>
/// <param name="Trail">How much of a projectile's trail draws.</param>
/// <param name="LiveEffects">Effects alive at once before the oldest is recycled.</param>
/// <param name="FullDetailDistance">Metres from the camera inside which an effect is drawn whole.</param>
public readonly record struct VfxBudget(
    float ParticleMultiplier,
    int MaxLights,
    int ShadowedLights,
    bool Distortion,
    int GroundMarks,
    bool SecondaryDebris,
    float DebrisMultiplier,
    int BoltSegments,
    int BoltBranches,
    VfxTrail Trail,
    int LiveEffects,
    float FullDetailDistance);

/// <summary>
/// The spell-effect budget table and the rules that read it. Pure, so the table the design states is
/// a test: which tier a saved setting means, what each tier may spend, how far away an effect thins
/// out, and which effect gives way when the budget is full.
/// </summary>
public static class VfxBudgetRules
{
    /// <summary>The saved value that means "use the graphics preset's tier".</summary>
    public const int FollowPreset = -1;

    /// <summary>Past this multiple of the full-detail distance nothing spawns at all.</summary>
    public const float CullDistanceFactor = 1.5f;

    private static readonly VfxBudget[] Table =
    {
        // particles, lights, shadowed, distortion, marks, debris, debris x, bolt seg/branch, trail, live, distance
        new(0.25f, 2, 0, false, 0, false, 0f, 6, 0, VfxTrail.CoreAndHalo, 12, 25f),      // Performance
        new(0.45f, 3, 0, false, 0, false, 0f, 8, 0, VfxTrail.Short, 20, 35f),            // Low
        new(0.7f, 5, 0, false, 6, true, 1f, 12, 1, VfxTrail.Normal, 32, 50f),            // Medium
        new(1.0f, 8, 0, true, 12, true, 1f, 16, 2, VfxTrail.Long, 48, 70f),              // High
        new(1.5f, 12, 1, true, 24, true, 2f, 24, 3, VfxTrail.LongWithSparks, 64, 90f),   // Ultra
    };

    /// <summary>How many tiers there are.</summary>
    public static int TierCount => Table.Length;

    /// <summary>What a tier may spend.</summary>
    public static VfxBudget For(VfxTier tier) => Table[Math.Clamp((int)tier, 0, Table.Length - 1)];

    /// <summary>
    /// The visual tier a saved <c>Settings.RenderQuality</c> means. ⚠️ That setting's ordinals are not
    /// in visual order (0 Low, 1 Medium, 2 High, 3 Ultra, 4 Performance, appended last because the
    /// numbers were already in settings files), so this is a lookup and never a cast. An unknown
    /// value reads as Medium, as <c>GraphicsMath.ClampTier</c> does.
    /// </summary>
    public static VfxTier FromRenderQuality(int renderQuality) => renderQuality switch
    {
        0 => VfxTier.Low,
        1 => VfxTier.Medium,
        2 => VfxTier.High,
        3 => VfxTier.Ultra,
        4 => VfxTier.Performance,
        _ => VfxTier.Medium,
    };

    /// <summary>The tier in force: the player's own choice (0..4, already in visual order) when there
    /// is one, else the graphics preset's. Anything outside -1..4 follows the preset.</summary>
    public static VfxTier Resolve(int spellEffects, int renderQuality) =>
        spellEffects is >= 0 and <= (int)VfxTier.Ultra
            ? (VfxTier)spellEffects
            : FromRenderQuality(renderQuality);

    /// <summary>The settings dropdown's row for a saved value: 0 is "follow the preset", then the tiers.</summary>
    public static int DropdownIndex(int spellEffects) =>
        spellEffects is >= 0 and <= (int)VfxTier.Ultra ? spellEffects + 1 : 0;

    /// <summary>The value to save for a dropdown row.</summary>
    public static int FromDropdownIndex(int index) => Math.Clamp(index, 0, TierCount) - 1;

    /// <summary>How much of an effect draws <paramref name="distance"/> metres from the camera.</summary>
    public static VfxDetail DetailAt(VfxTier tier, float distance)
    {
        float full = For(tier).FullDetailDistance;
        if (distance <= full)
        {
            return VfxDetail.Full;
        }

        return distance <= full * CullDistanceFactor ? VfxDetail.FlareOnly : VfxDetail.None;
    }

    /// <summary>Whether a new effect needs one recycled first.</summary>
    public static bool OverBudget(VfxTier tier, int liveEffects) => liveEffects >= For(tier).LiveEffects;

    /// <summary>
    /// Which live effect gives way when the budget is full: the oldest one that is not the player's,
    /// and only when every effect is the player's, the oldest of those. -1 for an empty list.
    /// </summary>
    public static int PickRecycle(IReadOnlyList<(double Age, bool Player)> live)
    {
        int best = -1;
        for (int i = 0; i < live.Count; i++)
        {
            if (best < 0 || Older(live[i], live[best]))
            {
                best = i;
            }
        }

        return best;

        static bool Older((double Age, bool Player) a, (double Age, bool Player) b) =>
            a.Player != b.Player ? !a.Player : a.Age > b.Age;
    }
}
