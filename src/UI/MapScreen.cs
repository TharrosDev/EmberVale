using System.Collections.Generic;
using Embervale.Core;
using Embervale.Core.Events;
using Embervale.Core.Services;
using Embervale.Localization;
using Embervale.Player;
using Embervale.World;
using Godot;

namespace Embervale.UI;

/// <summary>
/// The world map (Phase 25E; on the 30.5F <see cref="UiPanel"/> framework): toggled with the
/// <c>map</c> action (M). It plots discovered regions and POIs (from <see cref="MapService"/>)
/// on a simple top-down view plus a name legend, and marks the player. Undiscovered regions
/// are simply not drawn (fog). Modal since Phase 25G — the fast-travel buttons need the mouse.
/// Marks itself dirty when discovery/attunement revisions change or a game is loaded.
/// </summary>
public partial class MapScreen : UiPanel
{
    private MapService? _map;
    private FastTravelService? _travel;
    private MapView _view = null!;
    private VBoxContainer _legend = null!;
    private VBoxContainer _travelList = null!;
    private int _shownRevision = -1;
    private int _shownTravelRevision = -1;

    /// <summary>Which marker tiers are drawn. Filters live here rather than on the view so the
    /// legend and the plot cannot disagree about what is being shown.</summary>
    private bool _showRegions = true;
    private bool _showPois = true;
    private bool _showTravel = true;

    protected override string? ToggleAction => GameInput.Map;

    protected override void BuildShell(PanelContainer shell)
    {
        // Centred horizontally, anchored near the top — the same explicit anchor+offset pattern the
        // other panels use (e.g. CraftingPanel). The old SetAnchorsPreset(Center) reset the shell's
        // offsets against its zero size at build time, which seated the whole map off-screen toward
        // the top-right corner.
        shell.AnchorLeft = 0.5f;
        shell.AnchorRight = 0.5f;
        shell.OffsetLeft = -290;
        shell.OffsetRight = 290;
        shell.OffsetTop = 40;
        shell.GrowHorizontal = Control.GrowDirection.Both;

        MarginContainer pad = UiTheme.Padding(14);
        shell.AddChild(pad);

        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", UiTheme.SpaceSm);
        pad.AddChild(col);

        Label header = UiTheme.Header(Loc.T("map.title"));
        header.HorizontalAlignment = HorizontalAlignment.Center;
        col.AddChild(header);

        // Sized against the viewport (37.5G): a fixed 500x320 plot plus the legend and the travel
        // list ran past the bottom of a 533 px logical viewport (Steam Deck at UI scale 1.5).
        _view = new MapView();
        col.AddChild(_view);

        col.AddChild(BuildFilterRow());
        col.AddChild(UiTheme.Divider());

        _legend = new VBoxContainer();
        _legend.AddThemeConstantOverride("separation", 2);
        col.AddChild(_legend);

        col.AddChild(new HSeparator());
        col.AddChild(UiTheme.Header(Loc.T("map.travel_header")));
        _travelList = new VBoxContainer();
        _travelList.AddThemeConstantOverride("separation", UiTheme.SpaceXs);
        col.AddChild(_travelList);
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
        _view.Service = map;
        MarkDirty();
    }

    public void SetFastTravel(FastTravelService? travel)
    {
        _travel = travel;
        MarkDirty();
    }

    public override void _Process(double delta)
    {
        base._Process(delta);

        // Discovery/attunement changed while the map is up: refresh live.
        if (IsOpen &&
            ((_map != null && _shownRevision != _map.Revision) ||
             (_travel != null && _shownTravelRevision != _travel.Revision)))
        {
            MarkDirty();
        }
    }

    private void OnGameLoaded(GameLoadedEvent e) => MarkDirty();

