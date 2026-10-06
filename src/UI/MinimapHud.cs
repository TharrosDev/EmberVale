using System.Collections.Generic;
using Embervale.Core.Services;
using Embervale.Localization;
using Embervale.Player;
using Embervale.Quests;
using Embervale.World;
using Godot;

namespace Embervale.UI;

/// <summary>
/// The HUD minimap (39.5B): a small, north-up local plot in the bottom-right of the gameplay HUD.
///
/// ⚠️ <b>It is a <see cref="MapView"/>, not a second map.</b> The plot, the land, the coastline, the
/// marker shape language, the waypoint mark and the player arrow are all the full map screen's, drawn
/// by the same code from the same <see cref="MapPins"/> — the minimap adds a fixed local zoom, a
/// follow-the-player centre, a distance filter and nothing else. That is what makes invariant 5 hold
/// by construction: the map, the minimap and the <see cref="CompassStrip"/> cannot disagree about
/// where a place is, because only one of them knows.
///
/// <b>North-up</b>, with the player arrow rotating (maintainer decision, 39.5B). The full map is
/// north-up and so is the compass strip; a rotating minimap would be the only surface in the game
/// where north moves, and the cost of that disagreement is higher than the local-navigation win.
/// </summary>
public sealed partial class MinimapHud : PanelContainer
{
    /// <summary>Side of the plot before the layout is known. Small enough to stay out of the way,
    /// large enough that two markers a few metres apart do not merge into one blob; it follows the
    /// layout width from there (<see cref="HudCoreMetrics.MinimapSide"/>).</summary>
    private const float PlotSize = HudCoreMetrics.MinimapMin;

    /// <summary>How far the minimap sees, in world metres. The plot is scaled so this radius reaches
    /// the edge — so changing one number moves both the zoom and the cull together and they cannot
    /// drift into "drawn but filtered out" or "filtered in but off-plot".</summary>
    private const float RadiusMetres = 48f;

    /// <summary>Hard ceiling on drawn markers, nearest first (§20). Pin radii are in <i>pixels</i>,
    /// so a plot this size stops being readable somewhere around a dozen of them no matter how far
    /// apart they are in the world — distance filtering alone does not bound a dense market.</summary>
    private const int MaxPins = 10;

    /// <summary>How often the discovered set and the land are rebuilt, in seconds. The centre follows
    /// the player every frame; what EXISTS changes only on discovery, and walking does not discover
    /// ten things a second.</summary>
    private const float RebuildInterval = 0.5f;

    private MapView _view = null!;
    private GameHud? _hud;
    private MapService? _map;
    private FastTravelService? _travel;

    private readonly List<MapPin> _all = new();
    private readonly List<MapPin> _near = new();
    private List<QuestPin> _questPins = new();
    private readonly HashSet<string> _questIds = new();
    private readonly List<MapLandTile> _land = new();

    // ⚠️ Cached against MapService.Revision, not re-enumerated per frame — the same rule
    // CompassStrip.RefreshPlaces follows. DiscoveredLocations() walks every discovered id through a
    // database lookup, and at 60 fps that is a per-frame database walk to draw markers that only
    // move when the player discovers something.
    private int _builtRevision = -1;
    private int _builtTravelRevision = -1;
    private float _rebuildTimer;

    // --- Redraw-on-change (performance pass) -----------------------------------
    //
    // The plot is a full MapView paint: relief texture, every road segment, the pins and the player
    // arrow. It used to repaint every frame whether or not anything had moved. It now repaints when
    // the player has moved a quarter of a pixel on the plot, turned half a degree, or the things it
    // draws changed (waypoint, tracked objective, the half-second pin refresh, the plot's size).
    private const float MovePixels = 0.25f;
    private const float TurnStep = Mathf.Pi / 360f; // 0.5 degree

    private PlayerCharacter? _player;
    private bool _painted;
    private Vector2 _paintedCentre;
    private float _paintedYaw;
    private Vector2 _paintedSize;
    private Vector3? _paintedWaypoint;
    private string? _paintedObjective;
    private bool _contentChanged = true;
    private string? _trackedId;

    // What the minimap last told the HUD about (GameHud.MarkChanged), kept apart from the paint cache.
    private bool _noted;
    private string? _notedObjective;
    private Vector3? _notedWaypoint;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;

