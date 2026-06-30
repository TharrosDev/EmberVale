using System;
using Embervale.Combat;
using Embervale.Core.Diagnostics;
using Embervale.Entities;
using Godot;

namespace Embervale.Magic;

/// <summary>
/// One reactive-combo rule (Phase 29.5D): casting a <see cref="TriggerSchool"/> spell at a target that
/// already carries <see cref="RequiredStatusId"/> detonates the combo — a burst of
/// <see cref="BonusDamage"/> (in the trigger's school) and, if <see cref="ConsumeStatus"/>, the
/// triggering status is spent.
/// </summary>
public readonly record struct ComboRule(
    string Name,
    DamageType TriggerSchool,
    string RequiredStatusId,
    float BonusDamage,
    bool ConsumeStatus);

/// <summary>
/// The magic analogue of the combat read (Phase 29.5D): cross-school interactions. When a spell hits, it
/// checks the target's existing afflictions and, on a match, fires a bonus payoff. Read on the same
/// on-hit seam as <see cref="SchoolIdentity"/>, <em>before</em> the spell applies its own status, so a
/// combo reads the pre-hit condition and never triggers off the status the same cast is about to add.
///
/// The combos are a declarative table, not scattered <c>if</c>s — adding one is a new <see cref="Rules"/>
/// entry. ponytail: in-code table; promote to a <c>.tres</c> ComboResource + database only if the
/// catalogue (Phase 51) grows past a handful.
///
/// Shipped combos:
///   * <b>Shatter</b> — Lightning into a Chilled foe: the brittle ice cracks for a burst (consumes chill).
///   * <b>Thermal Shock</b> — Fire into a Chilled foe: the temperature swing wracks it (consumes chill).
/// </summary>
public static class SpellCombo
{
    private const float ComboPoise = 6f;

    private static readonly ComboRule[] Rules =
    {
        new("Shatter", DamageType.Lightning, "status.chill", 18f, true),
        new("Thermal Shock", DamageType.Fire, "status.chill", 14f, true),
    };

    /// <summary>The first combo whose trigger school matches and whose required status the target has
    /// (per <paramref name="targetHas"/>), or null. Pure — the lookup is unit-testable apart from Godot.</summary>
    public static ComboRule? Match(DamageType school, Func<string, bool> targetHas)
    {
        foreach (ComboRule rule in Rules)
        {
            if (rule.TriggerSchool == school && targetHas(rule.RequiredStatusId))
            {
                return rule;
            }
        }

        return null;
    }

    /// <summary>Resolves any combo for a spell hitting <paramref name="primary"/>: bonus damage + consume.</summary>
    public static void OnHit(SpellResource spell, IEntity? caster, Hurtbox primary)
    {
        StatusEffectsComponent? status = primary.OwnerEntity?.GetComponent<StatusEffectsComponent>();
        if (status == null || Match(spell.School, status.Has) is not { } rule)
        {
            return;
        }

        primary.Receive(new DamagePacket(rule.BonusDamage, spell.School, caster, false, ComboPoise));
        if (rule.ConsumeStatus)
        {
            status.Consume(rule.RequiredStatusId);
        }

        Log.Info($"Spell combo triggered: {rule.Name}");
    }
}
