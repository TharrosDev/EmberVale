using System;
using System.Collections.Generic;
using Embervale.Core.Services;
using Embervale.Player;
using Embervale.World;
using Godot;

namespace Embervale.UI;

/// <summary>One drawable marker on the plot. Built by <see cref="MapScreen"/> from discovered
/// locations, so the view never touches a database or a service.</summary>
public readonly record struct MapPin(
    string Id, string Label, Vector2 WorldXz, MapCategory Category, MapTier Tier);

/// <summary>
/// The map plot (Phase 39.5A): the drawing surface and every mouse interaction on it — drag to pan,
/// wheel to zoom, click to select, right-click to drop a waypoint.
///
/// It is deliberately dumb. It holds a <see cref="MapProjection"/> and a list of
/// <see cref="MapPin"/>s it was handed, and it resolves nothing itself: no database lookups, no
/// discovery rules, no filtering decisions. <see cref="MapScreen"/> decides what exists and this
/// decides where it lands in pixels. That split is what lets every interesting rule in the feature
/// live in a testable static class instead of inside a <c>_Draw</c> override.
///
/// ⚠️ <b>Markers are separated by shape first and colour second</b> (§13). Six group silhouettes —
/// circle, square, diamond, triangle, hexagon, cross — carry the meaning, and colour agrees with
/// them rather than doing the work alone; every colour goes through <see cref="UiTheme.Adapt"/>, so
/// the whole language survives <see cref="ColorVision"/> being on.
/// </summary>
public partial class MapView : Control
{
    /// <summary>How near a click must land to select a pin, in pixels.</summary>
    private const float PickRadius = 18f;

    /// <summary>Drag beyond this and the release is a pan, not a click. Without it, a click with a
    /// two-pixel wobble selects nothing and the map feels unresponsive rather than precise.</summary>
    private const float DragSlop = 4f;

    private bool _dragging;
    private bool _dragMoved;
    private Vector2 _lastDragAt;

    /// <summary>The current view. <see cref="MapScreen"/> owns the value; this raises
    /// <see cref="ViewChanged"/> whenever the mouse moves it.</summary>
    public MapProjection Projection { get; set; }

    public IReadOnlyList<MapPin> Pins { get; set; } = Array.Empty<MapPin>();

    /// <summary>Categories the player has filtered out.</summary>
    public HashSet<MapCategory> HiddenCategories { get; set; } = new();

    /// <summary>Region name labels, drawn under everything at low zoom.</summary>
    public IReadOnlyList<MapMarker> Regions { get; set; } = Array.Empty<MapMarker>();

    /// <summary>World-space XZ footprints of the cells the player has seen. Drawn as land, so the
    /// plot reads as a place rather than markers floating on a void.</summary>
    public IReadOnlyList<Rect2> Land { get; set; } = Array.Empty<Rect2>();

    public string? SelectedId { get; set; }

    public Vector3? Waypoint { get; set; }

    /// <summary>Raised when a drag or a wheel moved the view.</summary>
    public event Action<MapProjection>? ViewChanged;

    /// <summary>Raised with a pin id on click, or null when the click hit empty ground.</summary>
    public event Action<string?>? Picked;

    /// <summary>Raised with a world position on right-click.</summary>
    public event Action<Vector3>? WaypointRequested;

    public MapView()
    {
        MouseFilter = MouseFilterEnum.Stop;
        ClipContents = true;
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        SizeFlagsVertical = SizeFlags.ExpandFill;
        Resized += QueueRedraw;
    }

    /// <summary>
    /// The projection with its viewport reconciled to this control's real size.
    ///
    /// ⚠️ <b>EVERYTHING must go through this rather than <see cref="Projection"/> directly.</b>
    /// A <see cref="MapProjection"/> is a value the screen owns and hands over; it is constructed
    /// before any layout has happened, when the control's size is still meaningless, and
    /// <see cref="MapProjection.WorldToScreen"/> centres on <c>Viewport * 0.5</c>. Reading the stale
    /// value therefore projects the whole world about a half-pixel origin at the top-left corner, so
    /// every marker lands off-screen and is culled — and the map draws nothing but the region
    /// lettering, which is not a marker and so looks like "discovery is broken" rather than "the
    /// transform is wrong". That is exactly what shipped for the first hour of this sub-phase.
    /// </summary>
    private MapProjection Fitted =>
        Projection.Viewport.IsEqualApprox(Size) ? Projection : Projection.Resized(Size);

