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
    /// <summary>Side of the plot in pixels. Small enough to stay out of the way, large enough that
    /// two markers a few metres apart do not merge into one blob.</summary>
    private const float PlotSize = 186f;

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
    private MapService? _map;
    private FastTravelService? _travel;

    private readonly List<MapPin> _all = new();
    private readonly List<MapPin> _near = new();
    private List<MapLandTile> _land = new();

    // ⚠️ Cached against MapService.Revision, not re-enumerated per frame — the same rule
    // CompassStrip.RefreshPlaces follows. DiscoveredLocations() walks every discovered id through a
    // database lookup, and at 60 fps that is a per-frame database walk to draw markers that only
    // move when the player discovers something.
    private int _builtRevision = -1;
    private int _builtTravelRevision = -1;
    private float _rebuildTimer;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        AddThemeStyleboxOverride("panel", UiTheme.CardStyle());

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
        north.SetAnchorsPreset(LayoutPreset.CenterTop);
        north.GrowHorizontal = GrowDirection.Both;
        north.OffsetTop = 2f;
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

        if (ResolvePlayerXz() is not { } centre)
        {
            return;
        }

        _rebuildTimer -= (float)delta;
        if (_rebuildTimer <= 0f)
        {
            _rebuildTimer = RebuildInterval;
            RefreshDiscovered();
            RefreshNear(centre);
        }

        // Zoom is derived from the radius so the two can never disagree; MapView reconciles the
        // viewport itself (its `Fitted`), which is why this does not have to call Resized — the
        // 39.5A value-type-carrying-layout-state trap is already handled one level down.
        _view.Projection = new MapProjection(centre, ZoomFor(_view.Size), _view.Size);
        _view.Waypoint = _map?.Waypoint;
        _view.ObjectiveId = TrackedLocationId();
        _view.QueueRedraw();
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

        var land = new List<MapLandTile>();
        foreach ((string cellId, Rect2 rect) in _map.KnownFootprints())
        {
            land.Add(new MapLandTile(cellId, rect));
        }

        _land = land;
        _view.Land = _land;
    }

    /// <summary>Distance filter then priority cap (§20) — the rule itself is the pure, tested
    /// <see cref="MinimapFilter"/>.</summary>
    private void RefreshNear(Vector2 centre)
    {
        MinimapFilter.Select(_all, centre, RadiusMetres, MaxPins, _near, TrackedLocationId());
        _view.Pins = _near;
    }

    private static string? TrackedLocationId() =>
        ObjectiveNavigation.ActiveLocationId(
            ServiceLocator.Instance is { } locator && locator.TryGet(out PlayerCharacter player)
                ? player.GetComponent<QuestLogComponent>()?.Tracked
                : null);

    private static Vector2? ResolvePlayerXz() =>
        ServiceLocator.Instance is { } locator && locator.TryGet(out PlayerCharacter player)
            ? new Vector2(player.GlobalPosition.X, player.GlobalPosition.Z)
            : null;
}
