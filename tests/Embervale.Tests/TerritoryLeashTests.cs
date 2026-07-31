using Embervale.Enemies;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// The leash (Phase 35D) — the one rule standing between a territorial world boss and a flying
/// dragon that can be kited into the next realm. The chase, the pathing home and the state change are
/// Godot-bound and verified by build/run; the band is pure, and it is the part that decides whether
/// the fight is winnable by walking backwards.
/// </summary>
public class TerritoryLeashTests
{
    private const float Radius = 45f;

    [Fact]
    public void NoTerritory_NeverLeashes()
    {
        // Every archetype before the dragon has radius 0 — this is the property that keeps them all
        // behaving exactly as they did.
        Assert.False(TerritoryLeash.ShouldBreakOff(1000f, 0f, returning: false));
        Assert.False(TerritoryLeash.ShouldBreakOff(1000f, 0f, returning: true));
    }

    [Fact]
    public void NegativeTerritory_IsTreatedAsNone()
    {
        // The validator rejects it, but the rule must not invert into "always leashed" meanwhile.
        Assert.False(TerritoryLeash.ShouldBreakOff(1000f, -10f, returning: false));
    }

    [Fact]
    public void InsideTheRadius_KeepsFighting()
    {
        Assert.False(TerritoryLeash.ShouldBreakOff(0f, Radius, returning: false));
        Assert.False(TerritoryLeash.ShouldBreakOff(Radius - 1f, Radius, returning: false));
        Assert.False(TerritoryLeash.ShouldBreakOff(Radius, Radius, returning: false));
    }

    [Fact]
    public void PastTheRadius_BreaksOff()
    {
        Assert.True(TerritoryLeash.ShouldBreakOff(Radius + 0.1f, Radius, returning: false));
        Assert.True(TerritoryLeash.ShouldBreakOff(500f, Radius, returning: false));
    }

    [Fact]
    public void Returning_MustComeWellInsideBeforeReengaging()
    {
        // The hysteresis. Crossing back over the boundary is not enough — otherwise a creature sitting
        // on the line flips between chasing and going home every frame.
        float justInside = Radius - 1f;
        Assert.False(TerritoryLeash.ShouldBreakOff(justInside, Radius, returning: false));
        Assert.True(TerritoryLeash.ShouldBreakOff(justInside, Radius, returning: true));
    }

    [Fact]
    public void Returning_StopsAtTheReturnBand()
    {
        float band = Radius * TerritoryLeash.ReturnFraction;
        Assert.True(TerritoryLeash.ShouldBreakOff(band + 0.1f, Radius, returning: true));
        Assert.False(TerritoryLeash.ShouldBreakOff(band, Radius, returning: true));
        Assert.False(TerritoryLeash.ShouldBreakOff(0f, Radius, returning: true));
    }

    [Fact]
    public void TheBandIsNarrowerThanTheTerritory()
    {
        // A return fraction of 1 would remove the hysteresis entirely; 0 would make it walk all the
        // way to the exact centre before it would ever fight again.
        Assert.InRange(TerritoryLeash.ReturnFraction, 0.1f, 0.99f);
    }
}
