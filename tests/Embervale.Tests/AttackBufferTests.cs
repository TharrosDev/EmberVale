using Embervale.Combat;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// Covers the pure melee input-buffer release rule (Phase 29G): a buffered press fires only once out of
/// the committed window and while it still has time left.
/// </summary>
public class AttackBufferTests
{
    [Fact]
    public void Releases_WhenNotCommitted_AndBufferLive()
    {
        Assert.True(AttackBuffer.ShouldRelease(0.1, committed: false));
    }

    [Fact]
    public void HoldsWhileCommitted()
    {
        Assert.False(AttackBuffer.ShouldRelease(0.1, committed: true));
    }

    [Fact]
    public void ExpiredBuffer_DoesNotRelease()
    {
        Assert.False(AttackBuffer.ShouldRelease(0.0, committed: false));
        Assert.False(AttackBuffer.ShouldRelease(-0.05, committed: false));
    }
}
