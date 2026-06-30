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
}
