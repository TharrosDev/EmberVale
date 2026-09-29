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
}

/// <summary>What is readable about an action today that says what kind of blow it is.</summary>
public readonly record struct TelegraphSource(
    ActionKind Kind, string ActionId, string HitboxName, bool Interruptible);

/// <summary>
/// Pure mapping from an action to its <see cref="TelegraphClass"/>, keyed only on what
/// <see cref="ActionDefinitionResource"/> exposes today.
///
/// <para>⚠️ <b>This is a stand-in for an authored flag.</b> Nothing on the definition says "this cannot
/// be guarded" or "this is a wide arc", so the class is read off the id and the hitbox name (the
/// dragon's <c>wing</c> and <c>tail</c> volumes, an id that says <c>sweep</c>, <c>cleave</c> or
/// <c>breath</c>) and off commitment (a heavy blow that hyperarmor makes uninterruptible reads as
/// "do not stand in it"). The flag wanted is an authored <c>TelegraphClass</c> (or separate
/// <c>Unblockable</c> and <c>SweepDegrees</c>) on <c>ActionDefinitionResource</c>; when it exists it
/// replaces the token match here and nothing that consumes the class changes.</para>
/// </summary>
public static class TelegraphClasses
{
    /// <summary>The fan angle of a sweep with no better information, degrees.</summary>
    public const float DefaultSweepDegrees = 150f;

    /// <summary>The class of an action.</summary>
    public static TelegraphClass Classify(in TelegraphSource source)
    {
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
        new(action.Kind, action.Id, action.HitboxName, action.Interruptible);

    private static bool Has(string tokens, string token) => tokens.Contains(token, StringComparison.Ordinal);
}
