using Embervale.Combat;
using Embervale.Combat.Actions;
using Godot;

namespace Embervale.Magic;

/// <summary>
/// The cast action a spell runs through.
///
/// <para>Spells author their identity — school, delivery, damage, charge time — and almost never
/// want to author a bespoke animation timeline as well. So the cast shape is derived from the
/// spell's own <see cref="CastMode"/> and cached, exactly the way <c>WeaponResource</c> synthesises
/// an attack chain from its legacy timings. A spell that genuinely wants its own timing authors
/// <see cref="SpellResource.CastAction"/> and this steps aside.</para>
///
/// <para>⚠️ <b>The point is the release fraction.</b> It is where the bolt leaves the hand, and it is
/// the same number the animation is playing to — so a caster's arm and their spell agree, which they
/// did not when a cast fired on key-down and the clip played beside it.</para>
/// </summary>
public static class SpellActions
{
    private static readonly System.Collections.Generic.Dictionary<string, ActionDefinitionResource>
        Cache = new();

    /// <summary>The cast action for a spell — authored if it has one, otherwise derived once from the
    /// spell's own <see cref="SpellResource.WindupSeconds"/> and <see cref="SpellResource.RecoverySeconds"/>
    /// (<see cref="SpellRules.Shape"/>). <paramref name="hyperarmoured"/> is the variant that a stagger
    /// cannot cancel (an uninterruptible spell, or a caster under Barkskin).</summary>
    public static ActionDefinitionResource? For(SpellResource? spell, bool hyperarmoured = false)
    {
        if (spell == null)
        {
            return null;
        }

        if (spell.CastAction is { } authored)
        {
            return authored;
        }

        bool uninterruptible = hyperarmoured || !spell.Interruptible;
        string key = uninterruptible ? spell.Id + "#armoured" : spell.Id;
        if (Cache.TryGetValue(key, out ActionDefinitionResource? cached))
        {
            return cached;
        }

        // A channelled spell's "cast" is the moment it starts sustaining, so it releases early and
        // recovers fast; an instant one has a readable throw. Neither roots the caster completely —
        // a mage pinned in place by every bolt is a mage who cannot kite (MoveScale 0.5).
        bool sustained = spell.CastMode == CastMode.Channeled;
        CastShape shape = SpellRules.Shape(spell.WindupSeconds, spell.RecoverySeconds, sustained);
        var definition = new ActionDefinitionResource
        {
            Id = $"spell.{spell.Id}",
            Kind = ActionKind.Cast,
            AnimationSlot = sustained ? "channel" : "cast",
            Duration = shape.Duration,           // 0 = the clip decides; a derived shape warps the clip to fit
            FallbackDuration = shape.FallbackDuration,
            ActiveFrom = shape.ActiveFrom,
            ActiveTo = shape.ActiveTo,
            CancelFrom = shape.CancelFrom,
            ComboFrom = 1f,
            ComboTo = 1f,
            StaminaCost = 0f,                    // spells cost mana, and it is already spent
            MoveScale = 0.5f,
            TurnDegreesPerSecond = -1f,
            Interruptible = !uninterruptible,
            Telegraph = spell.Blockable ? TelegraphClass.Auto : TelegraphClass.Unblockable,
            SwingCueId = "sfx.combat.swing",
        };

        Cache[key] = definition;
        return definition;
    }

    /// <summary>Drops the derived actions. Session-scoped state: the spell database is rebuilt on a
    /// new game, and a definition cached against a spell id from a previous session would outlive
    /// the resource it describes.</summary>
    public static void Clear() => Cache.Clear();
}
