using Embervale.Companions;
using Godot;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// Covers the party formation layout (Phase 32A): companions stand behind the player, on alternating
/// shoulders, without stacking on each other.
/// </summary>
public class CompanionFormationTests
{
    private static readonly Vector3 Origin = Vector3.Zero;

    // Godot's forward is -Z, so a player at rest faces down negative Z.
    private static readonly Vector3 Forward = Vector3.Forward;

    [Fact]
    public void FirstSlot_SitsBehindThePlayer()
    {
        Vector3 slot = CompanionFormation.Slot(Origin, Forward, index: 0, distance: 3f);

        // Behind means the +Z side when facing -Z.
        Assert.True(slot.Z > 0f);
        Assert.Equal(3f, slot.Length(), 2);
    }

    [Fact]
    public void AdjacentSlots_TakeOppositeShoulders()
    {
        Vector3 left = CompanionFormation.Slot(Origin, Forward, index: 0, distance: 3f);
        Vector3 right = CompanionFormation.Slot(Origin, Forward, index: 1, distance: 3f);

        Assert.True(left.X * right.X < 0f, "slots 0 and 1 must fall on opposite sides of the player");
    }

    [Fact]
    public void SlotsNeverCoincide()
    {
        var seen = new System.Collections.Generic.List<Vector3>();
        for (int i = 0; i < 6; i++)
        {
            Vector3 slot = CompanionFormation.Slot(Origin, Forward, i, distance: 3f);
            foreach (Vector3 other in seen)
            {
                Assert.True(slot.DistanceTo(other) > 0.5f, $"slot {i} overlaps an earlier slot");
            }

            seen.Add(slot);
        }
    }

    [Fact]
    public void LaterRanks_FallFurtherBack()
    {
        Vector3 rank0 = CompanionFormation.Slot(Origin, Forward, index: 0, distance: 3f);
        Vector3 rank1 = CompanionFormation.Slot(Origin, Forward, index: 2, distance: 3f);

        Assert.True(rank1.Length() > rank0.Length());
    }

    [Fact]
    public void SlotFollowsThePlayersFacing()
    {
        // Facing +X, "behind" becomes -X.
        Vector3 slot = CompanionFormation.Slot(Origin, Vector3.Right, index: 0, distance: 3f);
        Assert.True(slot.X < 0f);
    }

    [Fact]
    public void KeepsThePlayersHeight_AndSurvivesADegenerateFacing()
    {
        var player = new Vector3(10f, 4.5f, -7f);
        Vector3 slot = CompanionFormation.Slot(player, Vector3.Zero, index: 0, distance: 3f);

        Assert.Equal(player.Y, slot.Y, 3);
        Assert.Equal(3f, new Vector3(slot.X - player.X, 0f, slot.Z - player.Z).Length(), 2);
    }

    [Fact]
    public void NegativeIndex_ClampsToTheFirstSlot()
    {
        Assert.Equal(
            CompanionFormation.Slot(Origin, Forward, index: 0, distance: 3f),
            CompanionFormation.Slot(Origin, Forward, index: -3, distance: 3f));
    }
}