        // The HUD keyline as a frame: one dark pixel outside, one lighter pixel inside it, and the
        // plot to the edge. It was a framed panel with a hanging shadow and a heavier top rule, which
        // is a screen's chrome on a corner widget.
        var frame = new StyleBoxFlat { BgColor = UiTheme.HudInnerEdge, BorderColor = UiTheme.Keyline };
        frame.SetBorderWidthAll(1);
        frame.SetContentMarginAll(2);
        AddThemeStyleboxOverride("panel", frame);

        // MouseFilter.Ignore is the whole of "no interaction": MapView's drag, wheel-zoom, pick and
        // right-click-waypoint all live in _GuiInput, which never fires on an ignored control. No
        // fork, no disabled flags, no second code path that can rot.
        _view = new MapView
        {
            Name = "Plot",
            MouseFilter = MouseFilterEnum.Ignore,
            Compact = true,
            TierZoom = MapTiers.DetailZoom,
            CustomMinimumSize = new Vector2(PlotSize, PlotSize),
        };
        AddChild(_view);

        // The one thing north-up owes the player: which way north is. Parented INTO the plot so it
        // draws over MapView's opaque background rather than under it.
        var north = new Label
        {
            Text = Loc.T("hud.compass.n"),
            MouseFilter = MouseFilterEnum.Ignore,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        UiTheme.ApplyType(north, UiTheme.FontRole.Interface, UiTheme.CaptionFontSize);
        north.AddThemeColorOverride("font_color", UiTheme.Accent);
        UiTheme.HudInk(north);
        north.SetAnchorsPreset(LayoutPreset.CenterTop);
        north.GrowHorizontal = GrowDirection.Both;
        north.OffsetTop = UiTheme.Space2xs;
        _view.AddChild(north);
    }

    public override void _Process(double delta)
    {
        _map ??= ServiceLocator.Instance is { } locator && locator.TryGet(out MapService resolved)
            ? resolved
            : null;
        _travel ??= ServiceLocator.Instance is { } services && services.TryGet(out FastTravelService resolvedTravel)
            ? resolvedTravel
            : null;

        if (ResolvePlayer() is not { } player)
        {
            return;
        }

        Vector3 position = player.GlobalPosition;
        var centre = new Vector2(position.X, position.Z);

        _rebuildTimer -= (float)delta;
        if (_rebuildTimer <= 0f)
        {
            _rebuildTimer = RebuildInterval;
            _hud ??= GameHud.Of(this);

            // The tracked objective's place is resolved on this cadence too: asking for it rebuilds
            // the quest's objective list, and the pin it rings is only re-selected here anyway.
            _trackedId = TrackedLocationId();

            // A place found, a waypoint set or moved, a different objective: the map has something
            // new on it, which is when a Dynamic minimap comes up. Walking across it is not news.
            // The first pass builds everything from nothing and announces nothing.
            Vector3? mark = _map?.Waypoint;
            if (_noted && (_trackedId != _notedObjective || mark != _notedWaypoint ||
                (_map != null && _map.Revision != _builtRevision)))
            {
                _hud?.MarkChanged(HudElement.Minimap);
            }

            _noted = true;
            _notedObjective = _trackedId;
            _notedWaypoint = mark;
            RefreshDiscovered();
            RefreshQuestPins();
            RefreshNear(centre);
            _contentChanged = true; // the pin set is re-selected by distance: repaint at this 2 Hz
        }

        // No visibility check: while the HUD is hidden the projection is still kept current (a queued
        // redraw on a hidden item draws nothing), so the frame it reappears on is already centred.
        Vector2 size = _view.Size;
        float zoom = ZoomFor(size);
        float yaw = player.GlobalRotation.Y;
        Vector3? waypoint = _map?.Waypoint;
        string? objective = _trackedId;

        float movedPixels = (centre - _paintedCentre).Length() * zoom;
        if (_painted && !_contentChanged && movedPixels < MovePixels &&
            Mathf.Abs(Mathf.AngleDifference(_paintedYaw, yaw)) < TurnStep &&
            size == _paintedSize && waypoint == _paintedWaypoint && objective == _paintedObjective)
        {
            return;
        }

        _painted = true;
        _contentChanged = false;
        _paintedCentre = centre;
        _paintedYaw = yaw;
        _paintedSize = size;
        _paintedWaypoint = waypoint;
        _paintedObjective = objective;

        // Zoom is derived from the radius so the two can never disagree; MapView reconciles the
        // viewport itself (its `Fitted`), which is why this does not have to call Resized — the
        // 39.5A value-type-carrying-layout-state trap is already handled one level down.
        _view.Projection = new MapProjection(centre, zoom, size);
        _view.Waypoint = waypoint;
        _view.ObjectiveId = objective;
        _view.QueueRedraw();
    }

