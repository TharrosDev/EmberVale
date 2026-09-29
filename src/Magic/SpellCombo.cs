using System;
using System.Collections.Generic;
using Embervale.Combat;
using Embervale.Core.Diagnostics;
using Embervale.Core.Events;
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
    bool ConsumeStatus,
    string Id = "")
{
    /// <summary>Player-visible name through <c>Loc</c>, falling back to <see cref="Name"/>.</summary>
    public string LocalName => Embervale.Localization.Loc.Has("magic.status." + Id + ".name")
        ? Embervale.Localization.Loc.T("magic.status." + Id + ".name") : Name;
}

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
///   * <b>Steam Burst</b> — Frost into a Kindled foe: the cold snuffs the Kindle in a scalding burst.
///   * <b>Meltdown</b> — Fire into a Frozen foe: the ice gives way at once, ending the freeze.
///   * <b>Superconduct</b> — Frost into a Stormbranded foe: the brand discharges through the cold.
///   * <b>Smoke Out</b> — Fire into a Swarmed foe: the swarm is burned off and scatters.
/// Each carries a stable id (<c>combo.*</c>) that <see cref="SpellComboEvent"/> raises.
/// </summary>
public static class SpellCombo
{
    private const float ComboPoise = 6f;

    private static readonly ComboRule[] Rules =
    {
        new("Shatter", DamageType.Lightning, StatusIds.Chill, 18f, true, "combo.shatter"),
        new("Meltdown", DamageType.Fire, StatusIds.Frozen, 24f, true, "combo.meltdown"),
        new("Thermal Shock", DamageType.Fire, StatusIds.Chill, 14f, true, "combo.thermal_shock"),
        new("Steam Burst", DamageType.Frost, StatusIds.Kindled, 16f, true, "combo.steam_burst"),
        new("Superconduct", DamageType.Frost, StatusIds.Stormbrand, 20f, true, "combo.superconduct"),
        new("Smoke Out", DamageType.Fire, StatusIds.Swarmed, 16f, true, "combo.smoke_out"),
    };

    /// <summary>
    /// The combo table, for the spellbook's synergy panel (37.5D). Exposed rather than duplicated so
    /// the screen teaches exactly the rules combat resolves — a documented combo that does not fire
    /// is worse than an undocumented one, because the player builds around it.
    /// </summary>
    public static IReadOnlyList<ComboRule> All => Rules;

    /// <summary>Every combo a school can trigger. The spellbook lists these under the school, which
    /// is the only place in the game that says these interactions exist at all.</summary>
    public static IEnumerable<ComboRule> ForSchool(DamageType school)
    {
        foreach (ComboRule rule in Rules)
        {
            if (rule.TriggerSchool == school)
            {
                yield return rule;
            }
        }
    }

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

        if (primary.OwnerEntity is { } target)
        {
            EventBus.Instance?.Publish(new SpellComboEvent(caster ?? target, target, rule.Id));
        }

        Log.Info($"Spell combo triggered: {rule.Name}");
    }
}
