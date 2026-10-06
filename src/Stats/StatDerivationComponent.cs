using System.Collections.Generic;
using Embervale.Entities;
using Godot;

namespace Embervale.Stats;

/// <summary>
/// Turns the primary attributes into derived-stat bonuses (<see cref="StatDerivation"/>). It listens to
/// each primary's <see cref="Stat.Changed"/> and keeps one Flat modifier per derived stat, sourced to
/// itself and sized from <c>(Value - BaseValue) * coefficient</c>, so an entity at its base primaries
/// is untouched. Re-applying removes the old modifier first, and it only writes derived stats (never a
/// primary), so it cannot loop. Stateless beyond that: nothing is saved, it re-derives on load because
/// the primaries do.
/// </summary>
[GlobalClass]
public partial class StatDerivationComponent : EntityComponent
{
    private readonly Dictionary<StatType, float> _applied = new();
    // One source per (primary, derived) pair so two primaries could feed one stat without stripping each other.
    private readonly Dictionary<(StatType, StatType), object> _keys = new();
    private StatsComponent? _stats;

    protected override void OnInitialize()
    {
        _stats = Entity!.GetComponent<StatsComponent>();
        if (_stats == null)
        {
            return;
        }

        foreach (StatType primary in StatDerivation.Primaries)
        {
            _stats.GetStat(primary).Changed += OnPrimaryChanged;
            Apply(primary);
        }
    }

    protected override void OnTeardown()
    {
        if (_stats == null)
        {
            return;
        }

        foreach (StatType primary in StatDerivation.Primaries)
        {
            _stats.GetStat(primary).Changed -= OnPrimaryChanged;
        }
    }

    /// <summary>Multiplier on this entity's dodge stamina cost from invested Dexterity; 1 for an entity without the
    /// derivation (enemies, NPCs), so only the player's primaries pay off.</summary>
    public static float DodgeFactor(IEntity? entity) => Invested(entity, StatType.Dexterity, StatDerivation.DodgeStaminaFactor);

    /// <summary>Multiplier on this entity's spell mana cost from invested Intelligence; 1 without the derivation.</summary>
    public static float ManaFactor(IEntity? entity) => Invested(entity, StatType.Intelligence, StatDerivation.ManaCostFactor);

    private static float Invested(IEntity? entity, StatType primary, System.Func<float, float> factor)
    {
        if (entity?.GetComponent<StatDerivationComponent>() == null || entity.GetComponent<StatsComponent>() is not { } stats)
        {
            return 1f;
        }

        Stat stat = stats.GetStat(primary);
        return factor(stat.Value - stat.BaseValue);
    }

    private void OnPrimaryChanged(Stat stat) => Apply(stat.Type);

    private void Apply(StatType primary)
    {
        Stat source = _stats!.GetStat(primary);
        float points = source.Value - source.BaseValue;
        if (_applied.TryGetValue(primary, out float last) && Mathf.IsEqualApprox(last, points))
        {
            return;
        }

        _applied[primary] = points;
        foreach (StatDerivation.Effect effect in StatDerivation.Effects(primary))
        {
            Stat target = _stats.GetStat(effect.Stat);
            object key = Key(primary, effect.Stat);
            target.RemoveModifiersFromSource(key);
            float amount = effect.PerPoint * points;
            if (amount != 0f)
            {
                target.AddModifier(new StatModifier(amount, ModifierType.Flat, key));
            }
        }
    }

    private object Key(StatType primary, StatType target)
    {
        if (!_keys.TryGetValue((primary, target), out object? key))
        {
            key = new object();
            _keys[(primary, target)] = key;
        }

        return key;
    }
}
