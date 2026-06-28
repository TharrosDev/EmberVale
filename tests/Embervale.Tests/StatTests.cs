using Embervale.Stats;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// Covers the ARPG stat formula — the bedrock combat/equipment/progression all build on.
/// Order matters: final = (base + Σflat) × (1 + ΣpercentAdd) × Π(1 + percentMult).
/// </summary>
public class StatTests
{
    private const float Tolerance = 0.0001f;

    [Fact]
    public void BaseValue_WithNoModifiers_IsReturnedVerbatim()
    {
        var stat = new Stat(StatType.Strength, 10f);
        Assert.Equal(10f, stat.Value, Tolerance);
    }

    [Fact]
    public void FlatModifiers_AreSummedOntoBase()
    {
        var stat = new Stat(StatType.Strength, 10f);
        stat.AddModifier(new StatModifier(5f, ModifierType.Flat));
        stat.AddModifier(new StatModifier(3f, ModifierType.Flat));

        Assert.Equal(18f, stat.Value, Tolerance);
    }

    [Fact]
    public void PercentAdd_StacksAdditivelyThenAppliesOnce()
    {
        var stat = new Stat(StatType.PhysicalPower, 100f);
        stat.AddModifier(new StatModifier(0.10f, ModifierType.PercentAdd));
        stat.AddModifier(new StatModifier(0.15f, ModifierType.PercentAdd));

        // 100 × (1 + 0.25) = 125
        Assert.Equal(125f, stat.Value, Tolerance);
    }

    [Fact]
    public void PercentMult_StacksMultiplicatively()
    {
        var stat = new Stat(StatType.PhysicalPower, 100f);
        stat.AddModifier(new StatModifier(0.10f, ModifierType.PercentMult));
        stat.AddModifier(new StatModifier(0.10f, ModifierType.PercentMult));

        // 100 × 1.1 × 1.1 = 121
        Assert.Equal(121f, stat.Value, Tolerance);
    }

    [Fact]
    public void CombinedOrder_FlatThenAddThenMult()
    {
        var stat = new Stat(StatType.PhysicalPower, 100f);
        stat.AddModifier(new StatModifier(50f, ModifierType.Flat));
        stat.AddModifier(new StatModifier(0.20f, ModifierType.PercentAdd));
        stat.AddModifier(new StatModifier(0.50f, ModifierType.PercentMult));

        // (100 + 50) × (1 + 0.20) × (1 + 0.50) = 150 × 1.2 × 1.5 = 270
        Assert.Equal(270f, stat.Value, Tolerance);
    }

    [Fact]
    public void RemoveModifiersFromSource_StripsOnlyThatSource()
    {
        var item = new object();
        var other = new object();
        var stat = new Stat(StatType.Armor, 0f);
        stat.AddModifier(new StatModifier(10f, ModifierType.Flat, item));
        stat.AddModifier(new StatModifier(5f, ModifierType.Flat, other));

        int removed = stat.RemoveModifiersFromSource(item);

        Assert.Equal(1, removed);
        Assert.Equal(5f, stat.Value, Tolerance);
    }

    [Fact]
    public void RemoveModifier_DropsOneAndRecomputes()
    {
        var stat = new Stat(StatType.Armor, 10f);
        var keep = new StatModifier(5f, ModifierType.Flat);
        var drop = new StatModifier(3f, ModifierType.Flat);
        stat.AddModifier(keep);
        stat.AddModifier(drop);
        Assert.Equal(18f, stat.Value, Tolerance);

        Assert.True(stat.RemoveModifier(drop));
        Assert.Equal(15f, stat.Value, Tolerance);
        Assert.False(stat.RemoveModifier(drop)); // already gone
    }

    [Fact]
    public void ClearModifiers_ReturnsToBase()
    {
        var stat = new Stat(StatType.PhysicalPower, 100f);
        stat.AddModifier(new StatModifier(50f, ModifierType.Flat));
        stat.AddModifier(new StatModifier(0.20f, ModifierType.PercentMult));
        Assert.NotEqual(100f, stat.Value, Tolerance);

        stat.ClearModifiers();
        Assert.Equal(100f, stat.Value, Tolerance);
    }

    [Fact]
    public void Changed_FiresWhenModifierRemoved()
    {
        var stat = new Stat(StatType.Mana, 50f);
        var mod = new StatModifier(10f, ModifierType.Flat);
        stat.AddModifier(mod);

        int fired = 0;
        stat.Changed += _ => fired++;
        stat.RemoveModifier(mod);

        Assert.True(fired > 0);
    }

    [Fact]
    public void RemoveModifiersFromSource_StripsAllStackedFromThatSource()
    {
        var gear = new object();
        var stat = new Stat(StatType.PhysicalPower, 0f);
        stat.AddModifier(new StatModifier(5f, ModifierType.Flat, gear));
        stat.AddModifier(new StatModifier(0.10f, ModifierType.PercentMult, gear));
        stat.AddModifier(new StatModifier(3f, ModifierType.Flat, new object())); // a different source

        int removed = stat.RemoveModifiersFromSource(gear);

        Assert.Equal(2, removed);
        Assert.Equal(3f, stat.Value, Tolerance); // only the other source's flat remains
    }

    [Fact]
    public void ChangingBaseValue_RecomputesCachedValue()
    {
        var stat = new Stat(StatType.Health, 100f);
        stat.AddModifier(new StatModifier(0.10f, ModifierType.PercentMult));
        Assert.Equal(110f, stat.Value, Tolerance);

        stat.BaseValue = 200f;
        Assert.Equal(220f, stat.Value, Tolerance);
    }

    [Fact]
    public void Changed_FiresWhenModifierAdded()
    {
        var stat = new Stat(StatType.Mana, 50f);
        int fired = 0;
        stat.Changed += _ => fired++;

        stat.AddModifier(new StatModifier(10f, ModifierType.Flat));

        Assert.True(fired > 0);
    }

    [Theory]
    [InlineData(StatType.Health, true)]
    [InlineData(StatType.Stamina, true)]
    [InlineData(StatType.Mana, true)]
    [InlineData(StatType.Strength, false)]
    [InlineData(StatType.Armor, false)]
    public void StatTypes_IsResource_ClassifiesDepletingStats(StatType type, bool expected)
    {
        Assert.Equal(expected, StatTypes.IsResource(type));
    }
}
