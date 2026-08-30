using Godot;

namespace Embervale.UI;

/// <summary>
/// The world↔screen transform behind the map plot (Phase 39.5A): where the view is centred, how far
/// it is zoomed in, and the arithmetic that turns a world position into a pixel and back.
///
/// Kept engine-free (Godot structs only — no <c>GodotObject</c>, no <c>GD.*</c>) so it is unit
/// testable headlessly, the same way <see cref="CompassMath"/> and
/// <see cref="Player.CameraRigMath"/> are. <c>MapView</c> owns one of these and does nothing with
/// coordinates that this type does not do for it.
///
/// ⚠️ <b>This replaces a fit-to-bounds transform, and that is the point.</b> The Phase 25E map
/// recomputed its extents from the visible markers on every draw, which meant the map had no
/// persistent notion of "where I am looking" — there was nothing to pan, nothing to zoom, and
/// 37.5E had to add a rule that filters must not re-fit the bounds because hiding a pin would
/// otherwise move the whole world under the player's cursor. A view with its own centre and scale
/// has no such failure mode: hiding a pin hides a pin.
///
/// Conventions match the rest of the project: <b>+X is East, −Z is North, and North is up</b>, so
/// screen Y increases with world Z.
/// </summary>
public readonly record struct MapProjection
{
    /// <summary>Closest the view may zoom out, in pixels per world metre. At 1.2 px/m the Ember
    /// Crown's ~300 m span sits inside a 400 px plot, which is the whole-realm view.</summary>
    // ⚠️ THE REALM GOT FIVE TIMES BIGGER AND THESE DID NOT (the 2026-08-29 geography overhaul).
    // The Ember Crown was 210 x 250 m and both regions shared one coordinate space; it is 330 x 440
    // now and Frostfang sits in its own band, so the two together span nearly 800 m of world. At the
    // old floor of 1.2 px/m the whole world no longer fits on a screen, which is the one thing a
    // fully-zoomed-out map has to do.
    public const float MinZoom = 0.7f;

    /// <summary>Closest the view may zoom in. At 40 px/m a market stall is a thumb's width.</summary>
    public const float MaxZoom = 40f;

    /// <summary>The zoom a freshly opened map starts at — a settlement and its neighbours.</summary>
    // Dropped 6 -> 4 for the same reason: at 6 px/m the map opened showing about a third of one
    // district. 4 frames a location together with the country it is approached through, which is
    // what the overhaul made worth looking at — and it is still above MapTiers.SecondaryZoom, so
    // shops and services are visible on open exactly as they were.
    public const float DefaultZoom = 4f;

    /// <summary>World-space point (X, Z) at the centre of the viewport.</summary>
    public Vector2 Center { get; init; }

    /// <summary>Pixels per world metre. Always within [<see cref="MinZoom"/>, <see cref="MaxZoom"/>].</summary>
    public float Zoom { get; init; }

    /// <summary>Size of the control being drawn into, in pixels.</summary>
    public Vector2 Viewport { get; init; }

    public MapProjection(Vector2 center, float zoom, Vector2 viewport)
    {
        Center = center;
        Zoom = Mathf.Clamp(zoom, MinZoom, MaxZoom);
        Viewport = viewport;
    }

    /// <summary>A view centred on a world position at the default zoom.</summary>
    public static MapProjection Centered(Vector2 worldXz, Vector2 viewport) =>
        new(worldXz, DefaultZoom, viewport);

    /// <summary>World (X, Z) to pixel. North (−Z) is up, so screen Y grows with world Z.</summary>
    public Vector2 WorldToScreen(Vector2 worldXz) =>
        ((worldXz - Center) * Zoom) + (Viewport * 0.5f);

    /// <summary>Pixel back to world (X, Z). The exact inverse of <see cref="WorldToScreen"/>.</summary>
    public Vector2 ScreenToWorld(Vector2 pixel) =>
        ((pixel - (Viewport * 0.5f)) / Zoom) + Center;

    /// <summary>The same view moved by a mouse drag, in pixels. Dragging right moves the world right,
    /// so the centre moves left — a map you grab and pull, not a scrollbar.</summary>
    public MapProjection Panned(Vector2 pixelDelta) =>
        this with { Center = Center - (pixelDelta / Zoom) };

    /// <summary>Recentred on a world position, keeping the zoom.</summary>
    public MapProjection CenteredOn(Vector2 worldXz) => this with { Center = worldXz };

    /// <summary>The same view resized, keeping the centre and zoom.</summary>
    public MapProjection Resized(Vector2 viewport) => this with { Viewport = viewport };

    /// <summary>
    /// Zoomed by a multiplicative factor about a pixel, so the world point under the cursor stays
    /// under the cursor. Zooming toward the centre of the screen instead is the thing that makes a
    /// map feel like it is fighting you.
    /// </summary>
    public MapProjection ZoomedAbout(Vector2 pixel, float factor)
    {
        float target = Mathf.Clamp(Zoom * factor, MinZoom, MaxZoom);
        if (Mathf.IsEqualApprox(target, Zoom))
        {
            return this;
        }

        // Solve for the centre that leaves `anchor` projecting to the same pixel at the new zoom.
        Vector2 anchor = ScreenToWorld(pixel);
        Vector2 offset = pixel - (Viewport * 0.5f);
        return this with { Zoom = target, Center = anchor - (offset / target) };
    }

    /// <summary>
    /// Pulled back inside the world's content, with one viewport of slack on each side.
    ///
    /// The slack matters: clamping hard to the content bounds means the player cannot centre a
    /// marker that sits on the edge of the world, which is most of them. This lets the view wander
    /// off the edge by a screen and no further, so the map can always be recovered without a Reset
    /// but can never be lost in empty space — the brief's "do not allow the player to become lost
    /// inside the map".
    /// </summary>
    public MapProjection ClampedTo(Vector2 contentMin, Vector2 contentMax)
    {
        Vector2 slack = Viewport / Zoom;
        return this with
        {
            Center = new Vector2(
                Mathf.Clamp(Center.X, contentMin.X - slack.X, contentMax.X + slack.X),
                Mathf.Clamp(Center.Y, contentMin.Y - slack.Y, contentMax.Y + slack.Y)),
        };
    }
}
