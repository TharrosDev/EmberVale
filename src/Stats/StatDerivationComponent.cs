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