    protected override void Rebuild()
    {
        // Re-measured per rebuild so a mid-session UI-scale change lands without a restart.
        float plot = Mathf.Clamp(UiTheme.UsableHeight(Shell) * 0.55f, 180f, 320f);
        _view.CustomMinimumSize = new Vector2(0f, plot);

        _view.ShowRegions = _showRegions;
        _view.ShowPois = _showPois;
        _view.ShowTravel = _showTravel;
        _view.Travel = _travel;

        if (_map != null)
        {
            _shownRevision = _map.Revision;
            _view.QueueRedraw();

            UiTheme.ClearChildren(_legend);

            if (!_map.HasAnyDiscovery)
            {
                _legend.AddChild(UiTheme.Body(Loc.T("map.empty"), UiTheme.Dim));
            }
            else
            {
                if (_showRegions)
                {
                    foreach (MapMarker region in _map.RegionMarkers())
                    {
                        _legend.AddChild(UiTheme.Body($"◆ {region.Label}", UiTheme.Accent));
                    }
                }

                if (_showPois)
                {
                    foreach (MapMarker poi in _map.PoiMarkers())
                    {
                        _legend.AddChild(UiTheme.Body($"   • {poi.Label}", UiTheme.Dim));
                    }
                }
            }
        }

        RebuildTravelList();
    }

    /// <summary>
    /// Marker filters. The map is the one screen in this overhaul where decoration most easily
    /// costs legibility, so the answer to "too many pins" is letting the player turn tiers off
    /// rather than styling them into a hierarchy fine enough to be unreadable.
    /// </summary>
    private Control BuildFilterRow()
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", UiTheme.SpaceXs);

        row.AddChild(FilterButton("map.filter_regions", UiTheme.Accent, () => _showRegions, v => _showRegions = v));
        row.AddChild(FilterButton("map.filter_pois", UiTheme.Dim, () => _showPois, v => _showPois = v));
        row.AddChild(FilterButton("map.filter_travel", UiTheme.GlyphLight, () => _showTravel, v => _showTravel = v));
        return row;
    }

    private Button FilterButton(string key, Color tint, System.Func<bool> get, System.Action<bool> set)
    {
        Button button = UiTheme.Action(Loc.T(key));
        button.AddThemeColorOverride("font_color", get() ? tint : UiTheme.Disabled);

        // Never rebuild inside a button signal (CLAUDE.md §8).
        button.Pressed += () =>
        {
            set(!get());
            MarkDirty();
        };
        return button;
    }

    /// <summary>Gold the player is carrying, for the fee display. Resolved through the
    /// <see cref="ServiceLocator"/> the way <c>PropertyDeedComponent.GoldHeld</c> does, so the bootstrap
    /// needs no extra wiring for the map to price a jump.</summary>
    private static int GoldHeld() =>
        Resolve<PlayerCharacter>()?.GetComponent<Items.InventoryComponent>()?.CountOf(GameIds.Currency.Gold) ?? 0;

    private static string CurrentRegionId() => Resolve<RegionStreamer>()?.ActiveRegionId ?? string.Empty;

    private static T? Resolve<T>()
        where T : class =>
        ServiceLocator.Instance is { } locator && locator.TryGet(out T service) ? service : null;

    /// <summary>The fast-travel section (Phase 25G): a button per attuned node that jumps there and
    /// closes the map. Empty until the player attunes to at least one waystone.</summary>
    private void RebuildTravelList()
    {
        if (_travel == null)
        {
            return;
        }

        _shownTravelRevision = _travel.Revision;
        UiTheme.ClearChildren(_travelList);

        // 38C: fast travel costs gold. The fee shown here and the fee charged in
        // GameBootstrap.OnFastTravelRequested are the same TravelCosts.FeeFor call, so a button can
        // never promise a price the jump does not take.
        string currentRegion = CurrentRegionId();
        int purse = GoldHeld();

        bool any = false;
        foreach (TravelNode node in _travel.Nodes)
        {
            any = true;
            string id = node.Id; // capture for the closure
            Economy.PriceQuote quote = Economy.TravelCosts.QuoteFor(node, currentRegion);
            int fee = quote.Total;
            bool affordable = fee <= purse;

            string label = fee > 0
                ? $"{Loc.TF("travel.button", node.Label)}   {Loc.TF("map.travel_cost", fee)}"
                : $"{Loc.TF("travel.button", node.Label)}   {Loc.T("map.travel_free")}";

            Button button = UiTheme.Action(label);

            // Greyed with the reason rather than hidden — a waypoint you cannot currently afford is
            // still somewhere you have attuned to, and hiding it would read as losing the attunement.
            button.Disabled = !affordable;

            // 38U: why this jump costs what it costs — the boundary it crosses, or the holding that
            // makes it free. ⚠️ The refusal still wins when there is one: a player who cannot pay needs
            // to be told that before they are told how the fee was arrived at.
            button.TooltipText = affordable
                ? PriceTooltip.Render(quote)
                : Loc.T("map.travel_cannot_afford");
            button.Pressed += () =>
            {
                SetOpen(false);
                EventBus.Instance?.Publish(new FastTravelRequestedEvent(id));
            };
            _travelList.AddChild(button);
        }

        if (!any)
        {
            _travelList.AddChild(UiTheme.Body(Loc.T("map.travel_empty"), UiTheme.Dim));
        }
    }
}

