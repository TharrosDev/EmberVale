using System.Collections.Generic;
using Embervale.Core;
using Embervale.Core.Events;
using Embervale.Core.Services;
using Embervale.Dialogue;
using Embervale.Economy;
using Embervale.Localization;
using Embervale.Player;
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
/// </summary>
public partial class MapScreen : UiPanel
{
    private const string BreadcrumbSeparator = "  ›  ";

    private MapService? _map;
    private FastTravelService? _travel;

    private MapView _view = null!;
    private LineEdit _search = null!;
    private Label _breadcrumb = null!;
    private VBoxContainer _results = null!;
    private VBoxContainer _info = null!;
    private VBoxContainer _filters = null!;
    private VBoxContainer _legend = null!;

    private MapProjection _projection = new(Vector2.Zero, MapProjection.DefaultZoom, Vector2.One);
    private readonly HashSet<MapCategory> _hidden = new();
    private readonly List<MapPin> _pins = new();

    private string _query = string.Empty;
    private string? _selectedId;
    private bool _centredOnce;
    private Vector3 _lastPlayerAt = Vector3.Zero;

    private int _shownRevision = -1;
    private int _shownTravelRevision = -1;

    protected override string? ToggleAction => GameInput.Map;

    protected override void BuildShell(PanelContainer shell)
    {
        // Near-fullscreen, unlike the 580 px shell 25E used. A map is the one screen where the plot
        // IS the content: shrinking it to leave room for chrome is what made the old one a legend
        // with a picture attached.
        shell.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        UiTheme.ApplyScreenInset(shell);

        MarginContainer pad = UiTheme.Padding(UiTheme.SpaceLg);
        shell.AddChild(pad);

        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", UiTheme.SpaceSm);
        pad.AddChild(col);

        col.AddChild(BuildHeader());
        col.AddChild(UiTheme.Divider());

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
        col.AddChild(UiTheme.Divider());
        col.AddChild(BuildFooter());
    }

    private Control BuildHeader()
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", UiTheme.SpaceMd);

        row.AddChild(UiTheme.Title(Loc.T("map.title")));

        var spacer = new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        row.AddChild(spacer);

        // Where the player is, in words (§29). The single most useful line on the screen and the one
        // the old map had no way to produce.
        _breadcrumb = UiTheme.Body(string.Empty, UiTheme.Accent);
        _breadcrumb.HorizontalAlignment = HorizontalAlignment.Right;
        row.AddChild(_breadcrumb);

