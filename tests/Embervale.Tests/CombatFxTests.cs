using Embervale.Combat;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// Covers the pure combat-FX mapping (Phase 29C): the sound cue and spark tint distinguish a crit, a
/// blocked hit, and a plain hit, with crit taking precedence over block.
/// </summary>
public class CombatFxTests
{
    [Fact]
    public void CueId_DistinguishesCritBlockHit()
    {
        Assert.Equal(CombatFx.CritCue, CombatFx.CueId(isCrit: true, isBlocked: false));
        Assert.Equal(CombatFx.BlockCue, CombatFx.CueId(isCrit: false, isBlocked: true));
        Assert.Equal(CombatFx.HitCue, CombatFx.CueId(isCrit: false, isBlocked: false));
    }

    [Fact]
    public void CueId_CritBeatsBlock()
    {
        Assert.Equal(CombatFx.CritCue, CombatFx.CueId(isCrit: true, isBlocked: true));
    }

    [Fact]
    public void Tint_CritBlockHitAllDiffer()
    {
        var crit = CombatFx.Tint(true, false);
        var block = CombatFx.Tint(false, true);
        var hit = CombatFx.Tint(false, false);
        Assert.NotEqual(crit, block);
        Assert.NotEqual(crit, hit);
        Assert.NotEqual(block, hit);
    }
}
