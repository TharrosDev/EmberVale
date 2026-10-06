using System.Collections.Generic;
using Embervale.Core;
using Embervale.Core.Events;
using Embervale.Core.Services;
using Embervale.Dialogue;
using Embervale.Economy;
using Embervale.Localization;
using Embervale.Player;
using Embervale.Quests;
using Embervale.World;
using Godot;

namespace Embervale.UI;

/// <summary>
/// The world map (Phase 25E, rebuilt in 39.5A) — toggled with the <c>map</c> action (M).
///
/// <b>What changed and why.</b> The 25E map fitted every discovered point into a fixed rectangle and
/// listed them. That works while "everything the player knows" is a handful of regions and cells; it
/// stops working the moment the realm has 23 shops and 15 services, because the map had no way to
/// know those existed and no room to show them if it had. This version is a real view onto a world:
/// it pans, it zooms, it culls by <see cref="MapTiers">tier</see>, and every marker on it is a
/// <see cref="MapLocationResource"/> that some cell scene physically contains.
///
/// ⚠️ <b>It resolves, it does not restate.</b> A shop's name comes from <see cref="ShopDatabase"/>, a
/// service's price from the same <see cref="TravelCosts"/> call the counter charges, an NPC's name
/// from their <see cref="DialogueResource"/>. Nothing player-facing on this screen is authored twice
/// — the brief's §4, and invariant 5's "the explanation is the charge" applied to a second screen.
///
/// Modal, because the pan/zoom/select interaction needs the mouse.
///
/// <b>The 2026-10 pass</b> gave the plot the screen: the footer of view buttons is gone (their verbs
/// are in the legend), the rail is two tabs (the place in hand, and a legend whose rows are the
/// filters), a gamepad has a cursor that snaps to the nearest pin, and fast travel asks once before
/// it goes.
/// </summary>
public partial class MapScreen : UiPanel
{
    private const string BreadcrumbSeparator = "  ›  ";

    private MapService? _map;
    private FastTravelService? _travel;

    private MapView _view = null!;
    private LineEdit _search = null!;
    private Control _searchRow = null!;
    private Label _breadcrumb = null!;
    private VBoxContainer _rail = null!;
    private UiTabs _railTabs = null!;
    private VBoxContainer _railBody = null!;

    // The selected marker's block, rebuilt with the rail; the info helpers add their lines to it.
    private VBoxContainer _info = null!;

    // The rail's two tabs, by UiTabs index.
    private const int PlaceTab = 0;
    private const int LegendTab = 1;

    // The legend lists groups; expanded, it lists every category under its group.
    private bool _legendExpanded;

    // The waystone a travel button asked for, while the confirmation is up.
    private string? _pendingTravelId;
    private bool _focusConfirm;

    // The screenshot harness has no pad to make the cursor appear with.
    private bool _cursorForced;

    private MapProjection _projection = new(Vector2.Zero, MapProjection.DefaultZoom, Vector2.One);
    private readonly HashSet<MapCategory> _hidden = new();
    private readonly List<MapPin> _pins = new();

    private string _query = string.Empty;
    private string? _selectedId;
    private bool _centredOnce;
    private Vector3 _lastPlayerAt = Vector3.Zero;

    private int _shownRevision = -1;

    // The quest pins last handed to the plot, kept so the legend can name only the kinds actually drawn.
    private List<QuestPin> _questPins = new();
    private int _shownTravelRevision = -1;

    protected override string? ToggleAction => GameInput.Map;

    protected override HubTab? Hub => HubTab.Map;

    /// <summary>While the travel confirmation is up, cancel steps back out of it and the map stays.</summary>
    protected override bool CloseOnCancel => _pendingTravelId == null;

    /// <summary>The map's verbs for the device in hand. A pad moves the map under a cursor; a mouse
    /// drags it and points for itself.</summary>
    protected override IReadOnlyList<LegendEntry> Legend
    {
        get
        {
            if (_pendingTravelId != null)
            {
                return new[]
                {
                    new LegendEntry("ui_accept", Loc.T("kn.legend.confirm")),
                    new LegendEntry("ui_cancel", Loc.T("kn.legend.back")),
                };
            }

            var entries = new List<LegendEntry>();
            if (InputDevice.GamepadActive)
            {
                entries.Add(new LegendEntry(GameInput.LookRight, Loc.T("kn.legend.pan")));
                entries.Add(new LegendEntry(GameInput.MenuSubPrev, Loc.T("kn.legend.zoom"), GameInput.MenuSubNext));
                entries.Add(new LegendEntry(KnowledgeInput.Primary, Loc.T("kn.legend.select")));
                entries.Add(new LegendEntry(KnowledgeInput.Secondary, Loc.T("kn.legend.waypoint")));
            }
            else
            {
                entries.Add(new LegendEntry(GameInput.Attack, Loc.T("kn.legend.pan_select")));
                entries.Add(new LegendEntry(GameInput.MenuSubPrev, Loc.T("kn.legend.zoom"), GameInput.MenuSubNext));
                entries.Add(new LegendEntry(GameInput.Block, Loc.T("kn.legend.waypoint")));
            }

            entries.AddRange(base.Legend);
            return entries;
        }
    }

    /// <summary>The rail takes under a third of the page, so the plot is the screen.</summary>
    private float RailWidth() => Mathf.Clamp(UiTheme.UsableWidth(Shell) * 0.30f, 250f, 340f);

    protected override void BuildShell(PanelContainer shell)
    {
        // Near-fullscreen, unlike the 580 px shell 25E used. A map is the one screen where the plot
        // IS the content: shrinking it to leave room for chrome is what made the old one a legend
        // with a picture attached.
        UiTheme.ApplyScreenInset(shell);
        VBoxContainer col = UiTheme.HubPage(shell, Loc.T("kn.title.map"), out HBoxContainer aside);

        // Where the player is, in words (§29). The single most useful line on the screen and the one
        // the old map had no way to produce.
        _breadcrumb = UiTheme.Body(string.Empty, UiTheme.Accent);
        _breadcrumb.HorizontalAlignment = HorizontalAlignment.Right;
        _breadcrumb.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _breadcrumb.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        _breadcrumb.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        aside.AddChild(_breadcrumb);

        var body = new HBoxContainer();
        body.AddThemeConstantOverride("separation", UiTheme.SpaceMd);
        body.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        col.AddChild(body);

        PanelContainer well = UiTheme.Well();
        well.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        well.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        body.AddChild(well);

        _view = new MapView { Projection = _projection };
        _view.ViewChanged += OnViewChanged;
        _view.Picked += OnPicked;
        _view.WaypointRequested += OnWaypointRequested;
        well.AddChild(_view);

        body.AddChild(BuildRail());
    }

