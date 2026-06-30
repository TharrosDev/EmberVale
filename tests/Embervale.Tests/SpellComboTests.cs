using Embervale.Combat;
using Embervale.Magic;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// The reactive-combo lookup (Phase 29.5D): a combo matches only when the trigger school AND the
/// required pre-hit status both line up. The damage/consume application is Godot-bound and verified by
/// build/run.
/// </summary>
public class SpellComboTests
{
    [Fact]
    public void Shatter_Matches_LightningIntoChill()
    {
        ComboRule? rule = SpellCombo.Match(DamageType.Lightning, id => id == "status.chill");
        Assert.NotNull(rule);
        Assert.Equal("Shatter", rule!.Value.Name);
        Assert.True(rule.Value.ConsumeStatus);
    }

    [Fact]
    public void ThermalShock_Matches_FireIntoChill()
    {
        ComboRule? rule = SpellCombo.Match(DamageType.Fire, id => id == "status.chill");
        Assert.Equal("Thermal Shock", rule!.Value.Name);
    }

    [Fact]
    public void NoCombo_WhenStatusAbsent_OrSchoolMismatch()
    {
        Assert.Null(SpellCombo.Match(DamageType.Lightning, _ => false)); // chilled-but-not present
        Assert.Null(SpellCombo.Match(DamageType.Frost, id => id == "status.chill")); // Frost has no chill combo
    }
}
