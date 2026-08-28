using System.Collections.Generic;
using Embervale.Core.Events;
using Embervale.Core.Services;
using Embervale.Dialogue;
using Embervale.Player;
using Embervale.Save;
using Godot;

namespace Embervale.World;

/// <summary>A point plotted on the world map (Phase 25E): a discovered region or POI, with a
/// label and a world-space planar position (X/Z).</summary>
public readonly record struct MapMarker(string Id, string Label, float X, float Z);

/// <summary>A discovered location and where it stands (Phase 39.5A).</summary>
public readonly record struct MapLocationView(MapLocationResource Location, Vector3 Position);

/// <summary>
/// What the player knows about the world (Phase 25E, rebuilt in 39.5A) — discovered regions, cells
/// and locations, plus their custom waypoint — exposed as data the <see cref="Embervale.UI.MapScreen"/>
/// renders. <see cref="ISaveable"/>, so all of it survives save/load.
///
/// <b>Where positions come from.</b> Nothing here authors a coordinate. A
/// <see cref="MapLocationComponent"/> in a cell scene calls <see cref="RegisterLocation"/> with its
/// own <c>GlobalPosition</c> as the cell streams in, so the map's idea of where the blacksmith is
/// *is* where the blacksmith is. See <see cref="MapLocationResource"/> for why that split exists.
///
/// ⚠️ <b>Two position stores, and the split is load-bearing.</b> <c>_livePositions</c> is what
/// components registered this run; <c>_savedPositions</c> is what a save file remembered. Reads
/// prefer the live one. This is what lets the world map still draw the Emberdeep Mine while the
/// player is in Frostfang — invariant 1 says a region loads whole and only one region is resident,
/// so the other region's markers have no live position to offer — <em>without</em> ever letting a
/// stale saved coordinate override a marker that is standing right there. A load replaces the saved
/// half and does not touch the live half, because the live half is the world and the world is not
/// something a save file gets to be wrong about.
///
/// <b>Discovery has two states, Unknown and Discovered, and that is a decision.</b> Rumoured and
/// Fully-Known were considered and cut rather than stubbed (Phase 40B's rule, 39C the worked
/// example): nothing in Embervale currently produces a rumour. The condition for adding Rumoured is
/// a check rather than a verdict — <em>when a dialogue graph sets a flag naming a place the player
/// has not visited</em>. Until then there is no third state and no dead enum member.
/// </summary>
[GlobalClass]
public partial class MapService : Node, ISaveable
{
    public string SaveId => "map";

    /// <summary>How close the player must come for a location to reveal itself. Anything visible
    /// from further off should author <see cref="MapLocationResource.RevealWithCell"/> instead.</summary>
    public const float DiscoveryRadius = 20f;

    private const float TickSeconds = 0.25f;

    private readonly HashSet<string> _regions = new();
    private readonly HashSet<string> _pois = new();
    private readonly HashSet<string> _locations = new();

    private readonly Dictionary<string, Vector3> _livePositions = new();
    private readonly Dictionary<string, Vector3> _savedPositions = new();

    /// <summary>World-space XZ footprints measured from cells resident THIS RUN.</summary>
    private readonly Dictionary<string, Rect2> _liveFootprints = new();

    /// <summary>Footprints a save remembered, for cells that are not resident to measure.</summary>
    private readonly Dictionary<string, Rect2> _savedFootprints = new();

    private float _sinceTick;

    /// <summary>Bumped whenever discovery changes, so the map UI can tell when to rebuild.</summary>
    public int Revision { get; private set; }

    /// <summary>The player's custom waypoint, or null. One at a time: the brief asks for waypoints
    /// "without unnecessarily complicating the UI", and a single move-it-where-you-want pin needs no
    /// list, no naming and no management screen.</summary>
    public Vector3? Waypoint { get; private set; }

    public override void _EnterTree()
    {
        ServiceLocator.Instance?.Register(this);
        SaveManager.Instance?.Register(this);
        EventBus.Instance?.Subscribe<RegionCellLoadedEvent>(OnCellLoaded);
    }

    public override void _ExitTree()
    {
        EventBus.Instance?.Unsubscribe<RegionCellLoadedEvent>(OnCellLoaded);
        SaveManager.Instance?.Unregister(this);
        ServiceLocator.Instance?.Unregister(this);
    }

