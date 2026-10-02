namespace Embervale.Combat;

/// <summary>Why a body is currently a critical opening. Runtime only — never persisted.</summary>
public enum OpenCause
{
    None = 0,

    /// <summary>Its poise broke into a stagger, heavy stagger or knockdown.</summary>
    PoiseBreak = 1,

    /// <summary>It was parried (a good or a late-good one).</summary>
    Parry = 2,

    /// <summary>Its guard gave out.</summary>
    GuardBreak = 3,

    /// <summary>It was parried on the very first frames of the guard. The biggest riposte.</summary>
    PerfectParry = 4,

    /// <summary>It is in the committed tail of its own heavy or plunge swing: the big blow is paid for.</summary>
    Recovery = 5,
}

/// <summary>Where an attacker sits relative to a raised guard.</summary>
public enum GuardZone
{
    /// <summary>Outside the arc: the guard does nothing.</summary>
    Outside = 0,

    /// <summary>The flank ring: the guard still works but is weaker, and cannot parry.</summary>
    Flank = 1,

    /// <summary>Squarely in front: full mitigation, and the only place a parry can happen.</summary>
    Front = 2,
}

/// <summary>What one kind of blow does to a defender, as multipliers on the attacker's authored numbers.</summary>
public readonly record struct BlowProfile(
    float DamageMultiplier,
    float PoiseMultiplier,
    float GuardPressure,
    bool CrushesGuard,
    bool Parryable);

/// <summary>
/// The defender-side rules of the damage pipeline that are not arithmetic on stats: what each
/// <see cref="HitKind"/> does, how a guard is judged and paid for, and when a blow is a critical
/// opening. Godot-free and pure, so every number a designer tunes here is pinned by a test, and
/// <see cref="CombatComponent"/> stays orchestration.
///
/// <para><b>The one contract with attackers:</b> an attacker stamps <see cref="DamagePacket.Kind"/> and
/// <see cref="DamagePacket.Charge"/> and leaves the consequences to this class. It must NOT pre-scale
/// Amount or PoiseDamage by the charge or the kind, or the blow counts twice.</para>
/// </summary>
public static class DefenceRules
{
    // --- Blow kinds --------------------------------------------------------------------------

    /// <summary>A charged blow at or past this fraction crushes a raised guard outright.</summary>
    public const float CrushCharge = 0.9f;

    /// <summary>What a blow of this kind and charge does. Charge is clamped to 0..1.</summary>
    public static BlowProfile Profile(HitKind kind, float charge)
    {
        float c = charge < 0f ? 0f : charge > 1f ? 1f : charge;
        return kind switch
        {
            // A committed two-hander is a bigger ask of a guard and of a poise bar; its damage is
            // whatever the weapon authored, so it is not rescaled here.
            HitKind.Heavy => new BlowProfile(1f, 1.5f, 2f, false, true),

            // The wind-up is the whole trade: +75% damage, double poise, triple guard pressure at
            // full charge, and a full charge cannot be held against at all.
            HitKind.Charged => new BlowProfile(
                1f + (0.75f * c), 1f + c, 1f + (2f * c), c >= CrushCharge, true),

            // Falling weight: not a thing a raised guard stops.
            HitKind.Plunge => new BlowProfile(1f, 1.5f, 2.5f, true, true),

            // A shaft or a bolt is easy to hold a shield against, and there is nobody to riposte.
            HitKind.Ranged => new BlowProfile(1f, 1f, 0.75f, false, false),
            // Never parryable. A fully charged spell (Sunfall, a drawn Flame Lance) is more than a guard
            // holds: pressure rises with the charge and a full one crushes it, like a charged swing.
            HitKind.Spell => new BlowProfile(1f, 1f, 1f + c, c >= CrushCharge, false),

            _ => new BlowProfile(1f, 1f, 1f, false, true),
        };
    }

    // --- The guard ---------------------------------------------------------------------------

    /// <summary>Fraction of the guard arc, from dead ahead, that counts as the front (full strength).</summary>
    public const float FrontFraction = 0.7f;

    /// <summary>Mitigation multiplier for a blow that lands in the flank ring.</summary>
    public const float FlankMitigationFactor = 0.6f;

    /// <summary>Which zone of the guard an attacker at <paramref name="bearingDegrees"/> (0 = dead ahead,
    /// 180 = behind) sits in, for a guard of half-width <paramref name="halfArcDegrees"/>.</summary>
    public static GuardZone ZoneOf(float bearingDegrees, float halfArcDegrees)
    {
        float arc = halfArcDegrees < 1f ? 1f : halfArcDegrees > 360f ? 360f : halfArcDegrees;
        float bearing = bearingDegrees < 0f ? -bearingDegrees : bearingDegrees;
        if (bearing > arc)
        {
            return GuardZone.Outside;
        }

        return bearing <= arc * FrontFraction ? GuardZone.Front : GuardZone.Flank;
    }

    /// <summary>The mitigation a block gives in this zone (a flank block is weaker, never negative).</summary>
    public static float ZoneMitigation(float blockMitigation, GuardZone zone)
    {
        float m = blockMitigation < 0f ? 0f : blockMitigation > 1f ? 1f : blockMitigation;
        return zone switch
        {
            GuardZone.Front => m,
            GuardZone.Flank => m * FlankMitigationFactor,
            _ => 0f,
        };
    }

    /// <summary>Poise damage a weapon of about this weight is the reference for guard cost.</summary>
    public const float ReferencePoise = 25f;

