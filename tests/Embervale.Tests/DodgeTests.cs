using Embervale.Combat;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// Covers the pure dodge gating + i-frame window (Phase 29E).
/// </summary>
public class DodgeTests
{
    [Fact]
    public void IFrames_OpenOnlyInsideTheWindow()
    {
        // window [0.05, 0.35)
        Assert.False(Dodge.IsInvulnerable(0.0f, 0.05f, 0.35f));  // startup
        Assert.True(Dodge.IsInvulnerable(0.1f, 0.05f, 0.35f));   // mid-roll
        Assert.False(Dodge.IsInvulnerable(0.4f, 0.05f, 0.35f));  // recovery
    }

    [Fact]
    public void CanStart_RequiresGroundStaminaAndNoLock()
    {
        Assert.True(Dodge.CanStart(grounded: true, stamina: 30f, cost: 22f, rolling: false, staggered: false));
        Assert.False(Dodge.CanStart(grounded: false, stamina: 30f, cost: 22f, rolling: false, staggered: false));
        Assert.False(Dodge.CanStart(grounded: true, stamina: 10f, cost: 22f, rolling: false, staggered: false));
        Assert.False(Dodge.CanStart(grounded: true, stamina: 30f, cost: 22f, rolling: true, staggered: false));
        Assert.False(Dodge.CanStart(grounded: true, stamina: 30f, cost: 22f, rolling: false, staggered: true));
    }
}