    /// <summary>Marks a region discovered (called on entry). No-op if already known.</summary>
    public void DiscoverRegion(string regionId)
    {
        if (!string.IsNullOrEmpty(regionId) && _regions.Add(regionId))
        {
            Revision++;
        }
    }

    /// <summary>
    /// Records where a placed <see cref="MapLocationComponent"/> stands. Called once per marker as
    /// its cell streams in; re-registering updates the position, so the scene always wins.
    /// </summary>
    public void RegisterLocation(string id, Vector3 position)
    {
        if (string.IsNullOrEmpty(id))
        {
            return;
        }

        _livePositions[id] = position;
        TryDiscover(id, PlayerPosition());
    }

    public bool IsDiscovered(string locationId) => _locations.Contains(locationId);

    /// <summary>Where a location is, live position preferred over the remembered one.</summary>
    public Vector3? PositionOf(string locationId)
    {
        if (_livePositions.TryGetValue(locationId, out Vector3 live))
        {
            return live;
        }

        return _savedPositions.TryGetValue(locationId, out Vector3 saved) ? saved : null;
    }

    /// <summary>Every discovered location whose resource still exists, with its position.</summary>
    public IEnumerable<MapLocationView> DiscoveredLocations()
    {
        foreach (string id in _locations)
        {
            if (MapLocationDatabase.Get(id) is { } location && PositionOf(id) is { } position)
            {
                yield return new MapLocationView(location, position);
            }
        }
    }

    /// <summary>Places or moves the player's waypoint; null clears it.</summary>
    public void SetWaypoint(Vector3? position)
    {
        Waypoint = position;
        Revision++;
    }

    public override void _Process(double delta)
    {
        _sinceTick += (float)delta;
        if (_sinceTick < TickSeconds)
        {
            return;
        }

        _sinceTick = 0f;

        // Polled rather than driven off player movement: a location can also become discoverable
        // because a flag was set while the player stood still. ~20 candidates at 4 Hz — the §35
        // mistake would be doing this every frame, not doing it at all.
        if (_livePositions.Count == 0)
        {
            return;
        }

        Vector3? player = PlayerPosition();
        foreach (string id in _livePositions.Keys)
        {
            TryDiscover(id, player);
        }
    }

    /// <summary>Reveals a location if it is eligible and either reveals-with-cell or close enough.</summary>
    private void TryDiscover(string id, Vector3? player)
    {
        if (_locations.Contains(id) || MapLocationDatabase.Get(id) is not { } location)
        {
            return;
        }

        if (location.RequiredFlagId.Length > 0 && !HasFlag(location.RequiredFlagId))
        {
            return;
        }

        if (!location.RevealWithCell)
        {
            if (player is not { } at || !_livePositions.TryGetValue(id, out Vector3 position))
            {
                return;
            }

            // Planar distance: a marker on an upper floor is not further away for being above you.
            float dx = position.X - at.X;
            float dz = position.Z - at.Z;
            if ((dx * dx) + (dz * dz) > DiscoveryRadius * DiscoveryRadius)
            {
                return;
            }
        }

        if (_locations.Add(id))
        {
            Revision++;
            EventBus.Instance?.Publish(new LocationDiscoveredEvent(location));
        }
    }

    private void OnCellLoaded(RegionCellLoadedEvent e)
    {
        bool changed = _pois.Add(e.CellId);

        if (MeasureGround(e.Root) is { } footprint)
        {
            _liveFootprints[e.CellId] = footprint;
            changed = true;
        }

        // Discovering a cell also discovers the region that owns it (so walking in reveals it).
        if (RegionOfCell(e.CellId) is { } region)
        {
            changed |= _regions.Add(region.Id);
        }

        if (changed)
        {
            Revision++;
        }
    }

    /// <summary>
    /// Every cell footprint the player has seen, for the map's land layer. Live measurements win
    /// over remembered ones, exactly as <see cref="PositionOf"/> does and for the same reason.
    /// </summary>
    public IEnumerable<(string CellId, Rect2 Rect)> KnownFootprints()
    {
        foreach (KeyValuePair<string, Rect2> entry in _liveFootprints)
        {
            yield return (entry.Key, entry.Value);
        }

        foreach (KeyValuePair<string, Rect2> entry in _savedFootprints)
        {
            if (!_liveFootprints.ContainsKey(entry.Key))
            {
                yield return (entry.Key, entry.Value);
            }
        }
    }

