using System.Linq;
using Embervale.Save;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// The slot rosters are a contract with saves already on disk: a manual slot that leaves the roster
/// strands the save in it (still loadable, no longer listed where the player left it), and a slot
/// that changes kind is filed under the wrong heading.
/// </summary>
public class SaveSlotsTests
{
    [Fact]
    public void KindOf_FollowsTheSlotName()
    {
        Assert.Equal(SaveKind.Quick, SaveSlots.KindOf(SaveSlots.Quick));
        Assert.All(SaveSlots.Auto, slot => Assert.Equal(SaveKind.Auto, SaveSlots.KindOf(slot)));
        Assert.All(SaveSlots.Manual, slot => Assert.Equal(SaveKind.Manual, SaveSlots.KindOf(slot)));
        Assert.Equal(SaveKind.Manual, SaveSlots.KindOf("save_audit_0123"));
    }

    [Fact]
    public void Rosters_KeepTheSlotsThatPredateThem()
    {
        Assert.Equal("quick", SaveSlots.Quick);
        Assert.Equal(new[] { "slot1", "slot2", "slot3" }, SaveSlots.Manual.Take(3));
        Assert.Equal(new[] { "auto1", "auto2", "auto3" }, SaveSlots.Auto);
    }

    [Fact]
    public void Rosters_DoNotOverlap()
    {
        string[] all = SaveSlots.Manual.Concat(SaveSlots.Auto).Append(SaveSlots.Quick).ToArray();
        Assert.Equal(all.Length, all.Distinct().Count());
    }
}
