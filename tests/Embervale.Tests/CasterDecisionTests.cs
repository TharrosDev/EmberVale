using Embervale.Enemies;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// The enemy caster's positioning brain (Phase 29.5F): hold the target in the band
/// [kiteDistance, castRange] — approach when too far, kite when too close, hold otherwise. The cast
/// selection + ally-support wiring is Godot-bound and verified by build/run.
/// </summary>
public class CasterDecisionTests
{
    private const float Kite = 6f;
    private const float CastRange = 14f;

    [Fact]
    public void TooClose_Kites()
    {
        Assert.Equal(CasterMove.Kite, CasterDecision.Move(3f, Kite, CastRange));
        Assert.Equal(CasterMove.Kite, CasterDecision.Move(0f, Kite, CastRange));
    }

    [Fact]
    public void TooFar_Approaches()
    {
        Assert.Equal(CasterMove.Approach, CasterDecision.Move(20f, Kite, CastRange));
    }

    [Fact]
    public void InTheBand_Holds()
    {
        Assert.Equal(CasterMove.Hold, CasterDecision.Move(6f, Kite, CastRange));   // at the near edge
        Assert.Equal(CasterMove.Hold, CasterDecision.Move(10f, Kite, CastRange));
        Assert.Equal(CasterMove.Hold, CasterDecision.Move(14f, Kite, CastRange));  // at the far edge
    }

    // ----- cast pitch -----

    private const float MaxPitch = 1.047f;

    [Fact]
    public void ATallCasterTiltsItsBoltDownOntoTheTarget()
    {
        // Hands 3.9 m up, the target's chest at 1 m, six metres off: level, the bolt clears the
        // target's head by two metres.
        float pitch = CasterDecision.AimPitch(1f - 3.9f, 6f, MaxPitch);

        Assert.True(pitch < 0f);
        Assert.Equal(System.MathF.Atan2(-2.9f, 6f), pitch, 5);
    }

    [Fact]
    public void ATargetUphillIsAimedUpAt()
    {
        Assert.True(CasterDecision.AimPitch(4f, 10f, MaxPitch) > 0f);
        Assert.Equal(0f, CasterDecision.AimPitch(0f, 10f, MaxPitch), 5);
    }

    [Fact]
    public void ThePitchIsHeldWithinItsLimit()
    {
        Assert.Equal(-MaxPitch, CasterDecision.AimPitch(-50f, 1f, MaxPitch), 5);
        Assert.Equal(MaxPitch, CasterDecision.AimPitch(50f, 1f, MaxPitch), 5);
    }

    [Fact]
    public void ATargetWithNoLevelDistanceGetsALevelAim()
    {
        Assert.Equal(0f, CasterDecision.AimPitch(-3f, 0f, MaxPitch), 5);
        Assert.Equal(0f, CasterDecision.AimPitch(float.NaN, 5f, MaxPitch), 5);
    }
}