    /// <summary>
    /// The right-hand rail: two tabs over one scroll. "Place" is what the player is doing (search,
    /// the selected marker, the waypoint, fast travel); "Legend" says what the marks mean, and each
    /// of its rows is the switch that hides that kind of mark.
    ///
    /// ⚠️ ONE SCROLL FOR THE WHOLE RAIL, AND THAT IS A FIX (39.5C). The sections used to scroll
    /// separately, and a rail whose pinned heights outgrew the page squashed whichever one could
    /// flex: seven attuned waystones collapsed the filter list to a row of buttons sliced in half.
    /// </summary>
    private Control BuildRail()
    {
        _rail = new VBoxContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        _rail.AddThemeConstantOverride("separation", UiTheme.RowGap);
        _rail.CustomMinimumSize = new Vector2(RailWidth(), 0f);

        _railTabs = new UiTabs();
        _railTabs.Add(Loc.T("kn.map.tab_place"));
        _railTabs.Add(Loc.T("kn.map.tab_legend"));
        _railTabs.TabChanged += _ => MarkDirty();
        _rail.AddChild(_railTabs);

        (ScrollContainer scroll, VBoxContainer list) = UiTheme.ScrollList();
        _rail.AddChild(scroll);

        _search = new LineEdit
        {
            PlaceholderText = Loc.T("map.search_placeholder"),
            ClearButtonEnabled = true,
            CustomMinimumSize = new Vector2(0f, UiTheme.ControlHeight),
        };
        UiSkin.Apply(_search);
        UiTheme.ApplyType(_search, UiTheme.FontRole.Interface, UiTheme.BodyFontSize);
        _search.TextChanged += OnSearchChanged;
        _search.TextSubmitted += OnSearchSubmitted;

        // The search box's focus ring is drawn on its edge, which the scroll would clip.
        var searchRow = new MarginContainer();
        searchRow.AddThemeConstantOverride("margin_top", UiTheme.Space2xs);
        searchRow.AddThemeConstantOverride("margin_left", UiTheme.Space2xs);
        searchRow.AddChild(_search);
        _searchRow = searchRow;
        list.AddChild(searchRow);

        _railBody = new VBoxContainer();
        _railBody.AddThemeConstantOverride("separation", UiTheme.SpaceSm);
        list.AddChild(_railBody);
        return _rail;
    }

    protected override void OnReady()
    {
        EventBus.Instance?.Subscribe<GameLoadedEvent>(OnGameLoaded);

        // Quest pins follow the quest log, which changes without the map's own revision moving.
        EventBus.Instance?.Subscribe<QuestStartedEvent>(OnQuestStarted);
        EventBus.Instance?.Subscribe<QuestObjectiveActivatedEvent>(OnObjectiveActivated);
        EventBus.Instance?.Subscribe<QuestStageChangedEvent>(OnStageChanged);
        EventBus.Instance?.Subscribe<QuestCompletedEvent>(OnQuestCompleted);
        EventBus.Instance?.Subscribe<QuestFailedEvent>(OnQuestFailed);

        // The cursor and the legend's verbs both follow the device in hand.
        EventBus.Instance?.Subscribe<InputDeviceChangedEvent>(OnDeviceChanged);
    }

    private void OnDeviceChanged(InputDeviceChangedEvent e) => MarkDirty();

    private void OnQuestStarted(QuestStartedEvent e) => MarkDirty();

    private void OnObjectiveActivated(QuestObjectiveActivatedEvent e) => MarkDirty();

    private void OnStageChanged(QuestStageChangedEvent e) => MarkDirty();

    private void OnQuestCompleted(QuestCompletedEvent e) => MarkDirty();

    private void OnQuestFailed(QuestFailedEvent e) => MarkDirty();

    public override void _ExitTree()
    {
        EventBus.Instance?.Unsubscribe<GameLoadedEvent>(OnGameLoaded);
        EventBus.Instance?.Unsubscribe<QuestStartedEvent>(OnQuestStarted);
        EventBus.Instance?.Unsubscribe<QuestObjectiveActivatedEvent>(OnObjectiveActivated);
        EventBus.Instance?.Unsubscribe<QuestStageChangedEvent>(OnStageChanged);
        EventBus.Instance?.Unsubscribe<QuestCompletedEvent>(OnQuestCompleted);
        EventBus.Instance?.Unsubscribe<QuestFailedEvent>(OnQuestFailed);
        EventBus.Instance?.Unsubscribe<InputDeviceChangedEvent>(OnDeviceChanged);
    }

    public void SetMapService(MapService? map)
    {
        _map = map;
        MarkDirty();
    }

    public void SetFastTravel(FastTravelService? travel)
    {
        _travel = travel;
        MarkDirty();
    }

    protected override void OnOpenChanged(bool open)
    {
        if (!open)
        {
            // A confirmation never outlives the screen it was asked on.
            _pendingTravelId = null;
            return;
        }

        // Opening the map should never leave the player hunting for themselves. The first open
        // centres; later opens keep where you left it, which is what makes it usable as a reference
        // you flick in and out of.
        if (!_centredOnce)
        {
            CenterOnPlayer();
            _centredOnce = true;
        }
    }

    private void OnGameLoaded(GameLoadedEvent e)
    {
        _selectedId = null;
        _centredOnce = false;
        MarkDirty();
    }

    public override void _Process(double delta)
    {
        base._Process(delta);

        if (!IsOpen)
        {
            return;
        }

        // CloseOnCancel is off while the confirmation is up, so cancel is this screen's to answer:
        // it backs out of the question and leaves the map open.
        if (_pendingTravelId != null && Godot.Input.IsActionJustPressed(UiLive.UiCancel))
        {
            UiAudio.Play(UiCue.Back);
            CancelTravel();
        }

        if ((_map != null && _shownRevision != _map.Revision) ||
            (_travel != null && _shownTravelRevision != _travel.Revision))
        {
            MarkDirty();
        }

        // Right stick pans (the look_* actions already exist for the camera), so the map is
        // navigable on a pad without inventing a binding.
        var stick = new Vector2(
            Godot.Input.GetActionStrength(UiLive.LookRight) - Godot.Input.GetActionStrength(UiLive.LookLeft),
            Godot.Input.GetActionStrength(UiLive.LookDown) - Godot.Input.GetActionStrength(UiLive.LookUp));
        if (stick.LengthSquared() > 0.04f)
        {
            SetProjection(_projection.Panned(-stick * 600f * (float)delta));
        }

        // Redraw only when something actually moved. The player walking is the common case; a static
        // map redrawn every frame is the §35 mistake in miniature.
        if (PlayerPosition() is { } at && at.DistanceSquaredTo(_lastPlayerAt) > 0.0025f)
        {
            _lastPlayerAt = at;
            _view.QueueRedraw();
        }
    }

