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
/// The world map (Phase 25E): a non-modal overlay toggled with the <c>map</c> action (M), like the
/// quest journal. It plots discovered regions and POIs (from <see cref="MapService"/>) on a simple
/// top-down view plus a name legend, and marks the player. Undiscovered regions are simply not drawn
/// (fog). Rebuilds when discovery changes (<see cref="MapService.Revision"/>) or a game is loaded.
/// Built through <see cref="UiTheme"/>.
/// </summary>
public partial class MapScreen : CanvasLayer
{
    private MapService? _map;
    private PanelContainer _panel = null!;
    private MapView _view = null!;
    private VBoxContainer _legend = null!;
    private int _shownRevision = -1;

    public override void _Ready()
    {
        _panel = UiTheme.Panel();
        _panel.Visible = false;
        _panel.SetAnchorsPreset(Control.LayoutPreset.Center);
        _panel.GrowHorizontal = Control.GrowDirection.Both;
        _panel.GrowVertical = Control.GrowDirection.Both;
        _panel.CustomMinimumSize = new Vector2(560, 0);
        AddChild(_panel);

        MarginContainer pad = UiTheme.Padding(14);
        _panel.AddChild(pad);

        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", 8);
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
        _shownRevision = -1;
    }

    public override void _Process(double delta)
    {
        if (Godot.Input.IsActionJustPressed(GameInput.Map))
        {
            _panel.Visible = !_panel.Visible;
            if (_panel.Visible)
            {
                _shownRevision = -1; // force a rebuild on open
            }
        }

        if (_panel.Visible && _map != null && _shownRevision != _map.Revision)
        {
            Rebuild();
        }
    }

    private void OnGameLoaded(GameLoadedEvent e) => _shownRevision = -1;

    private void Rebuild()
    {
        if (_map == null)
        {
            return;
        }

        _shownRevision = _map.Revision;
        _view.QueueRedraw();

        foreach (Node child in _legend.GetChildren())
        {
            _legend.RemoveChild(child);
            child.QueueFree();
        }

        if (!_map.HasAnyDiscovery)
        {
            _legend.AddChild(UiTheme.Body(Loc.T("map.empty"), UiTheme.Dim));
            return;
        }

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

/// <summary>The top-down plot inside the <see cref="MapScreen"/>: draws discovered regions, POIs and
/// the player, fitting them to the control rect. Pure shapes (no font), so it has no resource deps.</summary>
public partial class MapView : Control
{
    private const float Margin = 24f;

    public MapService? Service { get; set; }

    public override void _Draw()
    {
        // Backdrop so the plot area reads as a distinct surface within the panel.
        DrawRect(new Rect2(Vector2.Zero, Size), new Color(0.04f, 0.05f, 0.07f, 0.9f));

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
