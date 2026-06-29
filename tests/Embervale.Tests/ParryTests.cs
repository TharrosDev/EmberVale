using Embervale.Combat;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// Covers the pure parry window (Phase 29F): a hit just after the guard rises parries; a held guard does not.
/// </summary>
public class ParryTests
{
    [Fact]
    public void HitInsideWindow_Parries()
    {
        Assert.True(Parry.IsParry(0.0f, 0.2f));
        Assert.True(Parry.IsParry(0.2f, 0.2f));
    }

    [Fact]
    public void HeldGuard_DoesNotParry()
    {
        Assert.False(Parry.IsParry(0.5f, 0.2f));
        Assert.False(Parry.IsParry(2.0f, 0.2f));
    }
}