    /// <summary>The cursor's two verbs (<see cref="KnowledgeInput"/>): select the pin it has snapped
    /// to, and set or clear the waypoint under it.</summary>
    public override void _Input(InputEvent @event)
    {
        if (!IsOpen || _pendingTravelId != null || !_view.ShowCursor)
        {
            return;
        }

        if (KnowledgeInput.IsPrimary(@event))
        {
            UiAudio.Play(UiCue.Click);
            OnPicked(_view.SnapId);
            GetViewport().SetInputAsHandled();
        }
        else if (KnowledgeInput.IsSecondary(@event))
        {
            ToggleWaypointAtCursor();
            GetViewport().SetInputAsHandled();
        }
    }

    /// <summary>The sub-tab actions (Z / C, LT / RT) zoom: on a map the triggers are the zoom, and the
    /// rail's two tabs are a press away by focus.</summary>
    protected override void OnSubTab(int delta)
    {
        float target = MapSnapRules.StepZoom(_projection.Zoom, delta);
        ZoomBy(target / _projection.Zoom);
    }

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (!IsOpen || @event is not InputEventKey { Pressed: true, Echo: false } key)
        {
            return;
        }

        switch (key.Keycode)
        {
            case Key.Home:
                CenterOnPlayer();
                break;
            case Key.Equal or Key.Plus or Key.KpAdd:
                ZoomBy(1.3f);
                break;
            case Key.Minus or Key.KpSubtract:
                ZoomBy(1f / 1.3f);
                break;
            default:
                return;
        }

