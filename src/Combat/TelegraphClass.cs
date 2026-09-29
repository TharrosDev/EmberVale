using System;
using Embervale.Combat.Actions;
using Embervale.Entities;

namespace Embervale.Combat;

/// <summary>What a wind-up asks of the player, readable at a glance. <b>Append-only.</b></summary>
public enum TelegraphClass
{
    /// <summary>An ordinary blow: a plain ring in the phase colour.</summary>
    Standard = 0,

    /// <summary>A quick, guardable strike with a parry window: a gold timing ring closes on the
    /// ring and flashes when to raise the guard.</summary>
    Parryable = 1,

    /// <summary>Do not guard this, get out of the way: a filled, hot ring that pulses.</summary>
    Unblockable = 2,

    /// <summary>A wide arc (wing, tail, cleave, breath): a fan instead of a ring, so the player reads
    /// where the danger is and where it is not.</summary>
    Sweep = 3,

    /// <summary>Not authored: read the class off the action's id, hitbox and commitment. The default
    /// on <c>ActionDefinitionResource.Telegraph</c>, so existing data keeps its inferred class.</summary>
    Auto = 4,
}

/// <summary>What is readable about an action that says what kind of blow it is. An authored
/// <paramref name="Authored"/> class (and <paramref name="AuthoredSweep"/> fan angle, 0 = infer) wins.</summary>
public readonly record struct TelegraphSource(
    ActionKind Kind, string ActionId, string HitboxName, bool Interruptible,
    TelegraphClass Authored = TelegraphClass.Auto, float AuthoredSweep = 0f);

/// <summary>
/// Pure mapping from an action to its <see cref="TelegraphClass"/>, keyed only on what
/// <see cref="ActionDefinitionResource"/> exposes today.
///
/// <para>An action may author its class (<c>ActionDefinitionResource.Telegraph</c>, and
/// <c>SweepDegrees</c> for a fan) and that wins. ⚠️ Left at <see cref="TelegraphClass.Auto"/> — every
/// existing action — the class is still read off the id and the hitbox name (the dragon's <c>wing</c>
/// and <c>tail</c> volumes, an id that says <c>sweep</c>, <c>cleave</c> or <c>breath</c>) and off
/// commitment (a heavy blow that hyperarmor makes uninterruptible reads as "do not stand in it").
/// Author the flag when a name would lie.</para>
/// </summary>
public static class TelegraphClasses
{
    /// <summary>The fan angle of a sweep with no better information, degrees.</summary>
    public const float DefaultSweepDegrees = 150f;

    /// <summary>The class of an action.</summary>
    public static TelegraphClass Classify(in TelegraphSource source)
    {
        if (source.Authored != TelegraphClass.Auto)
        {
            return source.Authored;
        }

        string tokens = ((source.ActionId ?? "") + " " + (source.HitboxName ?? "")).ToLowerInvariant();

        // Declared in the id: an author who wrote it in the name has said it.
        if (Has(tokens, "unblock") || Has(tokens, "grab") || Has(tokens, "unparry"))
        {
            return TelegraphClass.Unblockable;
        }

        if (Has(tokens, "sweep") || Has(tokens, "tail") || Has(tokens, "wing") || Has(tokens, "cleave") ||
            Has(tokens, "spin") || Has(tokens, "whirl") || Has(tokens, "breath") || Has(tokens, "swipe"))
        {
            return TelegraphClass.Sweep;
        }

        // A committed heavy: nothing interrupts it and a held guard will not hold it.
        if (source.Kind == ActionKind.HeavyAttack && !source.Interruptible)
        {
            return TelegraphClass.Unblockable;
        }

        return source.Kind == ActionKind.Attack ? TelegraphClass.Parryable : TelegraphClass.Standard;
    }

    /// <summary>The fan angle of a sweep, degrees: a tail comes round behind, a spin is a full circle.</summary>
    public static float SweepDegrees(in TelegraphSource source)
    {
        if (source.AuthoredSweep > 0f)
        {
            return Math.Clamp(source.AuthoredSweep, 30f, 360f);
        }

        string tokens = ((source.ActionId ?? "") + " " + (source.HitboxName ?? "")).ToLowerInvariant();
        if (Has(tokens, "spin") || Has(tokens, "whirl"))
        {
            return 360f;
        }

        if (Has(tokens, "tail"))
        {
            return 220f;
        }

        return Has(tokens, "breath") ? 70f : DefaultSweepDegrees;
    }

    /// <summary>The locale key of the short label for a class ("PARRY", "DODGE", "SWEEP").</summary>
    public static string LabelKey(TelegraphClass cls) => cls switch
    {
        TelegraphClass.Parryable => "combat.feedback.tell_parry",
        TelegraphClass.Unblockable => "combat.feedback.tell_dodge",
        TelegraphClass.Sweep => "combat.feedback.tell_sweep",
        _ => "combat.feedback.tell_attack",
    };

    /// <summary>The class of the action an entity is running, or null when it is running none.</summary>
    public static TelegraphClass? Of(IEntity? attacker)
    {
        if (attacker?.GetComponent<CharacterActionComponent>()?.Current is not { } action)
        {
            return null;
        }

        return Classify(FromAction(action));
    }

    /// <summary>The readable facts of an action definition.</summary>
    public static TelegraphSource FromAction(ActionDefinitionResource action) =>
        new(action.Kind, action.Id, action.HitboxName, action.Interruptible,
            action.Telegraph, action.SweepDegrees);

    private static bool Has(string tokens, string token) => tokens.Contains(token, StringComparison.Ordinal);
}
