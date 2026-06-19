using System;
using System.Collections.Generic;

namespace Embervale.Stats;

/// <summary>
/// A single character statistic: a base value plus any number of
/// <see cref="StatModifier"/>s. The final value is recomputed lazily and cached
/// until something invalidates it, so reads are cheap even when queried every
/// frame by UI or AI.
///
/// Combination order (standard ARPG model):
///   final = (base + Σ flat) × (1 + Σ percentAdd) × Π (1 + percentMult)
/// </summary>
public sealed class Stat
{
    private readonly List<StatModifier> _modifiers = new();
    private float _baseValue;
    private float _cachedValue;
    private bool _dirty = true;

    public Stat(StatType type, float baseValue)
    {
        Type = type;
        _baseValue = baseValue;
    }

    /// <summary>Fired whenever the computed value may have changed.</summary>
    public event Action<Stat>? Changed;

    public StatType Type { get; }

    public float BaseValue
    {
        get => _baseValue;
        set
        {
            if (Math.Abs(_baseValue - value) < float.Epsilon)
            {
                return;
            }

            _baseValue = value;
            Invalidate();
        }
    }

    /// <summary>The final value after all modifiers; computed on demand and cached.</summary>
    public float Value
    {
        get
        {
            if (_dirty)
            {
                _cachedValue = Recalculate();
                _dirty = false;
            }

            return _cachedValue;
        }
    }

    public IReadOnlyList<StatModifier> Modifiers => _modifiers;

    public void AddModifier(StatModifier modifier)
    {
        ArgumentNullException.ThrowIfNull(modifier);
        _modifiers.Add(modifier);
        Invalidate();
    }

    public bool RemoveModifier(StatModifier modifier)
    {
        if (_modifiers.Remove(modifier))
        {
            Invalidate();
            return true;
        }

        return false;
    }

    /// <summary>Removes every modifier originating from <paramref name="source"/>.</summary>
    public int RemoveModifiersFromSource(object source)
    {
        int removed = _modifiers.RemoveAll(m => Equals(m.Source, source));
        if (removed > 0)
        {
            Invalidate();
        }

        return removed;
    }

    public void ClearModifiers()
    {
        if (_modifiers.Count == 0)
        {
            return;
        }

        _modifiers.Clear();
        Invalidate();
    }

    private void Invalidate()
    {
        _dirty = true;
        Changed?.Invoke(this);
    }

    private float Recalculate()
    {
        float flat = 0f;
        float percentAdd = 0f;
        float value = _baseValue;

        // First pass: gather flat and additive-percent; apply multiplicative inline.
        var multipliers = new List<float>();
        foreach (StatModifier mod in _modifiers)
        {
            switch (mod.Type)
            {
                case ModifierType.Flat:
                    flat += mod.Value;
                    break;
                case ModifierType.PercentAdd:
                    percentAdd += mod.Value;
                    break;
                case ModifierType.PercentMult:
                    multipliers.Add(mod.Value);
                    break;
            }
        }

        value += flat;
        value *= 1f + percentAdd;
        foreach (float m in multipliers)
        {
            value *= 1f + m;
        }

        return value;
    }
}
