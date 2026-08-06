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

        _view = new MapView { CustomMinimumSize = new Vector2(500, 320) };
        col.AddChild(_view);

        col.AddChild(new HSeparator());

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
                foreach (MapMarker region in _map.RegionMarkers())
                {
                    _legend.AddChild(UiTheme.Body($"◆ {region.Label}", UiTheme.Accent));
                }

                foreach (MapMarker poi in _map.PoiMarkers())
                {
                    _legend.AddChild(UiTheme.Body($"   • {poi.Label}", UiTheme.Dim));
                }
            }
        }

        RebuildTravelList();
    }

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

        bool any = false;
        foreach (TravelNode node in _travel.Nodes)
        {
            any = true;
            string id = node.Id; // capture for the closure
            Button button = UiTheme.Action(Loc.TF("travel.button", node.Label));
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
    private const float Margin = 24f;

    public MapService? Service { get; set; }

    public override void _Draw()
    {
        // Backdrop so the plot area reads as a distinct surface within the panel.
        DrawRect(new Rect2(Vector2.Zero, Size), UiTheme.WellBg);

        if (Service == null || !Service.HasAnyDiscovery)
        {
            return;
        }

        var markers = new List<(MapMarker Marker, bool IsRegion)>();
        foreach (MapMarker m in Service.RegionMarkers())
        {
            markers.Add((m, true));
        }

        foreach (MapMarker m in Service.PoiMarkers())
        {
            markers.Add((m, false));
        }

        Vector3? player = ResolvePlayer();

        // Fit bounds over every plotted point (markers + player).
        float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
        foreach ((MapMarker m, bool _) in markers)
        {
            Accumulate(ref minX, ref maxX, ref minZ, ref maxZ, m.X, m.Z);
        }

        if (player is { } p)
        {
            Accumulate(ref minX, ref maxX, ref minZ, ref maxZ, p.X, p.Z);
        }

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

        foreach ((MapMarker m, bool isRegion) in markers)
        {
            Vector2 s = ToScreen(m.X, m.Z);
            if (isRegion)
            {
                DrawCircle(s, 8f, UiTheme.Accent);
                DrawArc(s, 11f, 0f, Mathf.Tau, 20, new Color(UiTheme.Accent, 0.5f), 1.5f);
            }
            else
            {
                DrawCircle(s, 4f, UiTheme.Dim);
            }
        }

        if (player is { } pp)
        {
            Vector2 s = ToScreen(pp.X, pp.Z);
            DrawCircle(s, 5f, UiTheme.Mana);
            DrawArc(s, 8f, 0f, Mathf.Tau, 16, Colors.White, 1.5f);
        }
    }

    private static Vector3? ResolvePlayer()
    {
        if (ServiceLocator.Instance is { } locator && locator.TryGet(out PlayerCharacter player))
        {
            return player.GlobalPosition;
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
