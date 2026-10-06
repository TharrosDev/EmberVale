using System;

namespace Embervale.Magic.Vfx;

/// <summary>
/// The sizes and amounts the arcane, nature, necrotic and enemy-spell special cases are held to.
/// Pure, so the bounds that keep a breath or a spell on oneself from covering the frame are pinned
/// by tests and cannot drift: a breath's body is a tongue at the mouth and never the whole wedge,
/// its smoke is a handful of puffs on any tier, its glints stay small and away from the camera, and
/// the two leanest tiers draw the short list.
/// </summary>
public static class VfxArcanaRules
{
    /// <summary>How far a ring on the floor runs from a caster seen from outside (metres).</summary>
    public const float FloorReachOutside = 1.9f;

    /// <summary>
    /// How far it runs when the caster is the first-person player. The floor only enters a level
    /// first-person frame about two and a half metres out, so a ring that stopped at three would be
    /// a sliver under the hotbar; at this reach it sweeps the lower third of the view and is gone.
    /// </summary>
    public const float FloorReachInside = 4.5f;

    /// <summary>The longest and widest a breath's body of flame or ice is ever drawn (metres).</summary>
    public const float GoutMaxLength = 5f;

    public const float GoutMaxGirth = 2f;

    /// <summary>A breath's body is left out when the camera is nearer than this to its far end.</summary>
    public const float GoutClearance = 3f;

    /// <summary>The largest glint down a breath, and how near the camera one may be drawn (metres).</summary>
    public const float GlintMaxRadius = 0.32f;

    public const float GlintMinCameraDistance = 4f;

    /// <summary>The most smoke one tick of a breath may add, against the smoke preset's own amount.
    /// Ticks overlap, so this is what keeps a long breath from becoming a wall of fog.</summary>
    public const float SmokeHazeCap = 0.4f;

    /// <summary>The two tiers that draw the short list: cores, one ring, the first particles.</summary>
    public static bool IsLean(VfxTier tier) => tier <= VfxTier.Low;

    /// <summary>The floor reach of a spell on oneself.</summary>
    public static float FloorReach(bool inside) => inside ? FloorReachInside : FloorReachOutside;

    /// <summary>Length of a breath's body: the near part of the wedge, never all of it.</summary>
    public static float GoutLength(float range) => Math.Clamp(range * 0.4f, 1.5f, GoutMaxLength);

    /// <summary>Width of a breath's body for a wedge that opens at <paramref name="slope"/>
    /// (the tangent of its half angle).</summary>
    public static float GoutGirth(float length, float slope) =>
        Math.Clamp(length * Math.Max(0f, slope) * 1.4f, 0.6f, GoutMaxGirth);

    /// <summary>Glints down a breath's axis each tick.</summary>
    public static int Glints(VfxTier tier) => tier switch
    {
        VfxTier.Performance => 0,
        VfxTier.Low => 1,
        VfxTier.Ultra => 3,
        _ => 2,
    };

    /// <summary>Radius of a glint <paramref name="travelled"/> metres down the wedge.</summary>
    public static float GlintRadius(float travelled, float slope) =>
        Math.Clamp(travelled * Math.Max(0f, slope) * 0.12f, 0.18f, GlintMaxRadius);

    /// <summary>
    /// The amount of haze one tick of a breath hangs in the wedge, against its preset's own: none
    /// where the tier has no second layer, and smoke (which covers what is behind it) capped at
    /// <see cref="SmokeHazeCap"/> whatever the tier.
    /// </summary>
    public static float HazeDensity(in VfxBudget budget, float amount, bool smoke)
    {
        if (!budget.SecondaryDebris || amount <= 0f)
        {
            return 0f;
        }

        float density = budget.ParticleMultiplier * budget.DebrisMultiplier * amount * (smoke ? 0.22f : 0.5f);
        return smoke ? Math.Min(density, SmokeHazeCap) : density;
    }
}
