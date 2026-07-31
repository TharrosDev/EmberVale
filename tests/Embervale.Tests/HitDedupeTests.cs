using Embervale.Combat;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// The once-per-target rule (Phase 35A). Before hit zones existed every actor had one hurtbox, so
/// per-hurtbox dedupe and per-actor dedupe were indistinguishable. A dragon has four, and the bug
/// this guards against is a single sword arc clipping head, wing and body and billing three times.
/// </summary>
public class HitDedupeTests
{
    // Stand-ins for the entity and its hurtboxes — HitDedupe takes plain objects precisely so this
    // can be tested without constructing a Godot node.
    private static object Owner() => new();

    [Fact]
    public void FourZonesOfOneOwner_HitOnce()
    {
        var dedupe = new HitDedupe();
        object dragon = Owner();

        Assert.True(dedupe.TryHit(dragon, new object()));   // head
        Assert.False(dedupe.TryHit(dragon, new object()));  // wing
        Assert.False(dedupe.TryHit(dragon, new object()));  // body
        Assert.False(dedupe.TryHit(dragon, new object()));  // tail
    }

    [Fact]
    public void SeparateOwners_EachHitOnce()
    {
        var dedupe = new HitDedupe();

        Assert.True(dedupe.TryHit(Owner(), new object()));
        Assert.True(dedupe.TryHit(Owner(), new object()));
    }

    [Fact]
    public void OwnerlessHurtbox_FallsBackToItself()
    {
        // The training dummy in GameBootstrap has a hurtbox with no owning entity; it must still be
        // hittable exactly once, the way it was before this type existed.
        var dedupe = new HitDedupe();
        object dummy = new();

        Assert.True(dedupe.TryHit(null, dummy));
        Assert.False(dedupe.TryHit(null, dummy));
        Assert.True(dedupe.TryHit(null, new object()));
    }

    [Fact]
    public void Clear_ReopensTheWindow()
    {
        var dedupe = new HitDedupe();
        object dragon = Owner();

        Assert.True(dedupe.TryHit(dragon, new object()));
        dedupe.Clear();
        Assert.True(dedupe.TryHit(dragon, new object()));
    }
}