/// <summary>The top-down plot inside the <see cref="MapScreen"/>: draws discovered regions, POIs and
/// the player, fitting them to the control rect. Pure shapes (no font), so it has no resource deps.</summary>
public partial class MapView : Control
{
    private const float Margin = 28f;

    public MapService? Service { get; set; }

    /// <summary>Attuned waypoints. Plotted since 37.5E — they carry a world position and had never
    /// been drawn, so the map listed places you could travel to without showing you where they
    /// were.</summary>
    public FastTravelService? Travel { get; set; }

    public bool ShowRegions { get; set; } = true;

    public bool ShowPois { get; set; } = true;

    public bool ShowTravel { get; set; } = true;

    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero, Size), UiTheme.WellBg);

        if (Service == null || !Service.HasAnyDiscovery)
        {
            return;
        }

        var regions = new List<MapMarker>(Service.RegionMarkers());
        var pois = new List<MapMarker>(Service.PoiMarkers());
        var waypoints = new List<TravelNode>();
        if (Travel != null)
        {
            waypoints.AddRange(Travel.Nodes);
        }

        (Vector3 Position, float Yaw)? player = ResolvePlayer();

        // Fit over every known point, filtered or not: toggling a filter must not re-scale the map
        // under the player. A map that zooms when you hide a pin is a map you cannot read.
        float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
        foreach (MapMarker m in regions) { Accumulate(ref minX, ref maxX, ref minZ, ref maxZ, m.X, m.Z); }
        foreach (MapMarker m in pois) { Accumulate(ref minX, ref maxX, ref minZ, ref maxZ, m.X, m.Z); }
        foreach (TravelNode n in waypoints) { Accumulate(ref minX, ref maxX, ref minZ, ref maxZ, n.Position.X, n.Position.Z); }
        if (player is { } p) { Accumulate(ref minX, ref maxX, ref minZ, ref maxZ, p.Position.X, p.Position.Z); }

        // Pad degenerate (single point) extents so the transform never divides by zero.
        if (maxX - minX < 1f) { minX -= 20f; maxX += 20f; }
        if (maxZ - minZ < 1f) { minZ -= 20f; maxZ += 20f; }

        Vector2 ToScreen(float x, float z)
        {
            float u = (x - minX) / (maxX - minX);
            float v = (maxZ - z) / (maxZ - minZ); // invert Z so north (−Z) is up
            return new Vector2(
                Margin + (u * (Size.X - (2f * Margin))),
                Margin + (v * (Size.Y - (2f * Margin))));
        }

        // Drawn weakest first so the hierarchy resolves by overlap as well as by size: a POI never
        // covers a waypoint, and nothing ever covers the player.
        if (ShowPois)
        {
            foreach (MapMarker m in pois)
            {
                DrawCircle(ToScreen(m.X, m.Z), 3.5f, UiTheme.Dim);
            }
        }

        if (ShowTravel)
        {
            foreach (TravelNode n in waypoints)
            {
                Vector2 s = ToScreen(n.Position.X, n.Position.Z);
                DrawDiamond(s, 6f, UiTheme.GlyphLight);
                DrawArc(s, 9f, 0f, Mathf.Tau, 16, new Color(UiTheme.GlyphLight, 0.45f), 1f);
            }
        }

        if (ShowRegions)
        {
            foreach (MapMarker m in regions)
            {
                Vector2 s = ToScreen(m.X, m.Z);
                DrawCircle(s, 7f, UiTheme.Accent);
                DrawArc(s, 11f, 0f, Mathf.Tau, 20, new Color(UiTheme.Accent, 0.5f), 1.5f);
                DrawLabel(m.Label, s + new Vector2(0f, -16f), UiTheme.Accent);
            }
        }

        if (player is { } pp)
        {
            DrawPlayer(ToScreen(pp.Position.X, pp.Position.Z), pp.Yaw);
        }
    }

    /// <summary>
    /// The player as an arrow pointing where they are facing, not a dot.
    ///
    /// Orientation is the single thing that makes a map usable while walking — without it the
    /// player has to move to work out which way "up" is on the plot, which is exactly the moment
    /// they opened the map to avoid.
    /// </summary>
    private void DrawPlayer(Vector2 at, float yaw)
    {
        // Godot's −Z is forward and the plot puts −Z at the top, so a yaw of 0 must point up.
        float a = -yaw;
        var forward = new Vector2(Mathf.Sin(a), -Mathf.Cos(a));
        Vector2 right = new Vector2(-forward.Y, forward.X);

        Vector2[] tri =
        {
            at + (forward * 9f),
            at - (forward * 5f) + (right * 5.5f),
            at - (forward * 5f) - (right * 5.5f),
        };

        DrawColoredPolygon(tri, UiTheme.Text);
        DrawPolyline(new[] { tri[0], tri[1], tri[2], tri[0] }, UiTheme.Engrave, 1.5f);
    }

    private void DrawDiamond(Vector2 at, float radius, Color color)
    {
        Vector2[] points =
        {
            at + new Vector2(0f, -radius),
            at + new Vector2(radius, 0f),
            at + new Vector2(0f, radius),
            at + new Vector2(-radius, 0f),
        };
        DrawColoredPolygon(points, color);
    }

    /// <summary>
    /// A region name on the plot itself.
    ///
    /// This control used to be documented as "pure shapes (no font), so it has no resource deps" —
    /// true before 37.5A, when the project shipped no fonts at all. It now takes the UI face, and
    /// falls back to drawing nothing rather than throwing if that face is unavailable: a nameless
    /// map is a degraded map, a crashed one is no map.
    /// </summary>
    private void DrawLabel(string text, Vector2 at, Color color)
    {
        if (UiTheme.UiFont is not { } font)
        {
            return;
        }

        int size = UiTheme.FontSize(UiTheme.CaptionFontSize);
        Vector2 measured = font.GetStringSize(text, HorizontalAlignment.Left, -1, size);
        DrawString(font, at - new Vector2(measured.X * 0.5f, 0f), text, HorizontalAlignment.Left, -1, size, color);
    }

    private static (Vector3 Position, float Yaw)? ResolvePlayer()
    {
        if (ServiceLocator.Instance is { } locator && locator.TryGet(out PlayerCharacter player))
        {
            return (player.GlobalPosition, player.GlobalRotation.Y);
        }

        return null;
    }

    private static void Accumulate(ref float minX, ref float maxX, ref float minZ, ref float maxZ, float x, float z)
    {
        minX = Mathf.Min(minX, x);
        maxX = Mathf.Max(maxX, x);
        minZ = Mathf.Min(minZ, z);
        maxZ = Mathf.Max(maxZ, z);
    }
}
