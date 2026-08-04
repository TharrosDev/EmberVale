using System.Collections.Generic;
using Embervale.Core.Services;
using Embervale.Save;
using Godot;

namespace Embervale.Housing;

/// <summary>
/// What the player owns (Phase 37A). A set of claimed <see cref="PropertyResource"/> ids, persisted
/// with the save so a holding stays bought — the foundation 37B's storage, 37C's placed stations and
/// 37D's trophies all hang off.
///
/// Shaped on <see cref="World.FastTravelService"/>, the closest thing in the codebase and the one
/// that gets its lifecycle right: it registers with **both** the <see cref="ServiceLocator"/> and the
/// <see cref="SaveManager"/> and unregisters from both. Several services in this repo only did half
/// of that, which is why the locator now drops a freed registrant on read rather than handing it out.
/// </summary>
[GlobalClass]
public partial class HousingService : Node, ISaveable
{
    public string SaveId => "housing";

    private readonly HashSet<string> _owned = new();

    /// <summary>Bumped whenever ownership changes, so a future property UI knows to rebuild —
    /// the same signal <c>FastTravelService.Revision</c> gives the map screen.</summary>
    public int Revision { get; private set; }

    /// <summary>Every property the player holds.</summary>
    public IReadOnlyCollection<string> Owned => _owned;

    public bool Owns(string propertyId) => !string.IsNullOrEmpty(propertyId) && _owned.Contains(propertyId);

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

    /// <summary>Records a claim. Returns false for an empty id or one already held — which is also
    /// what stops a second interaction charging the player twice.</summary>
    public bool Claim(string propertyId)
    {
        if (string.IsNullOrEmpty(propertyId) || !_owned.Add(propertyId))
        {
            return false;
        }

        Revision++;
        return true;
    }

    public Godot.Collections.Dictionary Save()
    {
        var owned = new Godot.Collections.Array();
        foreach (string id in _owned)
        {
            owned.Add(id);
        }

        return new Godot.Collections.Dictionary { ["owned"] = owned };
    }

    public void Load(Godot.Collections.Dictionary data)
    {
        _owned.Clear();

        if (data.TryGetValue("owned", out Variant v) && v.VariantType == Variant.Type.Array)
        {
            foreach (Variant element in v.AsGodotArray())
            {
                string id = element.AsString();
                if (!string.IsNullOrEmpty(id))
                {
                    _owned.Add(id);
                }
            }
        }

        Revision++;
    }
}