    /// <summary>
    /// The world-space XZ extent of a cell's GROUND, measured from the geometry actually in it.
    ///
    /// ponytail: ground is "big and flat" — a mesh whose vertical extent is under two metres and
    /// whose area is over a hundred square metres. That heuristic is here because a cell has no
    /// authored size: <see cref="RegionCellResource"/> carries a centre and nothing else, and the
    /// floor dimensions live only as prose in the region .tres header. Unioning EVERY visual instead
    /// would let one tall tree or a distant backdrop mesh stretch a cell across the realm, which is
    /// worse than measuring nothing. If a cell ever authors its own size, delete this and read it.
    /// </summary>
    private static Rect2? MeasureGround(Node3D? root)
    {
        if (root == null)
        {
            return null;
        }

        Rect2? bounds = null;
        foreach (Node node in Descendants(root))
        {
            if (node is not VisualInstance3D visual || !visual.IsInsideTree())
            {
                continue;
            }

            Aabb box = visual.GlobalTransform * visual.GetAabb();
            if (box.Size.Y > 2f || box.Size.X * box.Size.Z < 100f)
            {
                continue;
            }

            var rect = new Rect2(box.Position.X, box.Position.Z, box.Size.X, box.Size.Z);
            bounds = bounds is { } current ? current.Merge(rect) : rect;
        }

        return bounds;
    }

    private static IEnumerable<Node> Descendants(Node node)
    {
        foreach (Node child in node.GetChildren())
        {
            yield return child;
            foreach (Node nested in Descendants(child))
            {
                yield return nested;
            }
        }
    }

    private static Vector3? PlayerPosition() =>
        ServiceLocator.Instance is { } locator && locator.TryGet(out PlayerCharacter player)
            ? player.GlobalPosition
            : null;

    private static bool HasFlag(string flagId) =>
        ServiceLocator.Instance is { } locator && locator.TryGet(out PlayerCharacter player) &&
        (player.GetComponent<StoryFlagsComponent>()?.Has(flagId) ?? false);

    /// <summary>Discovered regions as plottable markers (position from each region's spawn point).</summary>
    public IEnumerable<MapMarker> RegionMarkers()
    {
        foreach (string id in _regions)
        {
            if (RegionDatabase.Get(id) is { } region)
            {
                yield return new MapMarker(id, region.DisplayName, region.SpawnPoint.X, region.SpawnPoint.Z);
            }
        }
    }

    /// <summary>Discovered POIs as plottable markers (position from each cell's centre).</summary>
    public IEnumerable<MapMarker> PoiMarkers()
    {
        foreach (string id in _pois)
        {
            if (CellById(id) is { } cell)
            {
                yield return new MapMarker(id, Prettify(id), cell.Center.X, cell.Center.Z);
            }
        }
    }

    public bool HasAnyDiscovery => _regions.Count > 0 || _pois.Count > 0 || _locations.Count > 0;

    /// <summary>Whether a cell has been visited — the breadcrumb and the info panel both ask.</summary>
    public bool IsCellKnown(string cellId) => _pois.Contains(cellId);

    /// <summary>The one scan of the region table. It was written out twice — once returning the
    /// region and once the cell, byte-identical otherwise — so the two halves of one lookup are now
    /// one lookup with two projections below.</summary>
    private static (RegionResource Region, RegionCellResource Cell)? FindCell(string cellId)
    {
        foreach (RegionResource region in RegionDatabase.All)
        {
            foreach (RegionCellResource cell in region.Cells)
            {
                if (cell != null && cell.Id == cellId)
                {
                    return (region, cell);
                }
            }
        }

        return null;
    }

    private static RegionResource? RegionOfCell(string cellId) => FindCell(cellId)?.Region;

    private static RegionCellResource? CellById(string cellId) => FindCell(cellId)?.Cell;