    /// <summary>True when a category passes the filter and its tier is visible at this zoom.</summary>
    private bool Shows(MapPin pin) =>
        !HiddenCategories.Contains(pin.Category) && MapTiers.VisibleAt(pin.Tier, Projection.Zoom);

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton button)
        {
            HandleButton(button);
            return;
        }

        if (@event is InputEventMouseMotion motion && _dragging)
        {
            Vector2 delta = motion.Position - _lastDragAt;
            _lastDragAt = motion.Position;
            if (delta.LengthSquared() > 0f)
            {
                _dragMoved |= delta.Length() > DragSlop;
                Projection = Fitted.Panned(delta);
                ViewChanged?.Invoke(Projection);
                QueueRedraw();
            }

            AcceptEvent();
        }
    }

    private void HandleButton(InputEventMouseButton button)
    {
        switch (button.ButtonIndex)
        {
            case MouseButton.WheelUp:
                Zoom(button.Position, 1.15f);
                break;

            case MouseButton.WheelDown:
                Zoom(button.Position, 1f / 1.15f);
                break;

            case MouseButton.Left when button.Pressed:
                _dragging = true;
                _dragMoved = false;
                _lastDragAt = button.Position;
                AcceptEvent();
                break;

            case MouseButton.Left when !button.Pressed:
                _dragging = false;
                if (!_dragMoved)
                {
                    Picked?.Invoke(PinAt(button.Position));
                }

                AcceptEvent();
                break;

            case MouseButton.Right when button.Pressed:
                Vector2 world = Fitted.ScreenToWorld(button.Position);
                WaypointRequested?.Invoke(new Vector3(world.X, 0f, world.Y));
                AcceptEvent();
                break;
        }
    }

    private void Zoom(Vector2 at, float factor)
    {
        Projection = Fitted.ZoomedAbout(at, factor);
        ViewChanged?.Invoke(Projection);
        QueueRedraw();
        AcceptEvent();
    }

    /// <summary>The nearest visible pin within <see cref="PickRadius"/> of a pixel, or null.
    /// Nearest rather than first so overlapping markers pick the one actually under the cursor.</summary>
    private string? PinAt(Vector2 pixel)
    {
        string? best = null;
        float bestDistance = PickRadius * PickRadius;

        foreach (MapPin pin in Pins)
        {
            if (!Shows(pin))
            {
                continue;
            }

            float distance = Fitted.WorldToScreen(pin.WorldXz).DistanceSquaredTo(pixel);
            if (distance <= bestDistance)
            {
                bestDistance = distance;
                best = pin.Id;
            }
        }

        return best;
    }

    public override void _Draw()
    {
        // Reconcile once per frame, so a resize between frames cannot leave the pins and the
        // graticule disagreeing about where the centre of the map is.
        Projection = Fitted;

        // Sea first, then the land the player has walked, then the grid over both. A parchment
        // warmth rather than the plain well colour: an unlettered dark rectangle reads as a screen
        // that failed to load, which is exactly how it was reported.
        DrawRect(new Rect2(Vector2.Zero, Size), Deep);
        DrawLand();
        DrawGraticule();

        // Region names sit under the markers, as a cartographer would letter a territory.
        foreach (MapMarker region in Regions)
        {
            Vector2 at = Projection.WorldToScreen(new Vector2(region.X, region.Z));
            DrawLabel(region.Label, at, new Color(UiTheme.Accent, 0.55f), UiTheme.HeaderFontSize);
        }

        // Weakest tier first, so a settlement is never buried under a stall and the player is never
        // buried under anything — the same overlap-resolves-hierarchy rule the 25E plot had.
        DrawPins(MapTier.Detail);
        DrawPins(MapTier.Secondary);
        DrawPins(MapTier.Primary);

        DrawWaypoint();
        DrawPlayer();
    }

    /// <summary>Unmapped ground — the colour under everything.</summary>
    private static Color Deep => new(0.055f, 0.052f, 0.048f);

    /// <summary>Ground the player has been to.</summary>
    private static Color Ground => new(0.128f, 0.116f, 0.098f);

    /// <summary>
    /// The cells the player has seen, drawn as land.
    ///
    /// Each rect is the real measured extent of that cell's ground geometry, not a shape authored
    /// for the map — so the coastline of the known world is the world, and a new cell appears on the
    /// map the moment it is walked into with no cartography step at all.
    /// </summary>
    private void DrawLand()
    {
        foreach (Rect2 cell in Land)
        {
            Vector2 a = Projection.WorldToScreen(cell.Position);
            Vector2 b = Projection.WorldToScreen(cell.End);
            var screen = new Rect2(a, b - a).Abs();

            DrawRect(screen, Ground);
            DrawRect(screen, new Color(UiTheme.Brass, 0.22f), false, 1f);
        }
    }

    /// <summary>A faint 50 m grid, so panning and zooming have something to read motion against.
    /// Without it a uniform field reads as static no matter how fast you drag it.</summary>
    private void DrawGraticule()
    {
        const float spacing = 50f;
        var ink = new Color(UiTheme.PanelBorder, 0.22f);

        Vector2 topLeft = Projection.ScreenToWorld(Vector2.Zero);
        Vector2 bottomRight = Projection.ScreenToWorld(Size);

        // Skip when a line would land every few pixels — a solid wash of grid is worse than none.
        if ((bottomRight.X - topLeft.X) / spacing > 60f)
        {
            return;
        }

        for (float x = Mathf.Floor(topLeft.X / spacing) * spacing; x <= bottomRight.X; x += spacing)
        {
            float px = Projection.WorldToScreen(new Vector2(x, 0f)).X;
            DrawLine(new Vector2(px, 0f), new Vector2(px, Size.Y), ink);
        }

        for (float z = Mathf.Floor(topLeft.Y / spacing) * spacing; z <= bottomRight.Y; z += spacing)
        {
            float py = Projection.WorldToScreen(new Vector2(0f, z)).Y;
            DrawLine(new Vector2(0f, py), new Vector2(Size.X, py), ink);
        }
    }

    private void DrawPins(MapTier tier)
    {
        foreach (MapPin pin in Pins)
        {
            if (pin.Tier != tier || !Shows(pin))
            {
                continue;
            }

            Vector2 at = Projection.WorldToScreen(pin.WorldXz);
            if (at.X < -40f || at.Y < -40f || at.X > Size.X + 40f || at.Y > Size.Y + 40f)
            {
                continue; // culled: off-screen markers cost nothing to skip and everything to draw
            }

            MapGroup group = MapCategories.GroupOf(pin.Category);
            Color colour = ColourOf(group);
            float radius = RadiusOf(tier);
            bool selected = pin.Id == SelectedId;

            if (selected)
            {
                DrawArc(at, radius + 6f, 0f, Mathf.Tau, 24, UiTheme.AccentHot, 2f);
            }

            DrawShape(group, at, radius, colour);

            // Labels only for what is big enough to earn one: everything at once is the icon soup
            // the brief's §50 names. The selection always gets its name, whatever its tier.
            if (tier == MapTier.Primary || selected || Projection.Zoom >= MapTiers.DetailZoom)
            {
                DrawLabel(pin.Label, at + new Vector2(0f, -(radius + 6f)), colour, UiTheme.CaptionFontSize);
            }
        }
    }

    private static float RadiusOf(MapTier tier) => tier switch
    {
        MapTier.Primary => 7.5f,
        MapTier.Secondary => 5.5f,
        _ => 4f,
    };

    private static Color ColourOf(MapGroup group) => UiTheme.Adapt(group switch
    {
        MapGroup.Settlement => UiTheme.Accent,
        MapGroup.Trade => UiTheme.Brass,
        MapGroup.Service => UiTheme.GlyphLight,
        MapGroup.Exploration => UiTheme.AccentHot,
        MapGroup.Travel => UiTheme.ArcaneSilver,
        _ => UiTheme.Text,
    });

    /// <summary>The group's silhouette. Shape carries the meaning; colour agrees with it.</summary>
    private void DrawShape(MapGroup group, Vector2 at, float r, Color colour)
    {
        switch (group)
        {
            case MapGroup.Settlement:
                DrawCircle(at, r, colour);
                DrawArc(at, r + 2.5f, 0f, Mathf.Tau, 20, new Color(colour, 0.45f), 1.5f);
                break;

            case MapGroup.Trade:
                DrawRect(new Rect2(at - new Vector2(r, r), new Vector2(r * 2f, r * 2f)), colour);
                break;

            case MapGroup.Service:
                DrawColoredPolygon(Regular(at, r * 1.25f, 4, Mathf.Pi / 4f), colour);
                break;

            case MapGroup.Exploration:
                DrawColoredPolygon(Regular(at, r * 1.3f, 3, 0f), colour);
                break;

            case MapGroup.Travel:
                DrawColoredPolygon(Regular(at, r * 1.15f, 6, 0f), colour);
                break;

            default:
                DrawCross(at, r, colour);
                break;
        }
    }

    /// <summary>A regular n-gon, first vertex at <paramref name="phase"/> from straight up.</summary>
    private static Vector2[] Regular(Vector2 at, float r, int sides, float phase)
    {
        var points = new Vector2[sides];
        for (int i = 0; i < sides; i++)
        {
            float a = phase + (Mathf.Tau * i / sides);
            points[i] = at + new Vector2(Mathf.Sin(a) * r, -Mathf.Cos(a) * r);
        }

        return points;
    }

    private void DrawCross(Vector2 at, float r, Color colour)
    {
        DrawLine(at + new Vector2(-r, -r), at + new Vector2(r, r), colour, 2.5f);
        DrawLine(at + new Vector2(-r, r), at + new Vector2(r, -r), colour, 2.5f);
    }

    private void DrawWaypoint()
    {
        if (Waypoint is not { } waypoint)
        {
            return;
        }

        Vector2 at = Projection.WorldToScreen(new Vector2(waypoint.X, waypoint.Z));
        Color colour = UiTheme.Adapt(UiTheme.AccentHot);

        // A dashed ring plus a cross: unmistakably the player's own mark rather than a place.
        DrawArc(at, 10f, 0f, Mathf.Tau, 24, new Color(colour, 0.75f), 1.5f);
        DrawCross(at, 5f, colour);
    }

    /// <summary>
    /// The player as an arrow pointing where they are facing, not a dot.
    ///
    /// Orientation is the single thing that makes a map usable while walking — without it the player
    /// has to move to work out which way "up" is on the plot, which is exactly the moment they opened
    /// the map to avoid. (Kept verbatim from 37.5E, which is where this was learned.)
    /// </summary>
    private void DrawPlayer()
    {
        if (ResolvePlayer() is not var (position, yaw))
        {
            return;
        }

        Vector2 at = Projection.WorldToScreen(new Vector2(position.X, position.Z));

        // Godot's −Z is forward and the plot puts −Z at the top, so a yaw of 0 must point up.
        float a = -yaw;
        var forward = new Vector2(Mathf.Sin(a), -Mathf.Cos(a));
        var right = new Vector2(-forward.Y, forward.X);

        Vector2[] tri =
        {
            at + (forward * 10f),
            at - (forward * 5.5f) + (right * 6f),
            at - (forward * 5.5f) - (right * 6f),
        };

        DrawColoredPolygon(tri, UiTheme.Text);
        DrawPolyline(new[] { tri[0], tri[1], tri[2], tri[0] }, UiTheme.Engrave, 1.5f);
    }

    /// <summary>
    /// A label on the plot.
    ///
    /// Falls back to drawing nothing rather than throwing if the UI face is unavailable: a nameless
    /// map is a degraded map, a crashed one is no map. (37.5E.)
    /// </summary>
    private void DrawLabel(string text, Vector2 at, Color colour, int sizeToken)
    {
        if (UiTheme.UiFont is not { } font || string.IsNullOrEmpty(text))
        {
            return;
        }

        int size = UiTheme.FontSize(sizeToken);
        Vector2 measured = font.GetStringSize(text, HorizontalAlignment.Left, -1, size);
        Vector2 origin = at - new Vector2(measured.X * 0.5f, 0f);

        // Drawn once dark and offset, then in colour: a plot label crosses terrain of every value,
        // and an unshadowed one disappears over its own marker.
        DrawString(font, origin + Vector2.One, text, HorizontalAlignment.Left, -1, size, UiTheme.Engrave);
        DrawString(font, origin, text, HorizontalAlignment.Left, -1, size, colour);
    }

    private static (Vector3 Position, float Yaw)? ResolvePlayer() =>
        ServiceLocator.Instance is { } locator && locator.TryGet(out PlayerCharacter player)
            ? (player.GlobalPosition, player.GlobalRotation.Y)
            : null;
}