        MarkDirty();
        GetViewport().SetInputAsHandled();
    }

    // ── View control ──────────────────────────────────────────────────────────────────────────

    private void OnViewChanged(MapProjection projection)
    {
        _projection = ClampToContent(projection);
        _view.Projection = _projection;

        // ⚠️ NO MarkDirty HERE. This fires on every mouse-motion event of a drag, and a rebuild
        // frees and re-creates every row in the rail — search results, the selection panel, the
        // travel list, ~30 filter buttons and the legend. Nothing in the rail depends on where the
        // view is looking, so panning the map was rebuilding the whole screen tens of times a second
        // to produce identical content. The plot repaints itself; the rail does not need to know.
        _view.QueueRedraw();
        UpdateCursor();
    }

    /// <summary>
    /// Shows the cursor while a pad is in hand and snaps it to the nearest drawn pin. Called
    /// whenever the view or the pins change; writes to the plot only when the answer does.
    /// </summary>
    private void UpdateCursor()
    {
        bool cursor = _cursorForced || InputDevice.GamepadActive;
        string? snap = cursor && _view.Size.X > 1f
            ? _view.PinNear(_view.Size * 0.5f, MapSnapRules.SnapRadius)
            : null;
        if (cursor == _view.ShowCursor && snap == _view.SnapId)
        {
            return;
        }

        _view.ShowCursor = cursor;
        _view.SnapId = snap;
        _view.QueueRedraw();
    }

    /// <summary>Marks the spot under the cursor (the snapped pin's own position when there is one), or
    /// clears the mark when the cursor is already on it. One waypoint, as the mouse's right click sets.</summary>
    private void ToggleWaypointAtCursor()
    {
        if (_map == null)
        {
            return;
        }

        SyncViewport();
        Vector2 centre = _view.Size * 0.5f;
        Vector2? mark = _map.Waypoint is { } set ? _projection.WorldToScreen(new Vector2(set.X, set.Z)) : null;
        if (MapSnapRules.ClearsWaypoint(mark, centre, MapSnapRules.SnapRadius * 0.5f))
        {
            UiAudio.Play(UiCue.Back);
            _map.SetWaypoint(null);
        }
        else
        {
            Vector2 world = _view.SnapId != null && _map.PositionOf(_view.SnapId) is { } pin
                ? new Vector2(pin.X, pin.Z)
                : _projection.ScreenToWorld(centre);
            UiAudio.Play(UiCue.Confirm);
            _map.SetWaypoint(new Vector3(world.X, 0f, world.Y));
        }

        MarkDirty();
    }

    /// <summary>Keeps the stored projection's viewport in step with the plot, so a rebuild after a
    /// window resize does not hand the view a transform built for the old size.</summary>
    private void SyncViewport()
    {
        if (_view.Size.X > 1f && _view.Size.Y > 1f && !_projection.Viewport.IsEqualApprox(_view.Size))
        {
            _projection = _projection.Resized(_view.Size);
        }
    }

    private void SetProjection(MapProjection projection)
    {
        // Same reconciliation MapView does: _projection is built before any layout, so its viewport
        // is meaningless until the plot has a size. Clamping and zoom-about-centre both read it.
        if (_view.Size.X > 1f && _view.Size.Y > 1f)
        {
            projection = projection.Resized(_view.Size);
        }

        _projection = ClampToContent(projection);
        _view.Projection = _projection;
        _view.QueueRedraw();
        UpdateCursor();
    }

    /// <summary>Keeps the view within a screen of the known world, so it can never be lost in
    /// empty space but can still centre a marker on the edge of the map.</summary>
    private MapProjection ClampToContent(MapProjection projection)
    {
        if (_pins.Count == 0)
        {
            return projection;
        }

        var min = new Vector2(float.MaxValue, float.MaxValue);
        var max = new Vector2(float.MinValue, float.MinValue);
        foreach (MapPin pin in _pins)
        {
            min = new Vector2(Mathf.Min(min.X, pin.WorldXz.X), Mathf.Min(min.Y, pin.WorldXz.Y));
            max = new Vector2(Mathf.Max(max.X, pin.WorldXz.X), Mathf.Max(max.Y, pin.WorldXz.Y));
        }

        return projection.ClampedTo(min, max);
    }

    // Anchored on the PLOT's centre, not the projection's stored viewport — the stored one is a
    // half-pixel until the first layout pass, which put the zoom anchor in the top-left corner.
    private void ZoomBy(float factor) =>
        SetProjection(_projection.ZoomedAbout(_view.Size * 0.5f, factor));

    private void CenterOnPlayer()
    {
        if (PlayerPosition() is { } at)
        {
            SetProjection(_projection.CenteredOn(new Vector2(at.X, at.Z)));
        }
    }

    private void OnPicked(string? id)
    {
        _selectedId = id;

        // A marker chosen while the rail is on its legend brings the rail back to what was chosen.
        if (id != null)
        {
            _railTabs.Select(PlaceTab);
        }

        MarkDirty();
    }

    private void OnWaypointRequested(Vector3 position)
    {
        _map?.SetWaypoint(position);
        MarkDirty();
    }

    private void OnSearchChanged(string text)
    {
        _query = text;
        MarkDirty();
    }

    /// <summary>Enter takes the top hit. Typing a name and pressing return is what everyone does
    /// first, and a search box that ignores it feels broken however good the list below is.</summary>
    private void OnSearchSubmitted(string text)
    {
        IReadOnlyList<MapSearchHit> hits = MapSearch.Rank(text, SearchEntries());
        if (hits.Count > 0)
        {
            FocusLocation(hits[0].Id);
            MarkDirty();
        }
    }

    /// <summary>Centres and selects a location, zooming in far enough that its tier is actually
    /// drawn — a result that centres on an invisible pin reads as broken search.
    ///
    /// Public since 39.5C so the panel screenshot harness can drive the map to a named place at a
    /// chosen zoom. ⚠️ **The harness reuses this rather than setting the projection directly**, so
    /// what it photographs is the state a player reaches by searching — a capture path that bypasses
    /// the real one photographs the harness, not the screen.</summary>
    public void FocusLocation(string id)
    {
        _selectedId = id;
        if (_map?.PositionOf(id) is not { } position)
        {
            return;
        }

        float zoom = Mathf.Max(
            _projection.Zoom,
            MapLocationDatabase.Get(id) is { } location
                ? MapTiers.RevealZoom(location.EffectiveTier)
                : _projection.Zoom);

        SetProjection((_projection with { Zoom = zoom }).CenteredOn(new Vector2(position.X, position.Z)));
        _view.SelectedId = _selectedId;
        MarkDirty();
    }

    /// <summary>Sets the zoom, keeping the centre — the other half of what a capture harness needs
    /// (39.5C), since <see cref="FocusLocation"/> only ever zooms IN to reveal its target's tier.
    /// Routed through the same clamp every mouse wheel goes through.</summary>
    public void SetZoom(float zoom) => SetProjection(_projection with { Zoom = zoom });

    /// <summary>Opens the map on a location: centred, selected, its details in the rail. How the
    /// journal's "Show on map" arrives here.</summary>
    public void ShowLocation(string id)
    {
        SetOpen(true);
        _railTabs.Select(PlaceTab);
        FocusLocation(id);
    }

    // ── Capture hooks ─────────────────────────────────────────────────────────────────────────

    /// <summary>The marker the rail is describing, as of the last rebuild.</summary>
    public string? SelectedLocationId => _selectedId;

    /// <summary>Whether the rail is on its legend tab, and whether that legend lists categories.</summary>
    public bool LegendOpen => _railTabs.Current == LegendTab;

    public bool LegendExpanded => _legendExpanded;

    /// <summary>The pin the gamepad cursor has snapped to.</summary>
    public string? SnappedId => _view.SnapId;

    /// <summary>The waystone a travel confirmation is up for, or null.</summary>
    public string? PendingTravelId => _pendingTravelId;

    /// <summary>Puts the rail on its legend, folded or unfolded, the way its tab and its button do.</summary>
    public void ShowLegendForCapture(bool expanded)
    {
        _railTabs.Select(LegendTab);
        _legendExpanded = expanded;
        MarkDirty();
    }

    public void ShowPlaceForCapture()
    {
        _railTabs.Select(PlaceTab);
        MarkDirty();
    }

    /// <summary>Shows the gamepad cursor without a gamepad and brings a location under it.</summary>
    public void SnapForCapture(string id)
    {
        _cursorForced = true;
        FocusLocation(id);
    }

    /// <summary>Asks to travel to the first attuned waystone, as pressing its button does. False when
    /// none is attuned.</summary>
    public bool RequestTravelForCapture()
    {
        if (_travel == null)
        {
            return false;
        }

        foreach (TravelNode node in _travel.Nodes)
        {
            RequestTravel(node.Id);
            return true;
        }

        return false;
    }

    public void CancelTravelForCapture() => CancelTravel();

    // ── Rebuild ───────────────────────────────────────────────────────────────────────────────

    protected override void Rebuild()
    {
        if (_map != null)
        {
            _shownRevision = _map.Revision;
        }

        if (_travel != null)
        {
            _shownTravelRevision = _travel.Revision;
        }

        RebuildPins();

        SyncViewport();
        _view.Projection = _projection;
        _view.Pins = _pins;
        _view.HiddenCategories = _hidden;
        _view.SelectedId = _selectedId;
        _questPins = QuestProgressViews.Pins(Resolve<PlayerCharacter>()?.GetComponent<QuestLogComponent>());
        _view.QuestPins = _questPins;
        _view.ObjectiveId = TrackedObjectiveLocationId();
        _view.Waypoint = _map?.Waypoint;
        _view.Regions = _map != null ? new List<MapMarker>(_map.RegionMarkers()) : new List<MapMarker>();
        _view.Land = BuildLand();
        _view.Relief = MapCartography.Relief(CurrentRegionId()) is { } relief ? (relief.Texture, relief.World) : null;
        _view.Roads = MapCartography.Roads(CurrentRegionId());
        _view.QueueRedraw();
        UpdateCursor();

        // Called here as well as in BuildShell: the UI-scale setting can change mid-session.
        UiTheme.ApplyScreenInset(Shell);
        _rail.CustomMinimumSize = new Vector2(RailWidth(), 0f);

        RebuildBreadcrumb();
        RebuildRail();
    }

    private void RebuildRail()
    {
        UiTheme.ClearChildren(_railBody);

        TravelNode pending = default;
        bool confirming = _pendingTravelId != null && _travel != null &&
            _travel.TryGetNode(_pendingTravelId, out pending);
        if (!confirming)
        {
            _pendingTravelId = null;
        }

        bool place = _railTabs.Current == PlaceTab;
        _searchRow.Visible = place && !confirming;

        if (confirming)
        {
            BuildTravelConfirm(pending);
        }
        else if (place)
        {
            RebuildResults();
            RebuildInfo();
            RebuildWaypoint();
            RebuildTravelList();
        }
        else
        {
            RebuildLegend();
        }
    }

    private List<MapLandTile> BuildLand()
    {
        var land = new List<MapLandTile>();
        if (_map == null)
        {
            return land;
        }

        foreach ((string cellId, Rect2 rect) in _map.KnownFootprints())
        {
            land.Add(new MapLandTile(cellId, rect));
        }

        return land;
    }

    /// <summary>
    /// Every attuned waypoint, as a jump button.
    ///
    /// ⚠️ This is deliberately still a list, and 39.5A briefly shipped without one. Moving fast
    /// travel onto the selected marker alone reads as the feature having been REMOVED: the waystone
    /// pins are Secondary tier and are discovered by proximity, so a player who had attuned to five
    /// nodes could open the map and see no way to travel at all. Selection is the richer path;
    /// this is the one that is always there.
    /// </summary>
    private void RebuildTravelList()
    {
        _railBody.AddChild(UiTheme.SectionRule(Loc.T("kn.map.travel_header")));
        if (_travel == null)
        {
            return;
        }

        bool any = false;
        foreach (TravelNode node in _travel.Nodes)
        {
            any = true;
            _railBody.AddChild(TravelButton(node));
        }

        if (!any)
        {
            _railBody.AddChild(UiTheme.Body(Loc.T("map.travel_empty"), UiTheme.Dim));
        }
    }

    /// <summary>Shared with the HUD minimap since 39.5B — see <see cref="MapPins"/> for why there is
    /// exactly one pin builder.</summary>
    private void RebuildPins() => MapPins.Rebuild(_pins, _map, _travel);

    /// <summary>The player's own mark: how far it is and which way, and the button that clears it.
    /// Shown only while there is one.</summary>
    private void RebuildWaypoint()
    {
        if (_map?.Waypoint is not { } mark)
        {
            return;
        }

        _railBody.AddChild(UiTheme.SectionRule(Loc.T("kn.map.waypoint_header")));
        if (PlayerPosition() is { } player)
        {
            (int metres, string dirKey) = MapDistance.Describe(player.X, player.Z, mark.X, mark.Z);
            _railBody.AddChild(UiTheme.IconLabel(
                UiIcon.Kind.Waypoint,
                dirKey.Length == 0 ? Loc.T("map.distance_here") : Loc.TF("map.distance", metres, Loc.T(dirKey)),
                tint: UiTheme.AccentHot));
        }

        Button clear = UiTheme.Action(Loc.T("map.waypoint_clear"), UiCue.Back);
        clear.Pressed += () =>
        {
            _map?.SetWaypoint(null);
            MarkDirty();
        };
        _railBody.AddChild(clear);
    }

    private void RebuildBreadcrumb()
    {
        var parts = new List<string>();

        if (Resolve<RegionStreamer>()?.ActiveRegionId is { Length: > 0 } regionId &&
            RegionDatabase.Get(regionId) is { } region)
        {
            parts.Add(region.DisplayName);
        }

        // The settlement and district come from the nearest discovered location, which is the only
        // record that knows a cell is "the Market District" rather than "embermarket".
        if (NearestLocation() is { } nearest)
        {
            string settlement = SettlementNameOf(nearest.CellId);
            if (settlement.Length > 0)
            {
                parts.Add(settlement);
            }
        }

        _breadcrumb.Text = string.Join(BreadcrumbSeparator, parts);
    }

    private void RebuildResults()
    {
        if (_query.Trim().Length == 0)
        {
            return;
        }

        IReadOnlyList<MapSearchHit> hits = MapSearch.Rank(_query, SearchEntries());
        if (hits.Count == 0)
        {
            _railBody.AddChild(UiTheme.Body(Loc.T("map.search_none"), UiTheme.Dim));
            return;
        }

        foreach (MapSearchHit hit in hits)
        {
            string id = hit.Id; // capture for the closure
            Button button = UiTheme.Action(hit.Name);
            button.Alignment = HorizontalAlignment.Left;
            button.Pressed += () =>
            {
                FocusLocation(id);
                MarkDirty();
            };
            _railBody.AddChild(button);
        }
    }

    /// <summary>
    /// Everything searchable, built from discovered locations only.
    ///
    /// ⚠️ Discovered only, deliberately: a hit on somewhere the player has never been would name it,
    /// place it and tell them to go there — exploration handed over by a text box.
    /// </summary>
    private IEnumerable<MapSearchEntry> SearchEntries()
    {
        if (_map == null)
        {
            yield break;
        }

        foreach (MapLocationView view in _map.DiscoveredLocations())
        {
            MapLocationResource location = view.Location;

            // Terms are what makes "blacksmith" find The Iron Anvil: the category, the district, the
            // trade, and whoever keeps the place — resolved from their own records, never restated.
            var terms = new List<string> { Loc.T(MapCategories.NameKey(location.Category)) };

            if (ShopDatabase.Get(location.ShopId) is { } shop)
            {
                terms.Add(Loc.T(shop.NameKey));
            }

            if (ServiceDatabase.Get(location.ServiceId) is { } service)
            {
                terms.Add(Loc.T(service.NameKey));
            }

            if (DialogueDatabase.Get(location.DialogueId) is { } dialogue)
            {
                terms.Add(Loc.T(dialogue.SpeakerName));
            }

            yield return new MapSearchEntry(
                location.Id, Loc.T(location.NameKey), string.Join(' ', terms));
        }
    }

    private void RebuildInfo()
    {
        _railBody.AddChild(UiTheme.SectionRule(Loc.T("kn.map.selected_header"), first: _query.Trim().Length == 0));

        var info = new VBoxContainer();
        info.AddThemeConstantOverride("separation", UiTheme.SpaceSm);
        _info = info;
        _railBody.AddChild(info);

        if (_map is null or { HasAnyDiscovery: false })
        {
            _info.AddChild(UiTheme.Body(Loc.T("map.empty"), UiTheme.Dim));
            return;
        }

        if (_selectedId == null || MapLocationDatabase.Get(_selectedId) is not { } location)
        {
            Label none = UiTheme.Body(Loc.T("map.info_none"), UiTheme.Dim);
            none.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            _info.AddChild(none);
            AddCentreButton();
            return;
        }

        var where = new List<string> { Loc.T(MapCategories.NameKey(location.Category)) };
        string settlement = SettlementNameOf(location.CellId);
        if (settlement.Length > 0)
        {
            where.Add(settlement);
        }

        // The name and where it is are one lockup; every block after it is a full gap apart.
        var heading = new VBoxContainer();
        heading.AddThemeConstantOverride("separation", UiTheme.LineGap);
        Label placeName = UiTheme.Header(Loc.T(location.NameKey));
        placeName.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        heading.AddChild(placeName);
        Label placeKind = UiTheme.Caption(string.Join(BreadcrumbSeparator, where), UiTheme.Dim);
        placeKind.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        heading.AddChild(placeKind);
        _info.AddChild(heading);

        if (location.DescriptionKey.Length > 0)
        {
            _info.AddChild(UiTheme.Prose(Loc.T(location.DescriptionKey), UiTheme.Text));
        }

        AddDistanceLine(location.Id);

        // Everything below is resolved from the authoritative record, never authored here.
        if (ShopDatabase.Get(location.ShopId) is { } shop)
        {
            _info.AddChild(InfoPair(Loc.T("map.trade_header"), UiTheme.Brass, Loc.T(shop.NameKey)));
        }

        if (ServiceDatabase.Get(location.ServiceId) is { } service)
        {
            _info.AddChild(InfoPair(Loc.T("map.service_header"), UiTheme.GlyphLight, service.PriceGold > 0
                ? Loc.TF("map.service_price", Loc.T(service.NameKey), service.PriceGold)
                : Loc.T(service.NameKey)));
        }

        if (DialogueDatabase.Get(location.DialogueId) is { } dialogue)
        {
            _info.AddChild(InfoPair(Loc.T("map.npc_header"), UiTheme.Dim, Loc.T(dialogue.SpeakerName)));
        }

        AddTravelButton(location);
        AddCentreButton();
    }

    /// <summary>The way back to yourself: the one view button the legend has no room for.</summary>
    private void AddCentreButton()
    {
        Button centre = UiTheme.Action(Loc.T("map.center_player"));
        centre.Pressed += CenterOnPlayer;
        _info.AddChild(centre);
    }

    /// <summary>A small caption over its value, tight inside and a full gap from the next pair.</summary>
    private static Control InfoPair(string caption, Color captionColor, string value)
    {
        var pair = new VBoxContainer();
        pair.AddThemeConstantOverride("separation", UiTheme.LineGap);
        pair.AddChild(UiTheme.Caption(caption, captionColor));
        pair.AddChild(UiTheme.Body(value));
        return pair;
    }

    private void AddDistanceLine(string locationId)
    {
        if (PlayerPosition() is not { } player || _map?.PositionOf(locationId) is not { } target)
        {
            return;
        }

        (int metres, string dirKey) = MapDistance.Describe(player.X, player.Z, target.X, target.Z);
        _info.AddChild(UiTheme.Body(
            dirKey.Length == 0
                ? Loc.T("map.distance_here")
                : Loc.TF("map.distance", metres, Loc.T(dirKey)),
            UiTheme.Accent));
    }

    /// <summary>
    /// Fast travel, moved from a flat list into the selected place (Phase 25G rules unchanged).
    ///
    /// ⚠️ The fee shown is <see cref="TravelCosts.QuoteFor"/> — the same call
    /// <c>WorldSessionDirector.OnFastTravelRequested</c> charges — so a button can never promise a price the
    /// jump does not take (invariant 5).
    /// </summary>
    private void AddTravelButton(MapLocationResource location)
    {
        if (TravelNodeFor(location) is not { } node)
        {
            if (TravelNodeIdFor(location).Length > 0)
            {
                _info.AddChild(UiTheme.Body(Loc.T("map.travel_locked"), UiTheme.Disabled));
            }
            return;
        }

        _info.AddChild(TravelButton(node));
    }

    /// <summary>
    /// The waypoint a selected location can be travelled to.
    ///
    /// Its own <c>TravelNodeId</c> first; failing that, ANY attuned node in the same cell. That
    /// fallback is what makes the feature behave the way a player expects: you select "The
    /// Embermarket", not the unremarkable stone at its north end, and the jump you have already
    /// earned is offered on the place rather than on the object.
    ///
    /// ⚠️ It reads the whole catalogue rather than only discovered locations on purpose — attuning
    /// to the node IS having been there, so gating the offer on having also walked within twenty
    /// metres of the marker would refuse a jump the player has already paid for.
    /// </summary>
    private TravelNode? TravelNodeFor(MapLocationResource location)
    {
        string travelNodeId = TravelNodeIdFor(location);
        if (_travel == null || travelNodeId.Length == 0)
        {
            return null;
        }

        return _travel.TryGetNode(travelNodeId, out TravelNode node) ? node : null;
    }

    private static string TravelNodeIdFor(MapLocationResource location)
    {
        if (location.TravelNodeId.Length > 0)
        {
            return location.TravelNodeId;
        }

        if (location.CellId.Length == 0)
        {
            return string.Empty;
        }

        foreach (MapLocationResource candidate in MapLocationDatabase.All)
        {
            if (candidate.CellId == location.CellId && candidate.TravelNodeId.Length > 0 &&
                candidate.TravelNodeId.Length > 0)
            {
                return candidate.TravelNodeId;
            }
        }

        return string.Empty;
    }

    /// <summary>
    /// A jump button for one waypoint (Phase 25G rules unchanged).
    ///
    /// ⚠️ The fee shown is <see cref="TravelCosts.QuoteFor"/> — the same call
    /// <c>WorldSessionDirector.OnFastTravelRequested</c> charges — so a button can never promise a price the
    /// jump does not take (invariant 5).
    /// </summary>
    private Button TravelButton(TravelNode node)
    {
        PriceQuote quote = TravelCosts.QuoteFor(node, CurrentRegionId());
        int fee = quote.Total;
        bool affordable = fee <= GoldHeld();

        Button button = UiTheme.Action(fee > 0
            ? $"{Loc.TF("travel.button", node.Label)}   {Loc.TF("map.travel_cost", fee)}"
            : $"{Loc.TF("travel.button", node.Label)}   {Loc.T("map.travel_free")}");
        button.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        button.ClipText = true;

        // Greyed with the reason rather than hidden — a waypoint you cannot currently afford is
        // still somewhere you have attuned to, and hiding it would read as losing the attunement.
        button.Disabled = !affordable;
        button.TooltipText = affordable ? PriceTooltip.Render(quote) : Loc.T("map.travel_cannot_afford");

        // The press asks; the confirmation travels. A jump is a hard load and a fee, and one stray
        // press on a list of them should cost neither.
        string id = node.Id;
        button.Pressed += () => RequestTravel(id);
        return button;
    }

    private void RequestTravel(string nodeId)
    {
        _pendingTravelId = nodeId;
        _focusConfirm = true;
        _railTabs.Select(PlaceTab);
        MarkDirty();
    }

    private void CancelTravel()
    {
        _pendingTravelId = null;
        MarkDirty();
    }

    private void ConfirmTravel()
    {
        if (_pendingTravelId is not { } id)
        {
            return;
        }

        _pendingTravelId = null;
        SetOpen(false);
        EventBus.Instance?.Publish(new FastTravelRequestedEvent(id));
    }

    /// <summary>
    /// The question fast travel asks before it goes: where to, and what it costs. A press, not a
    /// hold: the journey can be made back, so it is confirmed like any other choice. It takes the
    /// rail's place while it is up, and cancel (or leaving the screen) withdraws it.
    /// </summary>
    private void BuildTravelConfirm(TravelNode node)
    {
        PriceQuote quote = TravelCosts.QuoteFor(node, CurrentRegionId());

        PanelContainer card = UiTheme.Card(UiTheme.Accent);
        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", UiTheme.SpaceSm);

        var heading = new VBoxContainer();
        heading.AddThemeConstantOverride("separation", UiTheme.LineGap);
        heading.AddChild(UiTheme.Caption(Loc.T("kn.map.travel_confirm"), UiTheme.Dim));
        Label destination = UiTheme.Header(node.Label);
        destination.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        heading.AddChild(destination);
        col.AddChild(heading);

        Label fee = UiTheme.Body(quote.Total > 0
            ? Loc.TF("kn.map.travel_fee", quote.Total)
            : Loc.T("kn.map.travel_no_fee"));
        fee.TooltipText = PriceTooltip.Render(quote);
        col.AddChild(fee);

        HFlowContainer buttons = UiTheme.FlowRow();
        Button go = UiTheme.Action(Loc.T("kn.map.travel_go"), UiCue.Confirm);
        go.Pressed += ConfirmTravel;
        buttons.AddChild(go);
        Button back = UiTheme.Action(Loc.T("kn.map.travel_cancel"), UiCue.Back);
        back.Pressed += CancelTravel;
        buttons.AddChild(back);
        col.AddChild(buttons);

        card.AddChild(col);
        _railBody.AddChild(card);

        // The rebuild that follows restores focus by position, which is wherever the travel button
        // was. The question is new, so focus goes to its answer, after that restore has run.
        if (_focusConfirm)
        {
            _focusConfirm = false;
            go.CallDeferred(Control.MethodName.GrabFocus);
        }
    }

    /// <summary>
    /// The legend (§28), which is also the filter: one row per group of marks on this map, each a
    /// switch that hides the group. Unfolded, every category is listed under its group with a switch
    /// of its own.
    ///
    /// ⚠️ Only groups with something in them get a row. A filter for a category the realm has no
    /// content for is a control that does nothing, which reads as a broken filter rather than an
    /// empty world.
    /// </summary>
    private void RebuildLegend()
    {
        HFlowContainer head = UiTheme.FlowRow();
        Button fold = UiTheme.Action(Loc.T(_legendExpanded ? "kn.map.legend_collapse" : "kn.map.legend_expand"));
        fold.Pressed += () =>
        {
            _legendExpanded = !_legendExpanded;
            MarkDirty();
        };
        head.AddChild(fold);

        if (_hidden.Count > 0)
        {
            Button all = UiTheme.Action(Loc.T("map.show_all"));
            all.Pressed += () =>
            {
                _hidden.Clear();
                MarkDirty();
            };
            head.AddChild(all);
        }

        _railBody.AddChild(head);

        foreach (MapGroup group in System.Enum.GetValues<MapGroup>())
        {
            var present = new List<MapCategory>();
            foreach (MapCategory category in MapCategories.InGroup(group))
            {
                if (HasPin(category))
                {
                    present.Add(category);
                }
            }

            if (present.Count == 0)
            {
                continue;
            }

            _railBody.AddChild(GroupToggle(group, present));
            if (!_legendExpanded)
            {
                continue;
            }

            foreach (MapCategory category in present)
            {
                var indent = new MarginContainer();
                indent.AddThemeConstantOverride("margin_left", UiTheme.SpaceLg);
                indent.AddChild(FilterToggle(category));
                _railBody.AddChild(indent);
            }
        }

        // The marks that are not places, and so have no switch. Each line is shown only when this
        // map draws it.
        _railBody.AddChild(UiTheme.SectionRule(Loc.T("kn.map.legend_marks")));
        _railBody.AddChild(UiTheme.IconLabel(UiIcon.Kind.Waypoint, Loc.T("map.legend_player"), tint: UiTheme.Text));

        // Quest pins: a filled diamond is the main thread, an outlined one an errand, a ring the tracked one.
        if (_questPins.Exists(p => p.IsMain))
        {
            _railBody.AddChild(MarkRow(MarkKind.Diamond, Loc.T("questui.legend.main"), UiTheme.Adapt(UiTheme.QuestMain)));
        }

        if (_questPins.Exists(p => !p.IsMain))
        {
            _railBody.AddChild(MarkRow(MarkKind.DiamondHollow, Loc.T("questui.legend.side"), UiTheme.Adapt(UiTheme.QuestSide)));
        }

        if (_questPins.Exists(p => p.Tracked))
        {
            _railBody.AddChild(UiTheme.IconLabel(UiIcon.Kind.Waypoint, Loc.T("questui.legend.tracked"), tint: UiTheme.Text));
        }

        if (_map?.Waypoint != null)
        {
            _railBody.AddChild(MarkRow(MarkKind.Cross, Loc.T("map.legend_waypoint"), UiTheme.Adapt(UiTheme.AccentHot)));
        }
    }

    /// <summary>A legend line whose sample is the shape the plot draws, not a stand-in icon.</summary>
    private static Control MarkRow(MarkKind kind, string text, Color colour)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", UiTheme.SpaceSm);
        row.AddChild(new MarkGlyph(kind, colour, 20f));
        row.AddChild(UiTheme.Body(text));
        return row;
    }

    private bool HasPin(MapCategory category)
    {
        foreach (MapPin pin in _pins)
        {
            if (pin.Category == category)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>A group's legend row: its icon in its colour, its name, and the switch that shows or
    /// hides every category of the group this map has.</summary>
    private Control GroupToggle(MapGroup group, List<MapCategory> present)
    {
        bool shown = present.Exists(category => !_hidden.Contains(category));
        CheckButton button = UiTheme.Toggle(shown);
        button.Text = Loc.T(MapCategories.NameKey(group));
        button.CustomMinimumSize = new Vector2(0f, UiTheme.ControlHeight);
        button.AddThemeColorOverride("font_color", shown ? UiTheme.Text : UiTheme.Disabled);

        button.Icon = UiIcon.Texture(IconOf(group));
        button.ExpandIcon = true;
        button.AddThemeConstantOverride("icon_max_width", UiTheme.HeaderFontSize);
        Color ink = shown ? LegendColour(group) : UiTheme.Disabled;
        foreach (string state in IconStates)
        {
            button.AddThemeColorOverride(state, ink);
        }

        button.Toggled += value =>
        {
            foreach (MapCategory category in present)
            {
                if (value)
                {
                    _hidden.Remove(category);
                }
                else
                {
                    _hidden.Add(category);
                }
            }

            MarkDirty();
        };
        return button;
    }

    private static readonly string[] IconStates =
    {
        "icon_normal_color", "icon_hover_color", "icon_pressed_color", "icon_hover_pressed_color", "icon_focus_color",
    };

    private Control FilterToggle(MapCategory category)
    {
        bool shown = !_hidden.Contains(category);
        CheckButton button = UiTheme.Toggle(shown);
        button.Text = Loc.T(MapCategories.NameKey(category));
        button.CustomMinimumSize = new Vector2(0f, UiTheme.ControlHeight);
        button.AddThemeColorOverride("font_color", shown ? UiTheme.Text : UiTheme.Disabled);
        button.Toggled += value =>
        {
            if (value)
            {
                _hidden.Remove(category);
            }
            else
            {
                _hidden.Add(category);
            }

            MarkDirty();
        };
        return button;
    }

    private static UiIcon.Kind IconOf(MapGroup group) => group switch
    {
        MapGroup.Settlement => UiIcon.Kind.Settlement,
        MapGroup.Trade => UiIcon.Kind.Currency,
        MapGroup.Service => UiIcon.Kind.Service,
        MapGroup.Exploration => UiIcon.Kind.Waypoint,
        MapGroup.Travel => UiIcon.Kind.Travel,
        _ => UiIcon.Kind.Waypoint,
    };

    private static Color LegendColour(MapGroup group) => UiTheme.Adapt(group switch
    {
        MapGroup.Settlement => UiTheme.Accent,
        MapGroup.Trade => UiTheme.Brass,
        MapGroup.Service => UiTheme.GlyphLight,
        MapGroup.Exploration => UiTheme.AccentHot,
        MapGroup.Travel => UiTheme.ArcaneSilver,
        _ => UiTheme.Text,
    });

    // ── Lookups ───────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The settlement name for a cell, taken from whichever discovered location in that cell is a
    /// settlement.
    ///
    /// ⚠️ <c>RegionCellResource</c> has no display name — it carries an id, a scene path and a
    /// centre. Rather than author a second name onto it (a new field on 15 cells, and a second
    /// record of something the map already knows), the settlement marker in the cell *is* the
    /// cell's name.
    /// </summary>
    private string SettlementNameOf(string cellId)
    {
        if (cellId.Length == 0 || _map == null)
        {
            return string.Empty;
        }

        foreach (MapLocationView view in _map.DiscoveredLocations())
        {
            if (view.Location.CellId == cellId &&
                MapCategories.GroupOf(view.Location.Category) == MapGroup.Settlement)
            {
                return Loc.T(view.Location.NameKey);
            }
        }

        return string.Empty;
    }

    /// <summary>The discovered location nearest the player, for the breadcrumb.</summary>
    private MapLocationResource? NearestLocation()
    {
        if (_map == null || PlayerPosition() is not { } player)
        {
            return null;
        }

        MapLocationResource? best = null;
        float bestDistance = float.MaxValue;

        foreach (MapLocationView view in _map.DiscoveredLocations())
        {
            float dx = view.Position.X - player.X;
            float dz = view.Position.Z - player.Z;
            float distance = (dx * dx) + (dz * dz);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = view.Location;
            }
        }

        return best;
    }

    /// <summary>Gold the player is carrying, for the fee display. Resolved through the
    /// <see cref="ServiceLocator"/> the way <c>PropertyDeedComponent.GoldHeld</c> does.</summary>
    private static int GoldHeld() =>
        Resolve<PlayerCharacter>()?.GetComponent<Items.InventoryComponent>()?.CountOf(GameIds.Currency.Gold) ?? 0;

    private static string CurrentRegionId() => Resolve<RegionStreamer>()?.ActiveRegionId ?? string.Empty;

    /// <summary>
    /// The map location the tracked quest's first outstanding objective points at, or null.
    ///
    /// ⚠️ Reads <see cref="Quests.QuestLogComponent.Tracked"/> — the same single authority the HUD
    /// tracker and the compass strip read since 39.5B. The map showing one quest while the tracker
    /// shows another is the exact class of drift that authority was created to make impossible.
    /// </summary>
    private static string? TrackedObjectiveLocationId() =>
        ObjectiveNavigation.ActiveLocationId(
            Resolve<PlayerCharacter>()?.GetComponent<QuestLogComponent>()?.Tracked);

    // ObjectiveNavigation answers for the tracked quest alone; every other live quest's pin comes from
    // QuestProgressViews.Pins, which applies the same rule per quest.

    private static Vector3? PlayerPosition() => Resolve<PlayerCharacter>()?.GlobalPosition;

    private static T? Resolve<T>()
        where T : class =>
        ServiceLocator.Instance is { } locator && locator.TryGet(out T service) ? service : null;
}
