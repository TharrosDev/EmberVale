using Embervale.UI;
using Godot;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// The world↔screen transform behind the Phase 39.5A map plot. Panning, zooming and centring are
/// the difference between a map you can read and the fit-to-bounds picture 25E drew, and all three
/// are pure arithmetic, so they are pinned here rather than discovered by dragging in-game.
/// </summary>
public class MapProjectionTests
{
    private static readonly Vector2 Viewport = new(800f, 600f);

    private static MapProjection At(float x, float z, float zoom) =>
        new(new Vector2(x, z), zoom, Viewport);

    [Fact]
    public void Center_ProjectsToViewportCentre()
    {
        MapProjection p = At(50f, -20f, 6f);
        Assert.Equal(new Vector2(400f, 300f), p.WorldToScreen(new Vector2(50f, -20f)));
    }

    [Fact]
    public void NorthIsUp_AndEastIsRight()
    {
        MapProjection p = At(0f, 0f, 4f);

        // North is -Z, and must land ABOVE the centre (smaller screen Y).
        Assert.True(p.WorldToScreen(new Vector2(0f, -10f)).Y < 300f);

        // East is +X, and must land to the RIGHT of the centre.
        Assert.True(p.WorldToScreen(new Vector2(10f, 0f)).X > 400f);
    }

    [Fact]
    public void ScreenToWorld_InvertsWorldToScreen()
    {
        MapProjection p = At(12f, -34f, 7.5f);
        var world = new Vector2(90f, 15f);

        Vector2 round = p.ScreenToWorld(p.WorldToScreen(world));

        Assert.Equal(world.X, round.X, 3);
        Assert.Equal(world.Y, round.Y, 3);
    }

    [Fact]
    public void Zoom_ScalesDistanceInPixels()
    {
        Vector2 a = At(0f, 0f, 5f).WorldToScreen(new Vector2(10f, 0f));
        Vector2 b = At(0f, 0f, 10f).WorldToScreen(new Vector2(10f, 0f));

        Assert.Equal(50f, a.X - 400f, 3);
        Assert.Equal(100f, b.X - 400f, 3);
    }

    [Fact]
    public void ZoomedAbout_KeepsTheWorldPointUnderTheCursor()
    {
        MapProjection p = At(0f, 0f, 5f);
        var cursor = new Vector2(650f, 120f);          // deliberately not the centre
        Vector2 before = p.ScreenToWorld(cursor);

        MapProjection zoomed = p.ZoomedAbout(cursor, 2f);
        Vector2 after = zoomed.ScreenToWorld(cursor);

        Assert.Equal(before.X, after.X, 3);
        Assert.Equal(before.Y, after.Y, 3);
        Assert.Equal(10f, zoomed.Zoom, 3);
    }

    [Fact]
    public void ZoomedAbout_ClampsAndThenStopsMovingTheView()
    {
        MapProjection p = At(0f, 0f, MapProjection.MaxZoom);
        MapProjection further = p.ZoomedAbout(new Vector2(10f, 10f), 4f);

        // Already at the ceiling: the call must be a no-op, not a pan with no zoom.
        Assert.Equal(MapProjection.MaxZoom, further.Zoom, 3);
        Assert.Equal(p.Center, further.Center);
    }

    [Fact]
    public void Zoom_IsClampedBothWays()
    {
        Assert.Equal(MapProjection.MinZoom, At(0f, 0f, 0.001f).Zoom, 3);
        Assert.Equal(MapProjection.MaxZoom, At(0f, 0f, 9000f).Zoom, 3);
    }

    [Fact]
    public void Panned_MovesTheWorldWithTheDrag()
    {
        MapProjection p = At(0f, 0f, 10f);

        // Drag 100 px right at 10 px/m: the world slides 10 m right, so the centre moves 10 m left.
        MapProjection dragged = p.Panned(new Vector2(100f, 0f));

        Assert.Equal(-10f, dragged.Center.X, 3);
    }

    [Fact]
    public void Panned_IsReversible()
    {
        MapProjection p = At(5f, 5f, 8f);
        MapProjection there = p.Panned(new Vector2(37f, -19f)).Panned(new Vector2(-37f, 19f));

        Assert.Equal(p.Center.X, there.Center.X, 3);
        Assert.Equal(p.Center.Y, there.Center.Y, 3);
    }

    [Fact]
    public void CenteredOn_MovesTheCentreAndKeepsTheZoom()
    {
        MapProjection p = At(0f, 0f, 12f).CenteredOn(new Vector2(-40f, 60f));

        Assert.Equal(new Vector2(-40f, 60f), p.Center);
        Assert.Equal(12f, p.Zoom, 3);
    }

    [Fact]
    public void Resized_KeepsCentreAndZoom_AndRecentresTheProjection()
    {
        MapProjection built = At(20f, -40f, 8f) with { Viewport = Vector2.One };
        MapProjection fitted = built.Resized(Viewport);

        Assert.Equal(built.Center, fitted.Center);
        Assert.Equal(built.Zoom, fitted.Zoom, 3);

        // The centre of the world must land in the middle of the PLOT, not half a pixel from its
        // top-left corner.
        Assert.Equal(new Vector2(400f, 300f), fitted.WorldToScreen(built.Center));
    }

    [Fact]
    public void AnUnfittedViewportProjectsEverythingIntoTheCorner()
    {
        // The 39.5A shipping bug, pinned so it cannot come back quietly: a projection built before
        // layout carries Viewport = (1,1), so WorldToScreen centres on half a pixel and every marker
        // lands off the top-left corner and is culled. MapView.Fitted exists to prevent this, and
        // this test is what makes the difference between the two states observable.
        MapProjection unfitted = At(0f, 0f, 6f) with { Viewport = Vector2.One };

        Assert.Equal(new Vector2(0.5f, 0.5f), unfitted.WorldToScreen(Vector2.Zero));
        Assert.Equal(new Vector2(400f, 300f), unfitted.Resized(Viewport).WorldToScreen(Vector2.Zero));
    }

    [Fact]
    public void ClampedTo_LeavesAViewInsideTheContentAlone()
    {
        MapProjection p = At(0f, 0f, 6f);
        MapProjection clamped = p.ClampedTo(new Vector2(-100f, -100f), new Vector2(100f, 100f));

        Assert.Equal(p.Center, clamped.Center);
    }

    [Fact]
    public void ClampedTo_PullsAStrandedViewBackWithinOneScreenOfTheContent()
    {
        MapProjection p = At(9000f, 0f, 6f);
        MapProjection clamped = p.ClampedTo(new Vector2(-100f, -100f), new Vector2(100f, 100f));

        // Allowed to overshoot by a viewport's worth of world, and no further.
        float slack = Viewport.X / 6f;
        Assert.Equal(100f + slack, clamped.Center.X, 3);
    }
}
