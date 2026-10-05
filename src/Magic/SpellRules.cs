using System;

namespace Embervale.Magic;

/// <summary>The windows of a derived cast action, as primitives so the timing rule is unit-testable
/// (a Godot <c>Resource</c> cannot be built in the test project). Fractions are of
/// <see cref="Duration"/>; a Duration of 0 means "the clip decides" (the legacy shape).</summary>
public readonly record struct CastShape(
    float Duration, float FallbackDuration, float ActiveFrom, float ActiveTo, float CancelFrom);

/// <summary>
/// The pure rules of a committed cast (magic upgrade 2026-09). Godot-free, so every number a designer
/// tunes here is pinned by <c>SpellRulesTests</c>; <see cref="SpellcastingComponent"/> only applies them.
/// </summary>
public static class SpellRules
{
    /// <summary>Fraction of the mana a cast interrupted in its wind-up gives back.</summary>
    public const float InterruptRefundFraction = 0.5f;

    /// <summary>Seconds the release window of a derived cast stays open.</summary>
    public const float ReleaseSpanSeconds = 0.08f;

    /// <summary>Wind-up and recovery a spell that authors neither is derived with (the pre-upgrade shape).</summary>
    public const float LegacyInstantWindup = 0.7f * 0.45f;
    public const float LegacySustainedWindup = 0.45f * 0.2f;
    public const float LegacyRecovery = 0.315f;

    /// <summary>Cheapest a shortened Blink can cost, as a fraction of its full mana cost.</summary>
    public const float BlinkMinCostFraction = 0.4f;

    /// <summary>The cast timeline for a spell. Both authored numbers at 0 keeps the legacy shape exactly
    /// (clip-driven, 45% wind-up), so every spell authored before the upgrade casts as it did.</summary>
    public static CastShape Shape(float windupSeconds, float recoverySeconds, bool sustained)
    {
        if (windupSeconds <= 0f && recoverySeconds <= 0f)
        {
            return sustained
                ? new CastShape(0f, 0.45f, 0.2f, 0.3f, 0.35f)
                : new CastShape(0f, 0.7f, 0.45f, 0.55f, 0.7f);
        }

        float windup = windupSeconds > 0f ? windupSeconds : (sustained ? LegacySustainedWindup : LegacyInstantWindup);
        float recovery = recoverySeconds > 0f ? recoverySeconds : LegacyRecovery;
        float total = windup + ReleaseSpanSeconds + recovery;
        return new CastShape(total, total, windup / total, (windup + ReleaseSpanSeconds) / total, 1f);
    }

    /// <summary>The wind-up, in seconds, a cast shape reports (what the telegraph shows).</summary>
    public static float WindupOf(in CastShape shape) => shape.FallbackDuration * shape.ActiveFrom;

    /// <summary>
    /// Whether a cast still in its wind-up (or being held) is dropped this frame. A silence or a stun
    /// always cancels it; a stagger cancels it unless the spell is not <c>Interruptible</c> or the caster
    /// is hyperarmoured (Barkskin).
    /// </summary>
    public static bool Interrupts(bool staggered, bool silenced, bool stunned, bool interruptible, bool hyperarmoured) =>
        silenced || stunned || (staggered && interruptible && !hyperarmoured);

    /// <summary>A caster cannot begin a cast while silenced or stunned.</summary>
    public static bool CanBegin(bool silenced, bool stunned) => !silenced && !stunned;

    /// <summary>What a cast charges: the sheet cost scaled by the region's Weave and by the caster's perk
    /// factor (<c>PerkQuery.Factor</c> of <c>ManaCostMult</c>, already floored by <c>PerkEffectMath</c>).</summary>
    public static float ManaCost(float baseCost, float weaveMultiplier, float perkFactor) =>
        Math.Max(0f, baseCost) * weaveMultiplier * perkFactor;

    /// <summary>Mana returned for a cast interrupted in its wind-up.</summary>
    public static float InterruptRefund(float manaSpent) => Math.Max(0f, manaSpent) * InterruptRefundFraction;

    /// <summary>A health cost is refused rather than killing the caster: it needs strictly more health than it takes.</summary>
    public static bool CanPayHealth(float currentHealth, float cost) => cost <= 0f || currentHealth > cost;

    /// <summary>Damage multiplier for a spell that strips <paramref name="stacks"/> of a status as it lands.</summary>
    public static float ConsumeMultiplier(int stacks, float bonusPerStack) =>
        stacks <= 0 || bonusPerStack <= 0f ? 1f : 1f + (stacks * bonusPerStack);