    /// <summary>
    /// Stamina a held guard pays for one blow: the body's base cost, scaled by the kind's pressure and
    /// by how heavy the blow is against a sword-weight reference. A dagger stab is cheap to hold
    /// against and a maul is not, which is the weight the brief asks for.
    /// </summary>
    public static float GuardStaminaCost(float baseCost, float pressure, float poiseDamage)
    {
        float weight = poiseDamage / ReferencePoise;
        weight = weight < 0.6f ? 0.6f : weight > 2f ? 2f : weight;
        return (baseCost < 0f ? 0f : baseCost) * (pressure < 0f ? 0f : pressure) * weight;
    }

    /// <summary>Below this stamina fraction a held guard is tired and bleeds poise faster.</summary>
    public const float TiredGuardKnee = 0.3f;

    /// <summary>
    /// The poise factor of a blocked blow, raised while the defender is out of breath: from
    /// <paramref name="baseFactor"/> at the knee up to double at empty stamina. A guard held to
    /// exhaustion is one blow from breaking, not merely one blow from unable to block again.
    /// </summary>
    public static float BlockPoiseFactor(float baseFactor, float staminaNormalized)
    {
        float f = baseFactor < 0f ? 0f : baseFactor;
        float s = staminaNormalized < 0f ? 0f : staminaNormalized > 1f ? 1f : staminaNormalized;
        if (s >= TiredGuardKnee)
        {
            return f;
        }

        return f * (1f + (1f - (s / TiredGuardKnee)));
    }

    /// <summary>How long a broken guard staggers this body, from the authored base seconds. Small
    /// bodies are thrown further off balance; large ones and bosses less.</summary>
    public static float GuardBreakSeconds(ReactionClass body, float baseSeconds)
    {
        float scale = body switch
        {
            ReactionClass.Small => 1.3f,
            ReactionClass.Armored => 1.1f,
            ReactionClass.Large => 0.8f,
            ReactionClass.Boss => 0.6f,
            _ => 1f,
        };
        return (baseSeconds < 0f ? 0f : baseSeconds) * scale;
    }

    /// <summary>What a broken guard does to the body — a heavy stagger, softened for big bodies.
    /// Never a knockdown for anything but the smallest, and never off its feet for a boss.</summary>
    public static StaggerResponse GuardBreakResponse(ReactionClass body) => body switch
    {
        ReactionClass.Small => StaggerResponse.Knockdown,
        ReactionClass.Large => StaggerResponse.Stagger,
        ReactionClass.Boss => StaggerResponse.Stagger,
        _ => StaggerResponse.Heavy,
    };

    /// <summary>Scale on the stagger a parry puts on an attacker of this class. A boss gets a
    /// shorter window than a goblin; poise stays the boss's real opening.</summary>
    public static float ParryStaggerScale(ReactionClass body) => body switch
    {
        ReactionClass.Small => 1.2f,
        ReactionClass.Large => 0.8f,
        ReactionClass.Boss => 0.65f,
        _ => 1f,
    };

    // --- Criticals ---------------------------------------------------------------------------

    /// <summary>A blow is "from behind" at or past this bearing (0 = defender's front, 180 = back).</summary>
    public const float BackstabBearingDegrees = 120f;

    /// <summary>Extra seconds an opening outlives the stagger that made it: a riposte a hair late
    /// still lands as one.</summary>
    public const float OpeningGraceSeconds = 0.25f;

    /// <summary>Is this bearing far enough round the back to count as a backstab?</summary>
    public static bool IsBehind(float bearingDegrees) => bearingDegrees >= BackstabBearingDegrees;

    /// <summary>
    /// Whether this blow lands as a critical opening, and which. A body that is open (parried, guard
    /// broken, poise broken) takes a <see cref="HitKind.Riposte"/>; one struck from behind takes a
    /// <see cref="HitKind.Backstab"/>. <see cref="HitKind.Normal"/> means no opening.
    ///
    /// <para>A stamped Riposte is honoured only against an open target, so an attacker cannot claim
    /// one; a stamped Backstab is honoured, since the attacker judged the geometry itself. Arrows
    /// and spells never open anything. And the player's own side is never critically opened: an
    /// enemy pack behind you is a problem of numbers, not a hidden multiplier you cannot see.</para>
    /// </summary>
    public static HitKind ResolveOpening(HitKind stamped, bool targetOpen, bool fromBehind, bool canBeOpened)
    {
        if (!canBeOpened || stamped is HitKind.Ranged or HitKind.Spell)
        {
            return HitKind.Normal;
        }

        if (targetOpen)
        {
            return HitKind.Riposte;
        }

        return stamped == HitKind.Backstab || fromBehind ? HitKind.Backstab : HitKind.Normal;
    }

    /// <summary>The riposte multiplier for the way the target was opened.</summary>
    public static float RiposteBonus(OpenCause cause) => cause switch
    {
        OpenCause.PoiseBreak => 1.35f,
        OpenCause.GuardBreak => 1.75f,
        OpenCause.Parry => 2f,
        OpenCause.PerfectParry => 2.5f,
        OpenCause.Recovery => 1.25f,
        _ => 1f,
    };

    /// <summary>The backstab multiplier.</summary>
    public const float BackstabBonus = 1.5f;

    /// <summary>
    /// The damage multiplier an opening applies. A blow that already rolled a crit takes half the
    /// bonus on top rather than stacking both, and a boss takes 60% of it: still a reward, never a
    /// way to delete one.
    /// </summary>
    public static float OpeningMultiplier(HitKind opening, OpenCause cause, bool alreadyCrit, ReactionClass body)
    {
        float bonus = opening switch
        {
            HitKind.Riposte => RiposteBonus(cause),
            HitKind.Backstab => BackstabBonus,
            _ => 1f,
        };

        float m = 1f + ((bonus - 1f) * (body == ReactionClass.Boss ? 0.6f : 1f));
        return alreadyCrit ? 1f + ((m - 1f) * 0.5f) : m;
    }
}