        return row;
    }

    /// <summary>The right-hand rail: search, results, selection, filters, legend — top to bottom in
    /// the order a player actually uses them.</summary>
    private Control BuildRail()
    {
        var rail = new VBoxContainer();
        rail.AddThemeConstantOverride("separation", UiTheme.SpaceSm);
        rail.CustomMinimumSize = new Vector2(320f, 0f);
        rail.SizeFlagsVertical = Control.SizeFlags.ExpandFill;

        _search = new LineEdit
        {
            PlaceholderText = Loc.T("map.search_placeholder"),
            ClearButtonEnabled = true,
        };
        UiTheme.ApplyType(_search, UiTheme.FontRole.Interface, UiTheme.BodyFontSize);
        _search.TextChanged += OnSearchChanged;
        rail.AddChild(_search);

        (ScrollContainer scroll, VBoxContainer list) = UiTheme.ScrollList();
        scroll.CustomMinimumSize = new Vector2(0f, 120f);
        _results = list;
        rail.AddChild(scroll);

        rail.AddChild(UiTheme.SectionRule(Loc.T("map.info_header")));
        _info = new VBoxContainer();
        _info.AddThemeConstantOverride("separation", 2);
        rail.AddChild(_info);

        rail.AddChild(UiTheme.SectionRule(Loc.T("map.filters_header")));
        (ScrollContainer filterScroll, VBoxContainer filterList) = UiTheme.ScrollList();
        filterScroll.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        _filters = filterList;
        rail.AddChild(filterScroll);

        rail.AddChild(UiTheme.SectionRule(Loc.T("map.legend_header")));
        _legend = new VBoxContainer();
        _legend.AddThemeConstantOverride("separation", 2);
        rail.AddChild(_legend);

        return rail;
    }

    private Control BuildFooter()
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", UiTheme.SpaceXs);

        row.AddChild(FooterButton("map.zoom_out", () => ZoomBy(1f / 1.3f)));
        row.AddChild(FooterButton("map.zoom_in", () => ZoomBy(1.3f)));
        row.AddChild(FooterButton("map.center_player", CenterOnPlayer));
        row.AddChild(FooterButton("map.reset_view", ResetView));
        row.AddChild(FooterButton("map.waypoint_clear", () => _map?.SetWaypoint(null)));

        var spacer = new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        row.AddChild(spacer);

        Label hint = UiTheme.Caption(Loc.T("map.hint"), UiTheme.Dim);
        row.AddChild(hint);

        return row;
    }

    private Button FooterButton(string key, System.Action action)
    {
        Button button = UiTheme.Action(Loc.T(key));

        // Never rebuild inside a button signal (CLAUDE.md §8) — mark dirty and let _Process do it.
        button.Pressed += () =>
        {
            action();
            MarkDirty();
        };
        return button;
    }

    protected override void OnReady()
    {
        EventBus.Instance?.Subscribe<GameLoadedEvent>(OnGameLoaded);
    }

    public override void _ExitTree()
    {
        EventBus.Instance?.Unsubscribe<GameLoadedEvent>(OnGameLoaded);
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

        if ((_map != null && _shownRevision != _map.Revision) ||
            (_travel != null && _shownTravelRevision != _travel.Revision))
        {
            MarkDirty();
        }

        // Right stick pans (the look_* actions already exist for the camera), so the map is
        // navigable on a pad without inventing a binding.
        var stick = new Vector2(
            Godot.Input.GetActionStrength(GameInput.LookRight) - Godot.Input.GetActionStrength(GameInput.LookLeft),
            Godot.Input.GetActionStrength(GameInput.LookDown) - Godot.Input.GetActionStrength(GameInput.LookUp));
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
        MarkDirty();
    }

    private void SetProjection(MapProjection projection)
    {
        _projection = ClampToContent(projection);
        _view.Projection = _projection;
        _view.QueueRedraw();
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

    private void ZoomBy(float factor) =>
        SetProjection(_projection.ZoomedAbout(_projection.Viewport * 0.5f, factor));

    private void CenterOnPlayer()
    {
        if (PlayerPosition() is { } at)
        {
            SetProjection(_projection.CenteredOn(new Vector2(at.X, at.Z)));
        }
    }

    private void ResetView()
    {
        _selectedId = null;
        _hidden.Clear();
        _query = string.Empty;
        _search.Text = string.Empty;
        SetProjection(_projection with { Zoom = MapProjection.DefaultZoom });
        CenterOnPlayer();
    }

    private void OnPicked(string? id)
    {
        _selectedId = id;
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

    /// <summary>Centres and selects a location, zooming in far enough that its tier is actually
    /// drawn — a result that centres on an invisible pin reads as broken search.</summary>
    private void FocusLocation(string id)
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
    }

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

        _view.Projection = _projection;
        _view.Pins = _pins;
        _view.HiddenCategories = _hidden;
        _view.SelectedId = _selectedId;
        _view.Waypoint = _map?.Waypoint;
        _view.Regions = _map != null ? new List<MapMarker>(_map.RegionMarkers()) : new List<MapMarker>();
        _view.QueueRedraw();

        RebuildBreadcrumb();
        RebuildResults();
        RebuildInfo();
        RebuildFilters();
        RebuildLegend();
    }

    private void RebuildPins()
    {
        _pins.Clear();
        if (_map == null)
        {
            return;
        }

        foreach (MapLocationView view in _map.DiscoveredLocations())
        {
            _pins.Add(new MapPin(
                view.Location.Id,
                Loc.T(view.Location.NameKey),
                new Vector2(view.Position.X, view.Position.Z),
                view.Location.Category,
                view.Location.EffectiveTier));
        }
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
        UiTheme.ClearChildren(_results);

        if (_query.Trim().Length == 0)
        {
            return;
        }

        IReadOnlyList<MapSearchHit> hits = MapSearch.Rank(_query, SearchEntries());
        if (hits.Count == 0)
        {
            _results.AddChild(UiTheme.Body(Loc.T("map.search_none"), UiTheme.Dim));
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
            _results.AddChild(button);
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
        UiTheme.ClearChildren(_info);

        if (_map is null or { HasAnyDiscovery: false })
        {
            _info.AddChild(UiTheme.Body(Loc.T("map.empty"), UiTheme.Dim));
            return;
        }

        if (_selectedId == null || MapLocationDatabase.Get(_selectedId) is not { } location)
        {
            _info.AddChild(UiTheme.Body(Loc.T("map.info_none"), UiTheme.Dim));
            return;
        }

        _info.AddChild(UiTheme.Header(Loc.T(location.NameKey)));

        var where = new List<string> { Loc.T(MapCategories.NameKey(location.Category)) };
        string settlement = SettlementNameOf(location.CellId);
        if (settlement.Length > 0)
        {
            where.Add(settlement);
        }

        _info.AddChild(UiTheme.Caption(string.Join(BreadcrumbSeparator, where), UiTheme.Dim));

        if (location.DescriptionKey.Length > 0)
        {
            _info.AddChild(UiTheme.Prose(Loc.T(location.DescriptionKey), UiTheme.Text));
        }

        AddDistanceLine(location.Id);

        // Everything below is resolved from the authoritative record, never authored here.
        if (ShopDatabase.Get(location.ShopId) is { } shop)
        {
            _info.AddChild(UiTheme.Caption(Loc.T("map.trade_header"), UiTheme.Brass));
            _info.AddChild(UiTheme.Body(Loc.T(shop.NameKey)));
        }

        if (ServiceDatabase.Get(location.ServiceId) is { } service)
        {
            _info.AddChild(UiTheme.Caption(Loc.T("map.service_header"), UiTheme.GlyphLight));
            _info.AddChild(UiTheme.Body(service.PriceGold > 0
                ? Loc.TF("map.service_price", Loc.T(service.NameKey), service.PriceGold)
                : Loc.T(service.NameKey)));
        }

        if (DialogueDatabase.Get(location.DialogueId) is { } dialogue)
        {
            _info.AddChild(UiTheme.Caption(Loc.T("map.npc_header"), UiTheme.Dim));
            _info.AddChild(UiTheme.Body(Loc.T(dialogue.SpeakerName)));
        }

        AddTravelButton(location);
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
    /// <c>GameBootstrap.OnFastTravelRequested</c> charges — so a button can never promise a price the
    /// jump does not take (invariant 5).
    /// </summary>
    private void AddTravelButton(MapLocationResource location)
    {
        if (_travel == null || location.TravelNodeId.Length == 0 ||
            !_travel.TryGetNode(location.TravelNodeId, out TravelNode node))
        {
            return;
        }

        PriceQuote quote = TravelCosts.QuoteFor(node, CurrentRegionId());
        int fee = quote.Total;
        bool affordable = fee <= GoldHeld();

        Button button = UiTheme.Action(fee > 0
            ? $"{Loc.TF("travel.button", node.Label)}   {Loc.TF("map.travel_cost", fee)}"
            : $"{Loc.TF("travel.button", node.Label)}   {Loc.T("map.travel_free")}");

        // Greyed with the reason rather than hidden — a waypoint you cannot currently afford is
        // still somewhere you have attuned to, and hiding it would read as losing the attunement.
        button.Disabled = !affordable;
        button.TooltipText = affordable ? PriceTooltip.Render(quote) : Loc.T("map.travel_cannot_afford");

        string id = node.Id;
        button.Pressed += () =>
        {
            SetOpen(false);
            EventBus.Instance?.Publish(new FastTravelRequestedEvent(id));
        };
        _info.AddChild(button);
    }

    private void RebuildFilters()
    {
        UiTheme.ClearChildren(_filters);

        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", UiTheme.SpaceXs);
        row.AddChild(FilterBulk("map.show_all", () => _hidden.Clear()));
        row.AddChild(FilterBulk("map.hide_all", HideAll));
        row.AddChild(FilterBulk("map.reset", () => _hidden.Clear()));
        _filters.AddChild(row);

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

            // ⚠️ Only groups with something in them get a row. A filter for a category the realm has
            // no content for is a control that does nothing, which reads as a broken filter rather
            // than an empty world — the same empty-promise the journal's Failed section would be.
            if (present.Count == 0)
            {
                continue;
            }

            _filters.AddChild(UiTheme.Caption(Loc.T(MapCategories.NameKey(group)), UiTheme.Dim));
            foreach (MapCategory category in present)
            {
                _filters.AddChild(FilterToggle(category));
            }
        }
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

    private void HideAll()
    {
        foreach (MapCategory category in System.Enum.GetValues<MapCategory>())
        {
            _hidden.Add(category);
        }
    }

    private Button FilterBulk(string key, System.Action action)
    {
        Button button = UiTheme.Action(Loc.T(key));
        button.Pressed += () =>
        {
            action();
            MarkDirty();
        };
        return button;
    }

    private Button FilterToggle(MapCategory category)
    {
        bool shown = !_hidden.Contains(category);
        Button button = UiTheme.Action($"{(shown ? "☑" : "☐")}  {Loc.T(MapCategories.NameKey(category))}");
        button.Alignment = HorizontalAlignment.Left;
        button.AddThemeColorOverride("font_color", shown ? UiTheme.Text : UiTheme.Disabled);
        button.Pressed += () =>
        {
            if (!_hidden.Remove(category))
            {
                _hidden.Add(category);
            }

            MarkDirty();
        };
        return button;
    }

    /// <summary>The legend (§28) — only the groups actually on screen, so it explains this map
    /// rather than every map the game could draw.</summary>
    private void RebuildLegend()
    {
        UiTheme.ClearChildren(_legend);

        foreach (MapGroup group in System.Enum.GetValues<MapGroup>())
        {
            bool any = false;
            foreach (MapCategory category in MapCategories.InGroup(group))
            {
                if (HasPin(category))
                {
                    any = true;
                    break;
                }
            }

            if (any)
            {
                _legend.AddChild(UiTheme.Caption(
                    $"{GlyphOf(group)}  {Loc.T(MapCategories.NameKey(group))}", LegendColour(group)));
            }
        }

        _legend.AddChild(UiTheme.Caption($"➤  {Loc.T("map.legend_player")}", UiTheme.Text));
        if (_map?.Waypoint != null)
        {
            _legend.AddChild(UiTheme.Caption($"✕  {Loc.T("map.legend_waypoint")}", UiTheme.AccentHot));
        }
    }

    /// <summary>The legend's stand-in for each silhouette <see cref="MapView"/> draws.</summary>
    private static string GlyphOf(MapGroup group) => group switch
    {
        MapGroup.Settlement => "●",
        MapGroup.Trade => "■",
        MapGroup.Service => "◆",
        MapGroup.Exploration => "▲",
        MapGroup.Travel => "⬢",
        _ => "✕",
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

    private static Vector3? PlayerPosition() => Resolve<PlayerCharacter>()?.GlobalPosition;

    private static T? Resolve<T>()
        where T : class =>
        ServiceLocator.Instance is { } locator && locator.TryGet(out T service) ? service : null;
}