    /// <summary>Foes a piercing spell passes through: the base, plus the bonus scaled by charge (0..1, rounded).</summary>
    public static int PierceCount(int baseCount, int chargeBonus, float charge)
    {
        float c = charge < 0f ? 0f : charge > 1f ? 1f : charge;
        return Math.Max(0, baseCount) + (int)MathF.Round(Math.Max(0, chargeBonus) * c);
    }

    /// <summary>A full charge extends a status by its authored bonus, clamped at both charge endpoints.</summary>
    public static float StatusDurationMultiplier(float charge, float bonus) =>
        1f + (Math.Clamp(charge, 0f, 1f) * Math.Max(0f, bonus));

    /// <summary>A ground impact directly strikes the actor when its horizontal footprint contains the centre.</summary>
    public static bool IsDirectImpact(float horizontalDistance, float directRadius) =>
        horizontalDistance <= Math.Max(0f, directRadius);

    /// <summary>Earliest parameter where a segment enters a rectangular barrier, including its thickness
    /// and the projectile radius. The volume matches the physical wall, so a near-face collision is a hit.</summary>
    public static float BarrierEntry(
        float x0, float y0, float z0, float x1, float y1, float z1,
        float width, float height, float thickness, float radius)
    {
        float r = Math.Max(0f, radius);
        float enter = 0f;
        float exit = 1f;
        return Slab(x0, x1, -width * 0.5f - r, width * 0.5f + r, ref enter, ref exit) &&
               Slab(y0, y1, -r, height + r, ref enter, ref exit) &&
               Slab(z0, z1, -thickness * 0.5f - r, thickness * 0.5f + r, ref enter, ref exit)
            ? enter : -1f;

        static bool Slab(float from, float to, float min, float max, ref float near, ref float far)
        {
            float delta = to - from;
            if (Math.Abs(delta) < 1e-7f)
            {
                return from >= min && from <= max;
            }

            float a = (min - from) / delta;
            float b = (max - from) / delta;
            near = Math.Max(near, Math.Min(a, b));
            far = Math.Min(far, Math.Max(a, b));
            return near <= far;
        }
    }

    /// <summary>A ground or barrier aim point's horizontal distance from the caster once clamped to the place range.</summary>
    public static float ClampPlaceDistance(float distance, float placeRange) =>
        Math.Clamp(distance, 0f, Math.Max(0f, placeRange));

    /// <summary>Metres a blink or dash actually travels: the full distance, or stopped short of a wall
    /// (<paramref name="hitDistance"/> negative = clear) by <paramref name="margin"/>.</summary>
    public static float TravelDistance(float wanted, float hitDistance, float margin) =>
        hitDistance < 0f ? wanted : Math.Clamp(hitDistance - margin, 0f, wanted);

    /// <summary>Mana for a blink that travels <paramref name="travelled"/> of <paramref name="full"/> metres.</summary>
    public static float BlinkCost(float fullCost, float travelled, float full)
    {
        if (full <= 0f)
        {
            return fullCost;
        }

        float t = Math.Clamp(travelled / full, 0f, 1f);
        return fullCost * (BlinkMinCostFraction + ((1f - BlinkMinCostFraction) * t));
    }

    /// <summary>Hit-stop scale a spell's <c>ImpactWeight</c> (0..1) asks for. 0.5, the default, is the
    /// weight spells already had; the comfort scale is applied downstream by the hit-stop director.</summary>
    public static float HitStopScale(float impactWeight) => Math.Clamp(impactWeight, 0f, 1f) * 2f;

    /// <summary>Camera trauma a landed spell adds to the player's view (0 at weight 0, well under a crit).</summary>
    public static float ShakeTrauma(float impactWeight) => Math.Clamp(impactWeight, 0f, 1f) * 0.24f;

    /// <summary>Whether a crossing at (<paramref name="along"/>, <paramref name="height"/>) lands on a wall
    /// face: within half its width and between its base and top.</summary>
    public static bool OnBarrierFace(float along, float height, float width, float wallHeight) =>
        Math.Abs(along) <= width * 0.5f && height >= 0f && height <= wallHeight;

    /// <summary>Parameter (0..1) at which a segment from signed plane distance <paramref name="d0"/> to
    /// <paramref name="d1"/> crosses the plane, or -1 when it does not.</summary>
    public static float PlaneCrossing(float d0, float d1)
    {
        if ((d0 > 0f && d1 > 0f) || (d0 < 0f && d1 < 0f) || d0 == d1)
        {
            return -1f;
        }

        return d0 / (d0 - d1);
    }
}
