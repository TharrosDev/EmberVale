using System.Collections.Generic;
using Embervale.Core.Events;
using Embervale.Core.Services;
using Embervale.Save;
using Godot;

namespace Embervale.World;

/// <summary>A point plotted on the world map (Phase 25E): a discovered region or POI, with a
/// label and a world-space planar position (X/Z).</summary>
public readonly record struct MapMarker(string Id, string Label, float X, float Z);

/// <summary>
/// Tracks world-map discovery (Phase 25E) and exposes it as data the <see cref="Embervale.UI.MapScreen"/>
/// renders. Regions are discovered on entry (the bootstrap calls <see cref="DiscoverRegion"/> for the
/// starting region and each hard transition); POIs are discovered when their cell first streams in
/// (it subscribes to <see cref="RegionCellLoadedEvent"/>). Undiscovered regions stay hidden (fog).
///
/// Marker geometry is re-resolved from the <see cref="RegionDatabase"/> at read time, so only the
/// discovered id sets need persisting. <see cref="ISaveable"/>, so discovery survives save/load.
/// </summary>
[GlobalClass]
public partial class MapService : Node, ISaveable
{
    public string SaveId => "map";

    private readonly HashSet<string> _regions = new();
    private readonly HashSet<string> _pois = new();

    /// <summary>Bumped whenever discovery changes, so the map UI can tell when to rebuild.</summary>
    public int Revision { get; private set; }

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

    private void OnCellLoaded(RegionCellLoadedEvent e)
    {
        bool changed = _pois.Add(e.CellId);

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

    public bool HasAnyDiscovery => _regions.Count > 0 || _pois.Count > 0;

    private static RegionResource? RegionOfCell(string cellId)
    {
        foreach (RegionResource region in RegionDatabase.All)
        {
            foreach (RegionCellResource cell in region.Cells)
            {
                if (cell != null && cell.Id == cellId)
                {
                    return region;
                }
            }
        }

        return null;
    }

    private static RegionCellResource? CellById(string cellId)
    {
        foreach (RegionResource region in RegionDatabase.All)
        {
            foreach (RegionCellResource cell in region.Cells)
            {
                if (cell != null && cell.Id == cellId)
                {
                    return cell;
                }
            }
        }

        return null;
    }

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

        return new Godot.Collections.Dictionary { ["regions"] = regions, ["pois"] = pois };
    }

    public void Load(Godot.Collections.Dictionary data)
    {
        _regions.Clear();
        _pois.Clear();

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

        Revision++;
    }
}
