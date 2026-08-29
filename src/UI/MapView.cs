using System;
using System.Collections.Generic;
using Embervale.Core.Services;
using Embervale.Economy;
using Embervale.Player;
using Embervale.World;
using Godot;

namespace Embervale.UI;

/// <summary>One drawable marker on the plot. Built by <see cref="MapScreen"/> from discovered
/// locations, so the view never touches a database or a service.</summary>
public readonly record struct MapPin(
    string Id, string Label, Vector2 WorldXz, MapCategory Category, MapTier Tier,
    bool HasTravelNode = false, bool TravelAvailable = false);

/// <summary>A cell's measured ground footprint, in world XZ. The id varies its tone so the realm
/// does not read as a grid of identical tiles.</summary>
public readonly record struct MapLandTile(string CellId, Rect2 Rect);

/// <summary>An authored traversal route in world XZ, derived from a cell presentation path.</summary>
public readonly record struct MapRoadSegment(Vector2 Start, Vector2 End, float Width);

/// <summary>
/// The map plot (Phase 39.5A): the drawing surface and every mouse interaction on it — drag to pan,
/// wheel to zoom, click to select, double-click to zoom in, right-click to drop a waypoint.
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

    /// <summary>This frame's label competition — measured in <see cref="QueueLabel"/>, resolved by
    /// <see cref="LabelPlacer"/>, drawn in <see cref="DrawPlacedLabels"/>. Cleared every `_Draw`.</summary>
    private readonly List<(LabelCandidate Candidate, string Text, Vector2 Origin, Color Colour)> _labels = new();

    private bool _dragging;
    private bool _dragMoved;
    private Vector2 _lastDragAt;
    private Vector2 _cursor;
    private string? _hoverId;

    /// <summary>The current view. <see cref="MapScreen"/> owns the value; this raises
    /// <see cref="ViewChanged"/> whenever the mouse moves it.</summary>
    public MapProjection Projection { get; set; }

    public IReadOnlyList<MapPin> Pins { get; set; } = Array.Empty<MapPin>();

    /// <summary>Categories the player has filtered out.</summary>
    public HashSet<MapCategory> HiddenCategories { get; set; } = new();

    /// <summary>Region name labels, drawn under the markers.</summary>
    public IReadOnlyList<MapMarker> Regions { get; set; } = Array.Empty<MapMarker>();

    /// <summary>Footprints of the cells the player has seen. Drawn as land, so the plot reads as a
    /// place rather than markers floating on a void.</summary>
    public IReadOnlyList<MapLandTile> Land { get; set; } = Array.Empty<MapLandTile>();

    /// <summary>Roads and trails from the actual cell presentation data, never a second map-only
    /// approximation of the world.</summary>
    public IReadOnlyList<MapRoadSegment> Roads { get; set; } = Array.Empty<MapRoadSegment>();

    public string? SelectedId { get; set; }

    /// <summary>The tracked quest objective's destination, ringed so it stands out from every other
    /// pin (39.5C). Null when the tracked objective has no authored place — which is most of them,
    /// because Embervale's hostiles are region-scoped encounters rather than placed actors.</summary>
    public string? ObjectiveId { get; set; }

    public Vector3? Waypoint { get; set; }

    /// <summary>
    /// Drops every label from the plot (39.5B): region lettering, pin names and the hover caption.
    ///
    /// The HUD minimap is the same drawing surface at a fifth of the size, and at that size the
    /// labels are the whole problem — a name is wider than the box it sits in, so six of them
    /// overlap into an unreadable smear over the markers they are meant to identify. Shape and
    /// colour already carry the category (§13), and the full map is one keypress away for the name.
    /// </summary>
    public bool Compact { get; set; }

    /// <summary>
    /// The zoom the <see cref="MapTiers">tier</see> test is taken at, when it must differ from the
    /// zoom the plot is actually drawn at (39.5B).
    ///
    /// Tier-by-zoom is the full map's clutter control and it is the right one there: zoom in, see
    /// more. A minimap cannot use it — it has one fixed zoom, so the rule would either show
    /// settlements only, forever, or every market stall in the district at all times. The minimap
    /// pins the tier test open and culls by <b>distance</b> instead (§20), which is the filter that
    /// actually matches "what is near me".
    /// </summary>
    public float? TierZoom { get; set; }

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
    private bool Emphasized(MapPin pin) => pin.Id == SelectedId || pin.Id == ObjectiveId;

    private bool Shows(MapPin pin) =>
        Emphasized(pin) ||
        (!HiddenCategories.Contains(pin.Category) &&
         MapTiers.VisibleAt(pin.Tier, TierZoom ?? Projection.Zoom));

    // ── Input ─────────────────────────────────────────────────────────────────────────────────

    public override void _GuiInput(InputEvent @event)
    {
        switch (@event)
        {
            case InputEventMouseButton button:
                HandleButton(button);
                return;

            case InputEventMouseMotion motion:
                HandleMotion(motion);
                return;
        }
    }

    private void HandleMotion(InputEventMouseMotion motion)
    {
        _cursor = motion.Position;

        if (_dragging)
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
            return;
        }

        // Hover is what makes a dense plot legible without labelling everything: the name under the
        // cursor appears, and nothing else has to.
        string? hover = PinAt(motion.Position);
        MouseDefaultCursorShape = hover != null ? CursorShape.PointingHand : CursorShape.Arrow;
        if (hover != _hoverId)
        {
            _hoverId = hover;
        }

        QueueRedraw(); // the hover label follows the cursor, so it repaints on every move
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

            // Double-click zooms in about the cursor — the gesture every map in the world has.
            case MouseButton.Left when button.Pressed && button.DoubleClick:
                Zoom(button.Position, 1.8f);
                break;

            case MouseButton.Left when button.Pressed:
            case MouseButton.Middle when button.Pressed:
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

            case MouseButton.Middle when !button.Pressed:
                _dragging = false;
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

    // ── Drawing ───────────────────────────────────────────────────────────────────────────────

    /// <summary>Unmapped ground — the colour under everything.</summary>
    private static Color Deep => new(0.058f, 0.054f, 0.049f);

    /// <summary>Ground the player has walked, before its per-cell tone variation.</summary>
    private static Color Ground => new(0.148f, 0.132f, 0.108f);

    public override void _Draw()
    {
        // Reconcile once per frame, so a resize between frames cannot leave the pins and the
        // graticule disagreeing about where the centre of the map is.
        Projection = Fitted;

        DrawRect(new Rect2(Vector2.Zero, Size), Deep);
        DrawLand();
        DrawRoads();
        DrawGraticule();
        DrawCoastline();

        // Region names sit under the markers, as a cartographer would letter a territory.
        if (!Compact)
        {
            foreach (MapMarker region in Regions)
            {
                Vector2 at = Projection.WorldToScreen(new Vector2(region.X, region.Z));
                DrawLabel(region.Label, at, new Color(UiTheme.Accent, 0.45f), UiTheme.HeaderFontSize);
            }
        }

        DrawSettlementHalos();

        // Weakest tier first, so a settlement is never buried under a stall and the player is never
        // buried under anything — the same overlap-resolves-hierarchy rule the 25E plot had.
        // Shapes first, all three tiers, collecting the labels each one wants rather than drawing
        // them inline (39.5C). A label can only be dropped for colliding with another label if every
        // label is known before any is drawn.
        _labels.Clear();
        DrawPins(MapTier.Detail);
        DrawPins(MapTier.Secondary);
        DrawPins(MapTier.Primary);
        DrawPlacedLabels();

        DrawWaypoint();
        DrawPlayer();
        DrawHoverLabel();
        DrawFrame();
    }

    /// <summary>
    /// The cells the player has seen, drawn as land.
    ///
    /// Each rect is the real measured extent of that cell's ground geometry, not a shape authored for
    /// the map — so the coastline of the known world is the world, and a new cell appears the moment
    /// it is walked into with no cartography step at all.
    ///
    /// ⚠️ <b>Fills only, no per-cell outline.</b> The realm's cells abut on a shared edge by
    /// construction (38F), so stroking each one draws a grid of boxes and the world reads as tiling
    /// rather than as a place — which is exactly how it was reported. The silhouette is drawn once,
    /// by <see cref="DrawCoastline"/>, along the edges no neighbour shares.
    /// </summary>
    private void DrawLand()
    {
        foreach (MapLandTile tile in Land)
        {
            // A stable, tiny tone shift per cell so abutting ground is not one flat wash — the same
            // hand-drawn-map cue that keeps a large area from reading as a single grey rectangle.
            float shade = (((StableRoll.Seed(tile.CellId) % 100u) / 100f) - 0.5f) * 0.035f;
            var tone = new Color(
                Ground.R + shade, Ground.G + (shade * 0.9f), Ground.B + (shade * 0.7f));

            DrawRect(ScreenRect(tile.Rect), tone);
        }
    }

    /// <summary>Draws the physical route network over known land. A dark shoulder and pale core keep
    /// adjoining segments legible at both world-map and minimap zoom without turning them into UI
    /// lines disconnected from the terrain underneath.</summary>
    private void DrawRoads()
    {
        foreach (MapRoadSegment road in Roads)
        {
            Vector2 start = Projection.WorldToScreen(road.Start);
            Vector2 end = Projection.WorldToScreen(road.End);
            float core = Mathf.Clamp(road.Width * Projection.Zoom * 0.55f, 1.25f, 7f);
            DrawLine(start, end, new Color(UiTheme.Engrave, 0.62f), core + 2f, true);
            DrawLine(start, end, new Color(UiTheme.Brass, 0.34f), core, true);
        }
    }

    /// <summary>
    /// The outline of the known world — every land edge that no other cell covers.
    ///
    /// O(n²) over at most fifteen cells, once per redraw. ponytail: a real polygon union is the
    /// correct general answer and would be entirely wasted here.
    /// </summary>
    private void DrawCoastline()
    {
        var ink = new Color(UiTheme.Brass, 0.45f);

        foreach (MapLandTile tile in Land)
        {
            Rect2 r = tile.Rect;
            TryEdge(tile.CellId, new Vector2(r.Position.X, r.Position.Y), new Vector2(r.End.X, r.Position.Y), ink);
            TryEdge(tile.CellId, new Vector2(r.Position.X, r.End.Y), new Vector2(r.End.X, r.End.Y), ink);
            TryEdge(tile.CellId, new Vector2(r.Position.X, r.Position.Y), new Vector2(r.Position.X, r.End.Y), ink);
            TryEdge(tile.CellId, new Vector2(r.End.X, r.Position.Y), new Vector2(r.End.X, r.End.Y), ink);
        }
    }

    /// <summary>Draws a world-space edge unless a DIFFERENT tile's ground already covers its
    /// midpoint — in which case it is an interior seam between two abutting cells, not a coast.</summary>
    private void TryEdge(string ownerCellId, Vector2 worldA, Vector2 worldB, Color ink)
    {
        Vector2 mid = (worldA + worldB) * 0.5f;

        foreach (MapLandTile other in Land)
        {
            if (other.CellId == ownerCellId)
            {
                continue;
            }

            // Grown slightly: two cells abut on a shared edge, so the midpoint sits exactly ON the
            // neighbour's boundary and an exact HasPoint would miss it on the far side.
            if (other.Rect.Grow(0.5f).HasPoint(mid))
            {
                return;
            }
        }

        DrawLine(Projection.WorldToScreen(worldA), Projection.WorldToScreen(worldB), ink, 1.5f);
    }

    /// <summary>A soft halo under each settlement, so a built-up area reads as an area rather than a
    /// dot. Radius is by category, which is the only size information the world actually carries.</summary>
    private void DrawSettlementHalos()
    {
        foreach (MapPin pin in Pins)
        {
            if (!Shows(pin) || MapCategories.GroupOf(pin.Category) != MapGroup.Settlement)
            {
                continue;
            }

            float metres = pin.Category switch
            {
                MapCategory.Capital => 26f,
                MapCategory.Town => 20f,
                MapCategory.Village => 14f,
                MapCategory.Outpost or MapCategory.Camp => 9f,
                _ => 0f,
            };

            if (metres <= 0f)
            {
                continue;
            }

            DrawCircle(
                Projection.WorldToScreen(pin.WorldXz),
                metres * Projection.Zoom,
                new Color(UiTheme.Brass, 0.10f));
        }
    }

    /// <summary>A faint 50 m grid, so panning and zooming have something to read motion against.
    /// Without it a uniform field reads as static no matter how fast you drag it.</summary>
    private void DrawGraticule()
    {
        const float spacing = 50f;
        var ink = new Color(UiTheme.PanelBorder, 0.20f);

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

    private Rect2 ScreenRect(Rect2 world)
    {
        Vector2 a = Projection.WorldToScreen(world.Position);
        Vector2 b = Projection.WorldToScreen(world.End);
        return new Rect2(a, b - a).Abs();
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
            float opacity = Emphasized(pin)
                ? 1f
                : MapTiers.OpacityAt(pin.Tier, TierZoom ?? Projection.Zoom);
            Color colour = new(ColourOf(group), opacity);
            float radius = RadiusOf(tier);
            bool selected = pin.Id == SelectedId;
            bool hovered = pin.Id == _hoverId;

            // The tracked objective is ringed under everything else, so a selection or hover still
            // reads on top of it — the quest marker says "this is where you are going", and the
            // selection ring says "this is what you just clicked". Both can be true of one pin.
            if (pin.Id == ObjectiveId)
            {
                Color quest = UiTheme.Adapt(UiTheme.QuestMain);
                DrawArc(at, radius + 9f, 0f, Mathf.Tau, 28, new Color(quest, 0.9f), 2f);
                DrawArc(at, radius + 12.5f, 0f, Mathf.Tau, 28, new Color(quest, 0.35f), 1f);
            }

            if (selected)
            {
                DrawArc(at, radius + 6f, 0f, Mathf.Tau, 24, UiTheme.AccentHot, 2f);
            }
            else if (hovered)
            {
                DrawArc(at, radius + 5f, 0f, Mathf.Tau, 24, new Color(UiTheme.Text, 0.65f), 1.5f);
            }

            DrawShape(group, at, radius, colour);
            DrawCategoryDetail(pin.Category, at, radius, opacity);
            DrawTravelState(pin, at, radius, opacity);

            // Labels only for what is big enough to earn one: everything at once is the icon soup
            // §50 names. The selection always gets its name; hover gets its own label by the cursor.
            if (!Compact &&
                (tier == MapTier.Primary || selected || Projection.Zoom >= MapTiers.DetailZoom))
            {
                // Queued, not drawn. Rank decides who survives a collision: the selection the player
                // clicked outranks everything, then the hover, then tier — so zooming into a market
                // never costs you the name of the town you are standing in.
                int rank = selected ? 0 : hovered ? 1 : tier switch
                {
                    MapTier.Primary => 2,
                    MapTier.Secondary => 3,
                    _ => 4,
                };

                QueueLabel(pin.Label, at + new Vector2(0f, -(radius + 6f)), colour, rank);
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
        // A dark seat under every marker, so a pin on pale ground still reads as a pin.
        DrawCircle(at, r + 1.5f, new Color(UiTheme.Engrave, 0.55f));

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

    /// <summary>Small interior cuts distinguish the categories players most often confuse while
    /// preserving the six coarse silhouettes at minimap size.</summary>
    private void DrawCategoryDetail(MapCategory category, Vector2 at, float r, float opacity)
    {
        Color ink = new(UiTheme.Engrave, 0.82f * opacity);
        switch (category)
        {
            case MapCategory.Dungeon:
                DrawArc(at + new Vector2(0f, 1f), r * 0.48f, Mathf.Pi, Mathf.Tau, 8, ink, 1.5f);
                DrawLine(at + new Vector2(-r * 0.48f, 1f), at + new Vector2(-r * 0.48f, r * 0.6f), ink, 1.5f);
                DrawLine(at + new Vector2(r * 0.48f, 1f), at + new Vector2(r * 0.48f, r * 0.6f), ink, 1.5f);
                break;
            case MapCategory.Mine:
                DrawLine(at + new Vector2(-r * 0.42f, -r * 0.35f), at + new Vector2(r * 0.42f, r * 0.45f), ink, 1.4f);
                DrawLine(at + new Vector2(r * 0.42f, -r * 0.35f), at + new Vector2(-r * 0.42f, r * 0.45f), ink, 1.4f);
                break;
            case MapCategory.Landmark:
                DrawLine(at + new Vector2(0f, -r * 0.55f), at + new Vector2(0f, r * 0.5f), ink, 1.5f);
                DrawLine(at + new Vector2(-r * 0.32f, -r * 0.2f), at + new Vector2(r * 0.32f, -r * 0.2f), ink, 1.5f);
                break;
            case MapCategory.Waystone:
                DrawColoredPolygon(Regular(at, r * 0.38f, 4, 0f), ink);
                break;
            case MapCategory.Gate:
                DrawLine(at + new Vector2(-r * 0.38f, -r * 0.45f), at + new Vector2(-r * 0.38f, r * 0.45f), ink, 1.5f);
                DrawLine(at + new Vector2(r * 0.38f, -r * 0.45f), at + new Vector2(r * 0.38f, r * 0.45f), ink, 1.5f);
                break;
        }
    }

    /// <summary>Attunement is a state, not a category: a filled spark means usable travel, while a
    /// diagonal cut means the discovered waystone is still unavailable. Shape carries the state so
    /// colour vision is never required.</summary>
    private void DrawTravelState(MapPin pin, Vector2 at, float radius, float opacity)
    {
        if (!pin.HasTravelNode)
        {
            return;
        }

        Vector2 badge = at + new Vector2(radius * 0.78f, radius * 0.78f);
        Color colour = new(UiTheme.Adapt(UiTheme.ArcaneSilver), opacity);
        if (pin.TravelAvailable)
        {
            DrawCircle(badge, 2.2f, colour);
            DrawArc(badge, 3.4f, 0f, Mathf.Tau, 12, new Color(UiTheme.Engrave, opacity), 1f);
        }
        else
        {
            DrawLine(badge + new Vector2(-2.5f, 2.5f), badge + new Vector2(2.5f, -2.5f), colour, 1.7f);
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

        // A ring plus a cross: unmistakably the player's own mark rather than a place.
        DrawArc(at, 10f, 0f, Mathf.Tau, 24, new Color(colour, 0.85f), 1.5f);
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

        DrawCircle(at, 12f, new Color(UiTheme.Text, 0.14f));
        DrawColoredPolygon(tri, UiTheme.Text);
        DrawPolyline(new[] { tri[0], tri[1], tri[2], tri[0] }, UiTheme.Engrave, 1.5f);
    }

    /// <summary>The hovered marker's name beside the cursor — the cheapest way to make a dense plot
    /// readable without labelling every pin at once.</summary>
    private void DrawHoverLabel()
    {
        if (Compact || _hoverId == null || UiTheme.UiFont is not { } font)
        {
            return;
        }

        foreach (MapPin pin in Pins)
        {
            if (pin.Id != _hoverId)
            {
                continue;
            }

            int size = UiTheme.FontSize(UiTheme.BodyFontSize);
            Vector2 measured = font.GetStringSize(pin.Label, HorizontalAlignment.Left, -1, size);
            Vector2 origin = _cursor + new Vector2(14f, -6f);

            // Flip to the other side of the cursor rather than run off the edge of the plot.
            if (origin.X + measured.X + 8f > Size.X)
            {
                origin.X = _cursor.X - measured.X - 14f;
            }

            DrawRect(
                new Rect2(origin - new Vector2(5f, measured.Y - 2f), measured + new Vector2(10f, 6f)),
                new Color(UiTheme.PanelBg, 0.92f));
            DrawString(font, origin, pin.Label, HorizontalAlignment.Left, -1, size, UiTheme.Text);
            return;
        }
    }

    /// <summary>A hairline inside the plot's edge, so the map reads as a framed chart.</summary>
    private void DrawFrame() =>
        DrawRect(new Rect2(Vector2.Zero, Size), new Color(UiTheme.PanelBorder, 0.55f), false, 1f);

    /// <summary>Measures a pin label and adds it to this frame's competition (39.5C).</summary>
    private void QueueLabel(string text, Vector2 at, Color colour, int rank)
    {
        if (UiTheme.UiFont is not { } font || string.IsNullOrEmpty(text))
        {
            return;
        }

        int size = UiTheme.FontSize(UiTheme.CaptionFontSize);
        Vector2 measured = font.GetStringSize(text, HorizontalAlignment.Left, -1, size);
        var origin = new Vector2(at.X - (measured.X * 0.5f), at.Y);

        // DrawString's origin is the text BASELINE, so the box starts a line-height above it.
        var rect = new Rect2(origin.X, origin.Y - measured.Y, measured.X, measured.Y);
        _labels.Add((new LabelCandidate(rect, rank, _labels.Count), text, origin, colour));
    }

    /// <summary>Runs the placer over this frame's labels and draws the survivors.</summary>
    private void DrawPlacedLabels()
    {
        if (_labels.Count == 0)
        {
            return;
        }

        var candidates = new List<LabelCandidate>(_labels.Count);
        foreach ((LabelCandidate candidate, _, _, _) in _labels)
        {
            candidates.Add(candidate);
        }

        foreach (int index in LabelPlacer.Place(candidates, new Rect2(Vector2.Zero, Size)))
        {
            (_, string text, Vector2 origin, Color colour) = _labels[index];
            DrawLabelAt(text, origin, colour, UiTheme.CaptionFontSize);
        }
    }

    /// <summary>
    /// A label on the plot, centred on <paramref name="at"/>.
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
        DrawLabelAt(text, at - new Vector2(measured.X * 0.5f, 0f), colour, sizeToken);
    }

    /// <summary>A label whose left-baseline origin is already decided (the placer's output).</summary>
    private void DrawLabelAt(string text, Vector2 origin, Color colour, int sizeToken)
    {
        if (UiTheme.UiFont is not { } font || string.IsNullOrEmpty(text))
        {
            return;
        }

        int size = UiTheme.FontSize(sizeToken);

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
