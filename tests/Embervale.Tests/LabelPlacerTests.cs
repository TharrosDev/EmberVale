using System.Collections.Generic;
using Embervale.UI;
using Godot;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// The map's label-collision rule (39.5C).
///
/// ⚠️ This exists because the deferred table predicted the wrong defect. It said marker clustering
/// lands when two `Detail` markers overlap — and measured against the real world they never do (the
/// closest pair in the game is 2.13 m apart, 19 px at Detail zoom, against a 4 px pin). **The
/// markers were fine; their 50–70 px labels were not**, and the town hub rendered seven names as one
/// illegible pile. Clustering would have merged the one thing that was working.
/// </summary>
public class LabelPlacerTests
{
    private static readonly Rect2 Plot = new(0f, 0f, 400f, 300f);

    private static LabelCandidate At(float x, float y, int rank, int index, float w = 60f, float h = 12f) =>
        new(new Rect2(x, y, w, h), rank, index);

    [Fact]
    public void KeepsLabelsThatDoNotTouch()
    {
        var kept = LabelPlacer.Place(
            new List<LabelCandidate> { At(10f, 10f, 0, 0), At(200f, 200f, 0, 1) }, Plot);

        Assert.Equal(new[] { 0, 1 }, kept);
    }

    [Fact]
    public void DropsTheLowerPriorityOfAnOverlappingPair()
    {
        // The town-hub case: two names on top of each other. Exactly one survives, and it is the one
        // that matters more — dropping both would lose information the plot had room for.
        var kept = LabelPlacer.Place(
            new List<LabelCandidate> { At(10f, 10f, 4, 0), At(20f, 12f, 2, 1) }, Plot);

        Assert.Equal(new[] { 1 }, kept);
    }

    [Fact]
    public void SelectionOutranksEveryTier()
    {
        // Rank 0 is the pin the player clicked. Zooming into a market must never cost you the name
        // of the thing you just selected.
        var kept = LabelPlacer.Place(
            new List<LabelCandidate> { At(10f, 10f, 2, 0), At(12f, 11f, 0, 1) }, Plot);

        Assert.Equal(new[] { 1 }, kept);
    }

    [Fact]
    public void DropsLabelsThatWouldSpillOutsideThePlot()
    {
        // ⚠️ The plot clips its contents, so an overflowing label is not omitted — it is SLICED.
        // "The Fact" where "The Factor's Rest" belongs reads as corrupted data, not as a tight fit.
        var kept = LabelPlacer.Place(
            new List<LabelCandidate> { At(370f, 100f, 0, 0), At(100f, 100f, 0, 1) }, Plot);

        Assert.Equal(new[] { 1 }, kept);
    }

    [Fact]
    public void DropsLabelsAboveTheTopEdge()
    {
        // A label sits a line-height ABOVE its pin, so a marker near the top edge is the one whose
        // name lands off-plot — the vertical mirror of the case above, and easy to miss.
        var kept = LabelPlacer.Place(new List<LabelCandidate> { At(100f, -4f, 0, 0) }, Plot);
        Assert.Empty(kept);
    }

    [Fact]
    public void TiesBreakOnIndexSoTheResultIsStable()
    {
        // Two equal-rank labels colliding must resolve the same way every frame. A label that
        // flickers as the sort churns is worse than one that is never drawn.
        var candidates = new List<LabelCandidate> { At(10f, 10f, 3, 0), At(14f, 11f, 3, 1) };
        Assert.Equal(LabelPlacer.Place(candidates, Plot), LabelPlacer.Place(candidates, Plot));
        Assert.Equal(new[] { 0 }, LabelPlacer.Place(candidates, Plot));
    }

    [Fact]
    public void ReturnsIndicesInAscendingOrder()
    {
        // The caller indexes its own parallel list with these, so priority order must not leak out.
        var kept = LabelPlacer.Place(
            new List<LabelCandidate> { At(10f, 10f, 9, 0), At(200f, 10f, 1, 1), At(10f, 200f, 5, 2) },
            Plot);

        Assert.Equal(new[] { 0, 1, 2 }, kept);
    }

    [Fact]
    public void EmptyInputDrawsNothing() =>
        Assert.Empty(LabelPlacer.Place(new List<LabelCandidate>(), Plot));

    [Fact]
    public void AdjacentLabelsStillCountAsColliding()
    {
        // Boxes that merely touch would render as one run-on word, so the placer grows each by a
        // couple of pixels before testing. 60 wide at x=10 ends at 70; starting the next at 71 is
        // inside that breathing room.
        var kept = LabelPlacer.Place(
            new List<LabelCandidate> { At(10f, 10f, 0, 0), At(71f, 10f, 1, 1) }, Plot);

        Assert.Equal(new[] { 0 }, kept);
    }
}
