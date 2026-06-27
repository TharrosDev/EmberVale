using System.Collections.Generic;
using Embervale.Core.Services;
using Embervale.Save;
using Godot;

namespace Embervale.World;

/// <summary>A discovered fast-travel destination (Phase 25G): a stable id, a display label, the
/// region it lives in, and the world position the player lands at.</summary>
public readonly record struct TravelNode(string Id, string Label, string RegionId, Vector3 Position);

/// <summary>
/// Tracks the fast-travel network (Phase 25G): the set of travel nodes the player has attuned to by
/// interacting with a <see cref="TravelNodeComponent"/> in the world. The map screen lists the
/// discovered nodes and lets the player jump to one; the bootstrap performs the jump on a
/// <see cref="FastTravelRequestedEvent"/> via the 25C hard-load path.
///
/// A node carries its own position/region (authored where it sits, not in a database), so the full
/// node is persisted. <see cref="ISaveable"/>, so the network survives save/load. Mirrors
/// <see cref="MapService"/>'s discovery-set shape.
/// </summary>
[GlobalClass]
public partial class FastTravelService : Node, ISaveable
{
    public string SaveId => "fasttravel";

    private readonly Dictionary<string, TravelNode> _nodes = new();

    /// <summary>Bumped whenever the network changes, so the map UI knows when to rebuild.</summary>
    public int Revision { get; private set; }

    public IEnumerable<TravelNode> Nodes => _nodes.Values;

    public bool HasNode(string id) => !string.IsNullOrEmpty(id) && _nodes.ContainsKey(id);

    public bool TryGetNode(string id, out TravelNode node) => _nodes.TryGetValue(id, out node);

    public override void _EnterTree()
    {
        ServiceLocator.Instance?.Register(this);
        SaveManager.Instance?.Register(this);
    }

    public override void _ExitTree()
    {
        SaveManager.Instance?.Unregister(this);
        ServiceLocator.Instance?.Unregister(this);
    }

    /// <summary>Records a newly-attuned node. No-op (returns false) if the id is empty or already known.</summary>
    public bool Discover(string id, string label, string regionId, Vector3 position)
    {
        if (string.IsNullOrEmpty(id) || _nodes.ContainsKey(id))
        {
            return false;
        }

        _nodes[id] = new TravelNode(id, label, regionId, position);
        Revision++;
        return true;
    }

    public Godot.Collections.Dictionary Save()
    {
        var nodes = new Godot.Collections.Array();
        foreach (TravelNode n in _nodes.Values)
        {
            nodes.Add(new Godot.Collections.Dictionary
            {
                ["id"] = n.Id,
                ["label"] = n.Label,
                ["region"] = n.RegionId,
                ["x"] = n.Position.X,
                ["y"] = n.Position.Y,
                ["z"] = n.Position.Z,
            });
        }

        return new Godot.Collections.Dictionary { ["nodes"] = nodes };
    }

    public void Load(Godot.Collections.Dictionary data)
    {
        _nodes.Clear();

        if (data.TryGetValue("nodes", out Variant v) && v.VariantType == Variant.Type.Array)
        {
            foreach (Variant element in v.AsGodotArray())
            {
                if (element.VariantType != Variant.Type.Dictionary)
                {
                    continue;
                }

                var entry = element.AsGodotDictionary();
                string id = entry.TryGetValue("id", out Variant idV) ? idV.AsString() : string.Empty;
                if (string.IsNullOrEmpty(id))
                {
                    continue;
                }

                var pos = new Vector3(
                    entry.TryGetValue("x", out Variant x) ? x.AsSingle() : 0f,
                    entry.TryGetValue("y", out Variant y) ? y.AsSingle() : 0f,
                    entry.TryGetValue("z", out Variant z) ? z.AsSingle() : 0f);
                string label = entry.TryGetValue("label", out Variant l) ? l.AsString() : id;
                string region = entry.TryGetValue("region", out Variant r) ? r.AsString() : string.Empty;
                _nodes[id] = new TravelNode(id, label, region, pos);
            }
        }

        Revision++;
    }
}