    /// <summary>Sizes the plot for the width the HUD lays out in (<see cref="HudCoreMetrics.MinimapSide"/>).
    /// <see cref="GameHud"/> calls it whenever that width changes; the plot's own size check in
    /// <see cref="_Process"/> repaints it.</summary>
    public void FitToLayout(float layoutWidth)
    {
        float side = HudCoreMetrics.MinimapSide(layoutWidth);
        if (_view != null && _view.CustomMinimumSize.X != side)
        {
            _view.CustomMinimumSize = new Vector2(side, side);
        }
    }

    /// <summary>Pixels per metre that puts <see cref="RadiusMetres"/> at the edge of the plot.
    /// Falls back to the nominal size before the first layout pass, when Size is still zero.</summary>
    private static float ZoomFor(Vector2 size)
    {
        float side = Mathf.Max(Mathf.Min(size.X, size.Y), 1f);
        if (side <= 1f)
        {
            side = PlotSize;
        }

        return side / (2f * RadiusMetres);
    }

    /// <summary>Rebuilds the discovered pins and the known land, but only when discovery changed.</summary>
    private void RefreshDiscovered()
    {
        int travelRevision = _travel?.Revision ?? -1;
        if (_map == null ||
            (_builtRevision == _map.Revision && _builtTravelRevision == travelRevision))
        {
            return;
        }

        _builtRevision = _map.Revision;
        _builtTravelRevision = travelRevision;
        MapPins.Rebuild(_all, _map, _travel);

        _land.Clear();
        foreach ((string cellId, Rect2 rect) in _map.KnownFootprints())
        {
            _land.Add(new MapLandTile(cellId, rect));
        }

        _view.Land = _land;
        string regionId = ServiceLocator.Instance is { } streamers && streamers.TryGet(out RegionStreamer streamer)
            ? streamer.ActiveRegionId : string.Empty;
        _view.Relief = MapCartography.Relief(regionId) is { } relief ? (relief.Texture, relief.World) : null;
        _view.Roads = MapCartography.Roads(regionId);
    }

    /// <summary>Distance filter then priority cap (§20) — the rule itself is the pure, tested
    /// <see cref="MinimapFilter"/>.</summary>
    private void RefreshNear(Vector2 centre)
    {
        MinimapFilter.Select(_all, centre, RadiusMetres, MaxPins, _near, _trackedId, _questIds);
        _view.Pins = _near;
    }

    /// <summary>Re-derives the quest pins on the rebuild cadence: the log changes without the map's revision
    /// moving, and the pins are the current objective of every live quest.</summary>
    private void RefreshQuestPins()
    {
        _questPins = QuestProgressViews.Pins(
            ServiceLocator.Instance is { } locator && locator.TryGet(out PlayerCharacter player)
                ? player.GetComponent<QuestLogComponent>()
                : null);
        _view.QuestPins = _questPins;
        _questIds.Clear();
        foreach (QuestPin pin in _questPins)
        {
            _questIds.Add(pin.LocationId);
        }
    }

    private static string? TrackedLocationId() =>
        ObjectiveNavigation.ActiveLocationId(
            ServiceLocator.Instance is { } locator && locator.TryGet(out PlayerCharacter player)
                ? player.GetComponent<QuestLogComponent>()?.Tracked
                : null);

    /// <summary>The player, re-resolved through the locator every tick (a load or a region swap
    /// replaces it) but read once per tick instead of once per property.</summary>
    private PlayerCharacter? ResolvePlayer()
    {
        _player = ServiceLocator.Instance is { } locator && locator.TryGet(out PlayerCharacter player) &&
                  IsInstanceValid(player)
            ? player
            : null;
        return _player;
    }
}
