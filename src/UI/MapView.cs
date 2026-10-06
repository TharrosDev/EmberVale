using System;
using System.Collections.Generic;
using Embervale.Core.Services;
using Embervale.Economy;
using Embervale.Localization;
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
    private static Texture2D? _mapMaterial;

    /// <summary>How near a click must land to select a pin, in pixels.</summary>
    private const float PickRadius = 18f;

    /// <summary>Drag beyond this and the release is a pan, not a click. Without it, a click with a
    /// two-pixel wobble selects nothing and the map feels unresponsive rather than precise.</summary>
    private const float DragSlop = 4f;

    /// <summary>This frame's label competition — measured in <see cref="QueueLabel"/>, resolved by
    /// <see cref="LabelPlacer"/>, drawn in <see cref="DrawPlacedLabels"/>. Cleared every `_Draw`.</summary>
    private readonly List<(LabelCandidate Candidate, string Text, Vector2 Origin, Color Colour)> _labels = new();

    // What a label may not be drawn over, in plot pixels, rebuilt with the labels on every draw:
    // the square round the player's arrow, which a pin's name steps off, and for a territory's
    // lettering that square, the markers and the kept pin names. The candidate list is the
    // placer's input, reused between draws.
    private Rect2? _playerClear;
    private readonly List<Rect2> _letteringBlocks = new();
    private readonly List<LabelCandidate> _candidates = new();

    // Reused by PinNear, so a pan that re-asks for the snap every frame allocates nothing.
    private readonly List<Vector2> _pickPoints = new();
    private readonly List<string> _pickIds = new();

    // The scale bar's caption, rebuilt only when the bar changes length.
    private int _scaleMetres = -1;
    private string _scaleText = string.Empty;

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

    /// <summary>The active region's shaded relief and the world rectangle it covers
    /// (<see cref="MapCartography.Relief"/>). When present it IS the land layer, and the per-cell
    /// fills and coastline — which drew the streaming lattice — are not drawn.</summary>
    public (Texture2D Texture, Rect2 World)? Relief { get; set; }

    /// <summary>Roads and trails from the actual cell presentation data, never a second map-only
    /// approximation of the world.</summary>
    public IReadOnlyList<MapRoadSegment> Roads { get; set; } = Array.Empty<MapRoadSegment>();

    public string? SelectedId { get; set; }

    /// <summary>The tracked quest objective's destination, ringed so it stands out from every other
    /// pin (39.5C). Null when the tracked objective has no authored place — which is most of them,
    /// because Embervale's hostiles are region-scoped encounters rather than placed actors.</summary>
    public string? ObjectiveId { get; set; }

    private readonly Dictionary<string, QuestPin> _questById = new();
    private IReadOnlyList<QuestPin> _questPins = Array.Empty<QuestPin>();

    /// <summary>
    /// The current objective's place for every live non-ledger quest (<see cref="MapQuestPinRules.Pins"/>).
    /// Each is drawn at full strength with a small badge - a filled ember diamond for the main thread, an
    /// outlined one for an errand - and only the tracked quest's pin also gets the ring. Shape and fill carry
    /// main versus side first and colour second, so the pair survives every colour-vision setting.
    /// </summary>
    public IReadOnlyList<QuestPin> QuestPins
    {
        get => _questPins;
        set
        {
            _questPins = value;
            _questById.Clear();
            foreach (QuestPin pin in value)
            {
                _questById[pin.LocationId] = pin;
            }
        }
    }

    public Vector3? Waypoint { get; set; }

    /// <summary>
    /// Draws the gamepad cursor: a reticle at the centre of the plot, which is where a pad "points"
    /// (the right stick moves the map under it). <see cref="MapScreen"/> turns it on while a pad is
    /// in use; a mouse has its own pointer and its own hover label.
    /// </summary>
    public bool ShowCursor { get; set; }

    /// <summary>The pin the cursor has snapped to (<see cref="MapSnapRules.SnapRadius"/>), ringed
    /// and named. Null when nothing is near enough.</summary>
    public string? SnapId { get; set; }

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
        _mapMaterial ??= GD.Load<Texture2D>("res://assets/ui/materials/map_ash_vellum.png");
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
    private bool Emphasized(MapPin pin) =>
        pin.Id == SelectedId || pin.Id == ObjectiveId || _questById.ContainsKey(pin.Id);

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
    private string? PinAt(Vector2 pixel) => PinNear(pixel, PickRadius);

    /// <summary>The nearest pin this plot is drawing within <paramref name="radius"/> pixels of
    /// <paramref name="pixel"/>, or null. The mouse asks with <see cref="PickRadius"/>; the gamepad
    /// cursor asks with the wider snap radius.</summary>
    public string? PinNear(Vector2 pixel, float radius)
    {
        _pickPoints.Clear();
        _pickIds.Clear();
        MapProjection fitted = Fitted;
        foreach (MapPin pin in Pins)
        {
            if (Shows(pin))
            {
                _pickPoints.Add(fitted.WorldToScreen(pin.WorldXz));
                _pickIds.Add(pin.Id);
            }
        }

        int index = MapSnapRules.Nearest(_pickPoints, pixel, radius);
        return index < 0 ? null : _pickIds[index];
    }

    // ── Drawing ───────────────────────────────────────────────────────────────────────────────

    /// <summary>Half the side of the square kept clear round the player's arrow.</summary>
    private const float PlayerClearance = 15f;

    /// <summary>The width of the dark outline round every word on the plot, in pixels.</summary>
    private const int LabelKeyline = 4;

    /// <summary>How far a territory's name steps down, then up, from its anchor to find clear ground.</summary>
    private static readonly float[] LetteringSteps = { 0f, 1f, -1f, 2f, -2f };

    public override void _Draw()
    {
        // Reconcile once per frame, so a resize between frames cannot leave the pins and the
        // graticule disagreeing about where the centre of the map is.
        Projection = Fitted;

        DrawRect(new Rect2(Vector2.Zero, Size), UiTheme.MapDeep);
        if (_mapMaterial is { } material)
        {
            DrawTextureRect(material, new Rect2(Vector2.Zero, Size), true, UiTheme.MapVellum);
        }
        if (Relief is { } relief)
        {
            DrawTextureRect(relief.Texture, ScreenRect(relief.World), false);
            DrawRoads();

            // The full map lets the terrain read on its own: shaded relief already gives a pan
            // something to move against, and a grid over it is one more layer between the player
            // and the ground. The minimap keeps its grid.
            if (Compact)
            {
                DrawGraticule();
            }
        }
        else
        {
            DrawLand();
            DrawRoads();
            DrawGraticule();
            DrawCoastline();
        }

        DrawSettlementHalos();

        // The player's arrow is the one mark no name may cover: a pin's name steps off it and a
        // territory's lettering is placed round it.
        _playerClear = null;
        _letteringBlocks.Clear();
        if (ResolvePlayer() is var (position, _))
        {
            Vector2 here = Projection.WorldToScreen(new Vector2(position.X, position.Z));
            var clear = new Rect2(
                here - new Vector2(PlayerClearance, PlayerClearance),
                new Vector2(PlayerClearance * 2f, PlayerClearance * 2f));
            _playerClear = clear;
            _letteringBlocks.Add(clear);
        }

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

        // A territory's name goes on after the markers and their names and gives way to all of
        // them: it is lettered on whatever ground they left clear.
        if (!Compact)
        {
            DrawLettering();
        }

        // The arrow goes over everything that is part of the chart and under the two things the
        // player is pointing with: the pad's reticle and the name chip beside the cursor.
        DrawWaypoint();
        DrawScaleBar();
        DrawPlayer();
        DrawCursor();
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
            Color ground = UiTheme.MapLand;
            var tone = new Color(
                ground.R + shade, ground.G + (shade * 0.9f), ground.B + (shade * 0.7f), ground.A);

            DrawRect(ScreenRect(tile.Rect), tone);
        }
    }

    /// <summary>Draws the physical route network over known land. A dark shoulder and a pale, opaque
    /// core: on the smoked ground the core is the lightest line on the plot after the lettering, so a
    /// road reads at a glance at both world-map and minimap zoom. Every shoulder goes down before any
    /// core, so a junction is one pale shape and not a core cut by its neighbour's shoulder.</summary>
    private void DrawRoads()
    {
        var shoulder = new Color(UiTheme.Engrave, 0.85f);
        foreach (MapRoadSegment road in Roads)
        {
            DrawLine(
                Projection.WorldToScreen(road.Start), Projection.WorldToScreen(road.End), shoulder, RoadCore(road) + 3f, true);
        }

        foreach (MapRoadSegment road in Roads)
        {
            DrawLine(
                Projection.WorldToScreen(road.Start), Projection.WorldToScreen(road.End),
                road.Width >= 4f ? UiTheme.MapRoad : UiTheme.MapTrack, RoadCore(road), true);
        }
    }

    private float RoadCore(MapRoadSegment road) => Mathf.Clamp(road.Width * Projection.Zoom * 0.55f, 1.25f, 7f);

    /// <summary>
    /// The outline of the known world — every land edge that no other cell covers.
    ///
    /// O(n²) over at most fifteen cells, once per redraw. ponytail: a real polygon union is the
    /// correct general answer and would be entirely wasted here.
    /// </summary>
    private void DrawCoastline()
    {
        var ink = new Color(UiTheme.IronLit, 0.72f);

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
        var ink = new Color(UiTheme.PanelBorder, 0.12f);

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

    /// <summary>The quest badge beside a pin: a filled diamond for the main thread, an outlined one for an errand.</summary>
    private void DrawQuestBadge(Vector2 at, Color colour, bool main)
    {
        const float r = 5f;
        Vector2[] diamond =
        {
            at + new Vector2(0f, -r), at + new Vector2(r, 0f), at + new Vector2(0f, r), at + new Vector2(-r, 0f),
        };

        if (main)
        {
            DrawColoredPolygon(diamond, colour);
            return;
        }

        DrawPolyline(new[] { diamond[0], diamond[1], diamond[2], diamond[3], diamond[0] }, colour, 2f);
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
            if (_questById.TryGetValue(pin.Id, out QuestPin questPin))
            {
                Color quest = UiTheme.Adapt(questPin.IsMain ? UiTheme.QuestMain : UiTheme.QuestSide);

                // The ring is the tracked quest's alone: "this is where you are going".
                if (questPin.Tracked)
                {
                    DrawArc(at, radius + 9f, 0f, Mathf.Tau, 28, new Color(quest, 0.9f), 2f);
                    DrawArc(at, radius + 12.5f, 0f, Mathf.Tau, 28, new Color(quest, 0.35f), 1f);
                }

                DrawQuestBadge(at + new Vector2(radius + 5f, -radius - 5f), quest, questPin.IsMain);
            }
            else if (pin.Id == ObjectiveId)
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
            float seal = radius + 4f;
            _letteringBlocks.Add(new Rect2(at - new Vector2(seal, seal), new Vector2(seal * 2f, seal * 2f)));

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

                QueueLabel(pin.Label, at, radius + 6f, colour, rank);
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
        // A dark seal under every marker keeps the authored SVG readable on pale and dark ink.
        DrawCircle(at, r + 3f, new Color(UiTheme.Engrave, 0.78f));
        DrawArc(at, r + 3f, 0f, Mathf.Tau, 20, new Color(colour, 0.42f), 1f);

        UiIcon.Kind kind = group switch
        {
            MapGroup.Settlement => UiIcon.Kind.Settlement,
            MapGroup.Trade => UiIcon.Kind.Currency,
            MapGroup.Service => UiIcon.Kind.Service,
            MapGroup.Exploration => UiIcon.Kind.Waypoint,
            MapGroup.Travel => UiIcon.Kind.Travel,
            _ => UiIcon.Kind.Waypoint,
        };

        if (UiIcon.Texture(kind) is { } texture)
        {
            float size = Mathf.Max(12f, r * 2.4f);
            DrawTextureRect(texture, new Rect2(at - new Vector2(size * 0.5f, size * 0.5f), new Vector2(size, size)), false, colour);
        }
        else
        {
            DrawCircle(at, r, colour);
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

        // A dark disc under the arrow and a keyline round it: bone on smoke, whatever is beneath.
        DrawCircle(at, 12.5f, new Color(UiTheme.Keyline, 0.55f));
        DrawArc(at, 12.5f, 0f, Mathf.Tau, 28, new Color(UiTheme.Text, 0.35f), 1f, true);
        DrawPolyline(new[] { tri[0], tri[1], tri[2], tri[0] }, UiTheme.Keyline, 4f, true);
        DrawColoredPolygon(tri, UiTheme.Text);
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
            Vector2 origin = _cursor + new Vector2(UiTheme.SpaceMd, -UiTheme.SpaceXs);

            // Flip to the other side of the cursor rather than run off the edge of the plot.
            if (origin.X + measured.X + UiTheme.SpaceSm > Size.X)
            {
                origin.X = _cursor.X - measured.X - UiTheme.SpaceMd;
            }

            DrawNameChip(font, pin.Label, origin, size);
            return;
        }
    }

    /// <summary>A name on its own small plate, <paramref name="origin"/> being the text's left
    /// baseline. The box wraps the glyphs (ascent above the baseline, descent below) with a tooltip's
    /// padding, SpaceSm at the sides and SpaceXs above and below, instead of hugging the text.</summary>
    private void DrawNameChip(Font font, string text, Vector2 origin, int size)
    {
        Vector2 measured = font.GetStringSize(text, HorizontalAlignment.Left, -1, size);
        float ascent = font.GetAscent(size);
        float height = ascent + font.GetDescent(size);
        DrawRect(
            new Rect2(
                origin - new Vector2(UiTheme.SpaceSm, ascent + UiTheme.SpaceXs),
                new Vector2(measured.X + (UiTheme.SpaceSm * 2f), height + (UiTheme.SpaceXs * 2f))),
            new Color(UiTheme.PanelBg, 0.92f));
        DrawString(font, origin, text, HorizontalAlignment.Left, -1, size, UiTheme.Text);
    }

    /// <summary>
    /// The gamepad cursor: a reticle at the centre of the plot and, when a pin is near enough to
    /// take the snap, a ring round that pin with its name on a plate. The ring and the name are the
    /// signal; the focus colour only agrees with them.
    /// </summary>
    private void DrawCursor()
    {
        if (Compact || !ShowCursor)
        {
            return;
        }

        Vector2 centre = Size * 0.5f;
        foreach (Vector2 arm in ReticleArms)
        {
            DrawLine(centre + (arm * 6f), centre + (arm * 15f), UiTheme.Keyline, 4f);
            DrawLine(centre + (arm * 7f), centre + (arm * 14f), UiTheme.Text, 2f);
        }

        if (SnapId == null || UiTheme.UiFont is not { } font)
        {
            return;
        }

        foreach (MapPin pin in Pins)
        {
            if (pin.Id != SnapId)
            {
                continue;
            }

            Vector2 at = Projection.WorldToScreen(pin.WorldXz);
            float radius = RadiusOf(pin.Tier) + 8f;
            DrawArc(at, radius + 1.5f, 0f, Mathf.Tau, 28, UiTheme.Keyline, 4f);
            DrawArc(at, radius, 0f, Mathf.Tau, 28, UiTheme.FocusRing, 2f);

            int size = UiTheme.FontSize(UiTheme.BodyFontSize);
            Vector2 measured = font.GetStringSize(pin.Label, HorizontalAlignment.Left, -1, size);
            float ascent = font.GetAscent(size);
            var origin = new Vector2(
                Mathf.Clamp(at.X - (measured.X * 0.5f), UiTheme.SpaceMd, Mathf.Max(UiTheme.SpaceMd, Size.X - measured.X - UiTheme.SpaceMd)),
                at.Y - radius - UiTheme.SpaceMd);

            // Under the pin instead when there is no room above it.
            if (origin.Y - ascent - UiTheme.SpaceXs < 0f)
            {
                origin.Y = at.Y + radius + UiTheme.SpaceMd + ascent;
            }

            DrawNameChip(font, pin.Label, origin, size);
            return;
        }
    }

    private static readonly Vector2[] ReticleArms = { Vector2.Up, Vector2.Down, Vector2.Left, Vector2.Right };

    /// <summary>A scale bar in the plot's lower left corner: the one measure that says how far apart
    /// two places are at this zoom. Its length steps through round distances.</summary>
    private void DrawScaleBar()
    {
        if (Compact || Size.X < 240f)
        {
            return;
        }

        int metres = MapSnapRules.ScaleBarMetres(Projection.Zoom, 120f);
        if (metres != _scaleMetres)
        {
            _scaleMetres = metres;
            _scaleText = Loc.TF("kn.map.scale", metres);
        }

        var left = new Vector2(UiTheme.SpaceMd, Size.Y - UiTheme.SpaceMd);
        var right = left + new Vector2(metres * Projection.Zoom, 0f);
        var tick = new Vector2(0f, -UiTheme.Space2xs);
        DrawLine(left + new Vector2(-1f, 0f), right + new Vector2(1f, 0f), UiTheme.Keyline, 4f);
        DrawLine(left, right, UiTheme.Text, 2f);
        DrawLine(left, left + tick, UiTheme.Text, 2f);
        DrawLine(right, right + tick, UiTheme.Text, 2f);
        DrawLabelAt(_scaleText, left + new Vector2(0f, -UiTheme.SpaceSm), UiTheme.Text, UiTheme.CaptionFontSize);
    }

    /// <summary>A hairline inside the plot's edge, so the map reads as a framed chart.</summary>
    private void DrawFrame() =>
        DrawRect(new Rect2(Vector2.Zero, Size), new Color(UiTheme.PanelBorder, 0.55f), false, 1f);

    /// <summary>
    /// Measures a pin label and adds it to this frame's competition (39.5C). The name sits centred
    /// <paramref name="gap"/> above the pin at <paramref name="pin"/>.
    ///
    /// A name that would lie on the player's arrow is moved, never dropped: under the pin, then
    /// just above the arrow's square, then just below it. Standing in a town is when its name
    /// matters most, and that is exactly when its pin and the arrow share the same few pixels.
    /// </summary>
    private void QueueLabel(string text, Vector2 pin, float gap, Color colour, int rank)
    {
        if (UiTheme.UiFont is not { } font || string.IsNullOrEmpty(text))
        {
            return;
        }

        int size = UiTheme.FontSize(UiTheme.CaptionFontSize);
        Vector2 measured = font.GetStringSize(text, HorizontalAlignment.Left, -1, size);

        // DrawString's origin is the text BASELINE, so the box starts a line-height above it.
        var rect = new Rect2(pin.X - (measured.X * 0.5f), pin.Y - gap - measured.Y, measured.X, measured.Y);
        if (_playerClear is { } arrow && arrow.Intersects(rect))
        {
            var bounds = new Rect2(Vector2.Zero, Size);
            Rect2 home = rect;
            for (int i = 0; i < 3; i++)
            {
                float top = i switch
                {
                    0 => pin.Y + gap,
                    1 => arrow.Position.Y - UiTheme.Space2xs - measured.Y,
                    _ => arrow.End.Y + UiTheme.Space2xs,
                };

                var moved = new Rect2(home.Position.X, top, measured.X, measured.Y);
                if (!arrow.Intersects(moved) && bounds.Encloses(moved))
                {
                    rect = moved;
                    break;
                }
            }
        }

        var origin = new Vector2(rect.Position.X, rect.End.Y);
        _labels.Add((new LabelCandidate(rect, rank, _labels.Count), text, origin, colour));
    }

    /// <summary>Runs the placer over this frame's labels and draws the survivors.</summary>
    private void DrawPlacedLabels()
    {
        if (_labels.Count == 0)
        {
            return;
        }

        _candidates.Clear();
        foreach ((LabelCandidate candidate, _, _, _) in _labels)
        {
            _candidates.Add(candidate);
        }

        foreach (int index in LabelPlacer.Place(_candidates, new Rect2(Vector2.Zero, Size)))
        {
            (LabelCandidate kept, string text, Vector2 origin, Color colour) = _labels[index];
            DrawLabelAt(text, origin, colour, UiTheme.CaptionFontSize);
            _letteringBlocks.Add(kept.Rect);
        }
    }

    /// <summary>
    /// Letters each territory's name near its anchor, on the first of a few spots (the anchor, then a
    /// line or two below and above it) that is clear of the player's arrow, every marker and every
    /// pin name already drawn. A name with nowhere clear to go is left off: the breadcrumb over the
    /// plot says which realm this is, and lettering across a marker says nothing.
    /// </summary>
    private void DrawLettering()
    {
        if (Regions.Count == 0 || UiTheme.MapLetteringFont is not { } font)
        {
            return;
        }

        int size = UiTheme.FontSize(UiTheme.HeaderFontSize);
        float line = font.GetAscent(size) + font.GetDescent(size);
        var bounds = new Rect2(Vector2.Zero, Size);
        foreach (MapMarker region in Regions)
        {
            if (string.IsNullOrEmpty(region.Label))
            {
                continue;
            }

            Vector2 anchor = Projection.WorldToScreen(new Vector2(region.X, region.Z));
            Vector2 measured = font.GetStringSize(region.Label, HorizontalAlignment.Left, -1, size);
            _candidates.Clear();
            for (int i = 0; i < LetteringSteps.Length; i++)
            {
                var origin = new Vector2(
                    anchor.X - (measured.X * 0.5f),
                    anchor.Y + (LetteringSteps[i] * (line + UiTheme.SpaceMd)));
                _candidates.Add(new LabelCandidate(new Rect2(origin.X, origin.Y - measured.Y, measured.X, measured.Y), i, i));
            }

            // The steps do not overlap one another, so the placer may keep several. Its answer is in
            // index order and the indices are the steps in order of preference: the first is drawn.
            List<int> clear = LabelPlacer.Place(_candidates, bounds, _letteringBlocks);
            if (clear.Count == 0)
            {
                continue;
            }

            Rect2 spot = _candidates[clear[0]].Rect;
            var baseline = new Vector2(spot.Position.X, spot.End.Y);
            DrawStringOutline(
                font, baseline, region.Label, HorizontalAlignment.Left, -1, size, LabelKeyline, UiTheme.Keyline);
            DrawString(font, baseline, region.Label, HorizontalAlignment.Left, -1, size, UiTheme.Dim);
            _letteringBlocks.Add(spot);
        }
    }

    /// <summary>
    /// A label whose left-baseline origin is already decided (the placer's output).
    ///
    /// Draws nothing rather than throwing if the UI face is unavailable: a nameless map is a
    /// degraded map, a crashed one is no map. (37.5E.)
    /// </summary>
    private void DrawLabelAt(string text, Vector2 origin, Color colour, int sizeToken)
    {
        if (UiTheme.UiFont is not { } font || string.IsNullOrEmpty(text))
        {
            return;
        }

        int size = UiTheme.FontSize(sizeToken);

        // A dark keyline all the way round, then the word: a plot label crosses roads, relief and
        // water, and a one-pixel drop shadow left its top and left edges to dissolve into them.
        // The keyline takes the label's own opacity, so a tier fading in does not arrive as a black
        // word first.
        DrawStringOutline(
            font, origin, text, HorizontalAlignment.Left, -1, size, LabelKeyline,
            new Color(UiTheme.Keyline, UiTheme.Keyline.A * colour.A));
        DrawString(font, origin, text, HorizontalAlignment.Left, -1, size, colour);
    }

    private static (Vector3 Position, float Yaw)? ResolvePlayer() =>
        ServiceLocator.Instance is { } locator && locator.TryGet(out PlayerCharacter player)
            ? (player.GlobalPosition, player.GlobalRotation.Y)
            : null;
}
