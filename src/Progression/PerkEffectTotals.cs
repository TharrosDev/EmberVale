using System.Collections.Generic;

namespace Embervale.Progression;

/// <summary>
/// The summed non-stat perk effects of one entity, keyed by kind and qualifier. Rebuilt by
/// <see cref="PerksComponent"/> whenever a rank changes and on load, so a read is a dictionary lookup.
/// Holds raw sums; <see cref="PerkEffectMath"/> applies the caps.
/// </summary>
public sealed class PerkEffectTotals
{
    private readonly Dictionary<(PerkEffectKind Kind, string Arg), float> _sums = new();

    public bool IsEmpty => _sums.Count == 0;

    public void Clear() => _sums.Clear();

    public void Add(PerkEffectKind kind, string? arg, float value)
    {
        (PerkEffectKind, string) key = (kind, arg ?? string.Empty);
        _sums[key] = (_sums.TryGetValue(key, out float sum) ? sum : 0f) + value;
    }

    /// <summary>Adds one perk effect at <paramref name="rank"/> ranks.</summary>
    public void Add(in PerkEffectEntry entry, int rank) => Add(entry.Kind, entry.Arg, entry.ValuePerRank * rank);

    /// <summary>The raw sum for a kind. An effect authored without a qualifier applies to every
    /// <paramref name="arg"/>, so a qualified read returns the global sum plus that qualifier's own.</summary>
    public float Get(PerkEffectKind kind, string? arg = null)
    {
        float total = _sums.TryGetValue((kind, string.Empty), out float global) ? global : 0f;
        if (!string.IsNullOrEmpty(arg) && _sums.TryGetValue((kind, arg), out float own))
        {
            total += own;
        }

        return total;
    }
}
