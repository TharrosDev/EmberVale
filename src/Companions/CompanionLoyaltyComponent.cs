using Embervale.Core.Events;
using Embervale.Core.Services;
using Embervale.Entities;
using Embervale.Stats;
using Godot;

namespace Embervale.Companions;

/// <summary>
/// Loyalty's mechanical face on the actor (Phase 32C): it reads the companion's standing from the
/// <see cref="CompanionRoster"/> — which owns and persists the number — and turns the current
/// <see cref="LoyaltyTier"/> into stat modifiers, so a companion who believes in you visibly fights
/// harder for you.
///
/// It deliberately stores nothing: the roster is the single source of truth for loyalty (a companion
/// you dismissed keeps their standing even with no actor in the world), and this component is a
/// projection of it that re-applies itself whenever the tier moves.
/// </summary>
[GlobalClass]
public partial class CompanionLoyaltyComponent : EntityComponent
{
    /// <summary>Stats the loyalty bonus applies to. Health scales so a trusted companion also
    /// survives longer, not merely hits harder.</summary>
    private static readonly StatType[] BoostedStats =
    {
        StatType.PhysicalPower,
        StatType.SpellPower,
        StatType.Health,
    };

    private StatsComponent? _stats;
    private LoyaltyTier _applied = LoyaltyTier.Wary;

    /// <summary>The tier currently reflected in the owner's stats.</summary>
    public LoyaltyTier Tier => _applied;

    protected override void OnInitialize()
    {
        _stats = Entity!.GetComponent<StatsComponent>();
        EventBus.Instance?.Subscribe<CompanionLoyaltyTierChangedEvent>(OnTierChanged);

        // Apply the standing the companion already has — on a load, or on a re-recruit, they walk in
        // at whatever they had earned, not at Wary.
        Apply(Roster()?.TierOf(CompanionId) ?? LoyaltyTier.Wary);
    }

    protected override void OnTeardown()
    {
        EventBus.Instance?.Unsubscribe<CompanionLoyaltyTierChangedEvent>(OnTierChanged);
        RemoveModifiers();
    }

    private string CompanionId => (Entity as CompanionEntity)?.CompanionId ?? string.Empty;

    private void OnTierChanged(CompanionLoyaltyTierChangedEvent e)
    {
        if (e.CompanionId == CompanionId)
        {
            Apply(e.Tier);
        }
    }

    private void Apply(LoyaltyTier tier)
    {
        if (_stats == null)
        {
            return;
        }

        RemoveModifiers();
        _applied = tier;

        float bonus = CompanionLoyalty.CombatBonus(tier);
        if (bonus <= 0f)
        {
            return;
        }

        foreach (StatType stat in BoostedStats)
        {
            _stats.GetStat(stat).AddModifier(new StatModifier(bonus, ModifierType.PercentAdd, this));
        }
    }

    private void RemoveModifiers()
    {
        if (_stats == null)
        {
            return;
        }

        foreach (StatType stat in BoostedStats)
        {
            _stats.GetStat(stat).RemoveModifiersFromSource(this);
        }
    }

    private static CompanionRoster? Roster() =>
        ServiceLocator.Instance != null && ServiceLocator.Instance.TryGet(out CompanionRoster roster)
            ? roster
            : null;
}
