using Embervale.Enemies;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// Which arc a big body swings by bearing (Phase 35A). The hitbox swap itself is Godot-bound and
/// verified by build/run; the angles are the part worth pinning down, because getting them wrong
/// gives a dragon that only ever bites and can be beaten by standing behind it.
/// </summary>
public class DragonMeleeTests
{
    [Fact]
    public void DeadAhead_Bites()
    {
        Assert.Equal(DragonAttack.Bite, DragonMelee.Choose(0f));
        Assert.Equal(DragonAttack.Bite, DragonMelee.Choose(30f));
    }

    [Fact]
    public void Flank_SweepsAWing()
    {
        Assert.Equal(DragonAttack.Wing, DragonMelee.Choose(90f));
        Assert.Equal(DragonAttack.Wing, DragonMelee.Choose(60f));
    }

    [Fact]
    public void Behind_SwingsTheTail()
    {
        Assert.Equal(DragonAttack.Tail, DragonMelee.Choose(180f));
        Assert.Equal(DragonAttack.Tail, DragonMelee.Choose(150f));
    }

    [Fact]
    public void IsSymmetric_SignIgnored()
    {
        // The body has two wings; a target 90° left is the same problem as one 90° right.
        Assert.Equal(DragonMelee.Choose(90f), DragonMelee.Choose(-90f));
        Assert.Equal(DragonMelee.Choose(170f), DragonMelee.Choose(-170f));
        Assert.Equal(DragonMelee.Choose(20f), DragonMelee.Choose(-20f));
    }

    [Fact]
    public void Boundaries_BelongToTheNearerArc()
    {
        Assert.Equal(DragonAttack.Bite, DragonMelee.Choose(DragonMelee.BiteHalfAngle));
        Assert.Equal(DragonAttack.Wing, DragonMelee.Choose(DragonMelee.BiteHalfAngle + 0.1f));
        Assert.Equal(DragonAttack.Wing, DragonMelee.Choose(DragonMelee.WingHalfAngle));
        Assert.Equal(DragonAttack.Tail, DragonMelee.Choose(DragonMelee.WingHalfAngle + 0.1f));
    }
}