    /// <summary>"ember_crown.waystone" -> "Waystone": the segment after the last dot, title-cased.</summary>
    private static string Prettify(string id)
    {
        int dot = id.LastIndexOf('.');
        string tail = dot >= 0 ? id[(dot + 1)..] : id;
        string[] words = tail.Split('_');
        for (int i = 0; i < words.Length; i++)
        {
            if (words[i].Length > 0)
            {
                words[i] = char.ToUpperInvariant(words[i][0]) + words[i][1..];
            }
        }

        return string.Join(' ', words);
    }

    public Godot.Collections.Dictionary Save()
    {
        var regions = new Godot.Collections.Array();
        foreach (string id in _regions)
        {
            regions.Add(id);
        }

        var pois = new Godot.Collections.Array();
        foreach (string id in _pois)
        {
            pois.Add(id);
        }

        // Discovered locations carry their position, exactly as FastTravelService persists a node's:
        // the cell that knows where it stands is not resident when the other region is.
        var locations = new Godot.Collections.Array();
        foreach (string id in _locations)
        {
            var entry = new Godot.Collections.Dictionary { ["id"] = id };
            if (PositionOf(id) is { } position)
            {
                entry["pos"] = position;
            }

            locations.Add(entry);
        }

        var footprints = new Godot.Collections.Dictionary();
        foreach ((string cellId, Rect2 rect) in KnownFootprints())
        {
            footprints[cellId] = rect;
        }

        var data = new Godot.Collections.Dictionary
        {
            ["regions"] = regions,
            ["pois"] = pois,
            ["locations"] = locations,
            ["footprints"] = footprints,
        };

        if (Waypoint is { } waypoint)
        {
            data["waypoint"] = waypoint;
        }

        return data;
    }

    public void Load(Godot.Collections.Dictionary data)
    {
        // Replace, never merge (CLAUDE.md §7). A quickload keeps every live actor, so anything not
        // explicitly overwritten here survives from the timeline being abandoned.
        _regions.Clear();
        _pois.Clear();
        _locations.Clear();
        _savedPositions.Clear();

        // ⚠️ Only the SAVED half is cleared — the live half is what the resident cells actually
        // measure, and a quickload does not reload them. Clearing both made the world's land vanish
        // on a quickload until the player crossed a region border. Same rule as _savedPositions.
        _savedFootprints.Clear();

        if (data.TryGetValue("regions", out Variant r) && r.VariantType == Variant.Type.Array)
        {
            foreach (Variant id in r.AsGodotArray())
            {
                _regions.Add(id.AsString());
            }
        }

        if (data.TryGetValue("pois", out Variant p) && p.VariantType == Variant.Type.Array)
        {
            foreach (Variant id in p.AsGodotArray())
            {
                _pois.Add(id.AsString());
            }
        }

        if (data.TryGetValue("locations", out Variant l) && l.VariantType == Variant.Type.Array)
        {
            foreach (Variant entry in l.AsGodotArray())
            {
                if (entry.VariantType != Variant.Type.Dictionary)
                {
                    continue;
                }

                Godot.Collections.Dictionary row = entry.AsGodotDictionary();
                string id = row.TryGetValue("id", out Variant idv) ? idv.AsString() : string.Empty;
                if (id.Length == 0)
                {
                    continue;
                }

                _locations.Add(id);
                if (row.TryGetValue("pos", out Variant pos) && pos.VariantType == Variant.Type.Vector3)
                {
                    _savedPositions[id] = pos.AsVector3();
                }
            }
        }

        if (data.TryGetValue("footprints", out Variant f) &&
            f.VariantType == Variant.Type.Dictionary)
        {
            foreach (KeyValuePair<Variant, Variant> entry in f.AsGodotDictionary())
            {
                if (entry.Value.VariantType == Variant.Type.Rect2)
                {
                    _savedFootprints[entry.Key.AsString()] = entry.Value.AsRect2();
                }
            }
        }

        // ⚠️ The else branch is the whole point: a save with no waypoint must CLEAR a live one, or
        // loading an older timeline leaves a pin the player placed in a future that never happened.
        Waypoint = data.TryGetValue("waypoint", out Variant w) && w.VariantType == Variant.Type.Vector3
            ? w.AsVector3()
            : null;

        Revision++;
    }
}
