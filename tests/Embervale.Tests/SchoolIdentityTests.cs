using Embervale.Magic;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// Pure school-identity maths (Phase 29.5B): Necrotic lifesteal and Lightning chain fractions, plus
/// the Fire ignite stack cap. The on-hit wiring (freeze escalation, the chain physics query) is
/// Godot-bound and verified by build/run, not here.
/// </summary>
public class SchoolIdentityTests
{
    [Fact]
    public void Lifesteal_IsAShareOfDamage_AndNeverNegative()
    {
        Assert.Equal(35f, SchoolIdentity.LifestealAmount(100f), 3);
        Assert.Equal(0f, SchoolIdentity.LifestealAmount(-20f), 3);
    }

    [Fact]
    public void ChainArc_IsHalfDamage_AndNeverNegative()
    {
        Assert.Equal(50f, SchoolIdentity.ChainDamage(100f), 3);
        Assert.Equal(0f, SchoolIdentity.ChainDamage(-5f), 3);
    }

    [Fact]
    public void IgniteStacks_ClimbToTheCap_ThenHold()
    {
        Assert.Equal(1, StatusMath.NextStack(0, 5));
        Assert.Equal(5, StatusMath.NextStack(4, 5));
        Assert.Equal(5, StatusMath.NextStack(5, 5)); // capped
        Assert.Equal(1, StatusMath.NextStack(3, 1)); // non-stacking effect stays at 1
    }
}
