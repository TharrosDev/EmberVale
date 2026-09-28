using Embervale.Movement;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// What riding does to a blow (Phase 39B). Small surface, but <c>MeleeWeaponComponent</c> asks this
/// on every swing in the game — every enemy, every companion, the player on foot — so the case that
/// matters most is the one where nothing is supposed to happen.
/// </summary>
public class MountedCombatTests
{
    /// <summary>
    /// ⚠️ The safety property, and the reason this is a pure function rather than three lines inside
    /// the melee component. A 0.99 here would restat every melee attacker in the world and would
    /// look like a balance drift, not like a bug in the mount.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AnUnmountedAttackerIsExactlyUnchanged(bool galloping)
    {
        Assert.Equal(1f, MountedCombat.DamageScale(mounted: false, galloping));
    }

    /// <summary>Weight with no speed behind it is not a weapon.</summary>
    [Fact]
    public void SittingOnAHorseIsWorthNothing()
    {
        Assert.Equal(1f, MountedCombat.DamageScale(mounted: true, galloping: false));
    }

    [Fact]
    public void AGallopIsTheCharge()
    {
        float charge = MountedCombat.DamageScale(mounted: true, galloping: true);

        Assert.Equal(MountedCombat.GallopScale, charge);
        Assert.True(charge > MountedCombat.DamageScale(mounted: true, galloping: false));
    }

    /// <summary>
    /// The bonus is a bonus, never a penalty. A mounted rider who has blown the gallop pool must
    /// swing for exactly what they would on foot — otherwise 39A's exhaustion latch would quietly
    /// become a combat debuff, which is not what it was written to be.
    /// </summary>
    /// <summary>
    /// The tightening: a granted gallop is only a charge at gallop pace. Pinned on a wall with
    /// sprint held, or on the first frame of the gallop, the horse is not carrying any weight.
    /// </summary>
    [Fact]
    public void AGallopWithNoPaceIsNotACharge()
    {
        const float gallop = 2.72f;

        Assert.False(MountedCombat.IsCharging(true, 0f, gallop));
        Assert.False(MountedCombat.IsCharging(true, gallop * 0.5f, gallop));
        Assert.True(MountedCombat.IsCharging(true, gallop * MountedCombat.ChargeSpeedFraction, gallop));
        Assert.True(MountedCombat.IsCharging(true, gallop, gallop));
    }

    [Fact]
    public void PaceWithoutTheGallopIsNotACharge()
    {
        // A downhill canter can touch gallop pace; the charge is still the gallop's, not the slope's.
        Assert.False(MountedCombat.IsCharging(false, 3f, 2.72f));
        Assert.False(MountedCombat.IsCharging(true, float.NaN, 2.72f));
        Assert.False(MountedCombat.IsCharging(true, 3f, 0f));
    }

    [Fact]
    public void RidingNeverMakesABlowWeakerThanOnFoot()
    {
        Assert.True(MountedCombat.DamageScale(true, false) >= MountedCombat.DamageScale(false, false));
        Assert.True(MountedCombat.DamageScale(true, true) >= MountedCombat.DamageScale(false, false));
        Assert.Equal(1f, MountedCombat.WalkingScale);
    }
}
