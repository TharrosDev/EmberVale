using Embervale.Combat;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// Covers the pure lock-on cycling/range maths (Phase 29H): wrap-around target switching and range gating.
/// </summary>
public class LockOnTests
{
    [Fact]
    public void Cycle_WrapsForward_AndBackward()
    {
        Assert.Equal(1, LockOn.CycleIndex(0, 3, 1));
        Assert.Equal(0, LockOn.CycleIndex(2, 3, 1));   // wraps past the end
        Assert.Equal(2, LockOn.CycleIndex(0, 3, -1));  // wraps past the start
    }

    [Fact]
    public void Cycle_FromNoLock_StartsAtEnd()
    {
        Assert.Equal(0, LockOn.CycleIndex(-1, 3, 1));
        Assert.Equal(2, LockOn.CycleIndex(-1, 3, -1));
        Assert.Equal(-1, LockOn.CycleIndex(-1, 0, 1)); // nothing to lock
    }

    [Fact]
    public void InRange_GatesAtTheBoundary()
    {
        Assert.True(LockOn.InRange(16f, 25f));
        Assert.True(LockOn.InRange(25f, 25f));
        Assert.False(LockOn.InRange(26f, 25f));
    }
}
