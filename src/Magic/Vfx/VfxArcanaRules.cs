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

    /// <summary>
    /// The radius of a disc that follows the first-person caster's own feet (a ward's circle, a
    /// bloom, a greened floor). Under the radius at which a ring about the camera stops being cut
    /// (<see cref="VfxScreenRules.SelfRing"/>), so it stays a faint floor and never a lit band
    /// through the hotbar: at four fifths of <see cref="FloorReachInside"/> it was half bright and
    /// filled the bottom of the view for as long as it lasted.
    /// </summary>
    public const float SelfDiscInside = 2.6f;

    /// <summary>The radius of a disc at a self-caster's feet: <paramref name="outside"/> seen from
    /// outside, <see cref="SelfDiscInside"/> from inside their head.</summary>
    public static float SelfDisc(bool inside, float outside) => inside ? SelfDiscInside : outside;

    /// <summary>How many stinging darts circle a swarmed body. None on the leanest tier, where the
    /// status keeps its drift of specks and nothing is added to it.</summary>
    public static int SwarmInsects(VfxTier tier) => tier switch
    {
        VfxTier.Performance => 0,
        VfxTier.Low => 6,
        VfxTier.Medium => 8,
        VfxTier.High => 10,
        _ => 12,
    };

    /// <summary>How many pieces are drawn in to an implosion (a lance landing, bone knitting).</summary>
    public static int ImplosionStreaks(VfxTier tier) => tier switch
    {
        VfxTier.Performance => 5,
        VfxTier.Low => 6,
        VfxTier.Medium => 8,
        VfxTier.High => 10,
        _ => 12,
    };

    /// <summary>One tick in this many of an ash breath lets flakes go. They hang for three or four
    /// seconds and a breath ticks three times a second, so the lean tiers space them out and no
    /// more emitters are alive at once than the embers they replace.</summary>
    public static int AshEvery(VfxTier tier) => tier switch
    {
        VfxTier.Performance => 3,
        VfxTier.Low => 2,
        _ => 1,
    };

    /// <summary>One tick in this many of a fire breath leaves its scorch burning; 0 = never (the
    /// tiers with no scorch to burn on).</summary>
    public static int BurnEvery(VfxTier tier) => tier switch
    {
        VfxTier.Performance or VfxTier.Low => 0,
        VfxTier.Medium => 2,
        _ => 1,
    };

    /// <summary>Whether a one-in-<paramref name="every"/> thing happens on the tick that drew
    /// <paramref name="roll"/>. Never for 0 or less.</summary>
    public static bool Falls(int every, int roll) => every > 0 && (roll & 0x7FFFFFFF) % every == 0;

    /// <summary>How far along a breath (0..1 of its reach) the tick that drew <paramref name="roll"/>
    /// lights the ground: from a fifth of the way to nine tenths.</summary>
    public static float PatchAlong(int roll) => 0.2f + (0.7f * (((roll >> 12) & 0xFFF) / 4095f));

    /// <summary>
    /// Whether a point stands in a breath: <paramref name="along"/> metres down its axis and
    /// <paramref name="off"/> metres to the side of it, for a wedge <paramref name="range"/> long
    /// that opens at <paramref name="slope"/>. Asked of the camera, with a metre of margin: when it
    /// is in the breath's path everything thrown down it is thinned.
    /// </summary>
    public static bool InBreath(float along, float off, float range, float slope) =>
        along > 1f && along < range * 1.15f && off < (along * Math.Max(0f, slope)) + 1f;

    /// <summary>
    /// Whether a breath may be drawn from the breather's mouth bone instead of the rule's own
    /// origin: the mouth is within a few metres of that origin (a bone found on some other part of
    /// a strange rig is not a mouth) and the far end of the wedge is still well ahead of it.
    /// </summary>
    public static bool MouthIsUsable(float mouthToOrigin, float mouthToEnd, float range) =>
        range > 0f && mouthToOrigin <= Math.Max(3f, range * 0.5f) && mouthToEnd >= range * 0.5f;

    /// <summary>The radius of the sigil under a marked body <paramref name="width"/> metres wide.</summary>
    public static float MarkSigilRadius(float width) => Math.Clamp((width * 0.5f) + 0.75f, 0.9f, 2.2f);

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
