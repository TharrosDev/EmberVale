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

    /// <summary>The cast action for a spell — authored if it has one, otherwise derived once.</summary>
    public static ActionDefinitionResource? For(SpellResource? spell)
    {
        if (spell == null)
        {
            return null;
        }

        if (spell.CastAction is { } authored)
        {
            return authored;
        }

        if (Cache.TryGetValue(spell.Id, out ActionDefinitionResource? cached))
        {
            return cached;
        }

        // A channelled spell's "cast" is the moment it starts sustaining, so it releases early and
        // recovers fast; an instant one has a readable throw. Neither roots the caster completely —
        // a mage pinned in place by every bolt is a mage who cannot kite.
        bool sustained = spell.CastMode == CastMode.Channeled;
        var definition = new ActionDefinitionResource
        {
            Id = $"spell.{spell.Id}",
            Kind = ActionKind.Cast,
            AnimationSlot = sustained ? "channel" : "cast",
            Duration = 0f,                       // the clip decides; a cast has no gameplay deadline
            FallbackDuration = sustained ? 0.45f : 0.7f,
            ActiveFrom = sustained ? 0.2f : 0.45f,
            ActiveTo = sustained ? 0.3f : 0.55f,
            CancelFrom = sustained ? 0.35f : 0.7f,
            ComboFrom = 1f,
            ComboTo = 1f,
            StaminaCost = 0f,                    // spells cost mana, and it is already spent
            MoveScale = 0.5f,
            TurnDegreesPerSecond = -1f,
            Interruptible = true,
            SwingCueId = "sfx.combat.swing",
        };

        Cache[spell.Id] = definition;
        return definition;
    }

    /// <summary>Drops the derived actions. Session-scoped state: the spell database is rebuilt on a
    /// new game, and a definition cached against a spell id from a previous session would outlive
    /// the resource it describes.</summary>
    public static void Clear() => Cache.Clear();
}
