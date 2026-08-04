using Godot;

namespace Embervale.Housing;

/// <summary>
/// A holding the player can come to own (Phase 37A), authored as a <c>.tres</c> under
/// <c>data/properties/</c> and indexed by <see cref="PropertyDatabase"/>. The deed that sells it is
/// a <see cref="PropertyDeedComponent"/> placed in a region cell; this is what it sells.
///
/// A property may be **bought**, **earned**, or both: a price with no quest is ordinary housing, a
/// quest with no price is a holding granted for a deed done, and both together is a reward you still
/// have to afford. What it cannot be is neither — free-on-touch property is a bug wearing content's
/// clothes, and <c>--validate</c> rejects it.
/// </summary>
[GlobalClass]
public partial class PropertyResource : Resource
{
    /// <summary>Stable id, e.g. <c>property.ember_crown.cottage</c>.</summary>
    [Export] public string Id { get; set; } = "property.unknown";

    /// <summary>Player-facing name. A <c>Loc</c> key — it reaches the interaction prompt, and §6
    /// admits no literals there.</summary>
    [Export] public string NameKey { get; set; } = string.Empty;

    /// <summary>Which region it stands in (a <c>region.*</c> id), recorded on the travel node.</summary>
    [Export] public string RegionId { get; set; } = string.Empty;

    [ExportGroup("Claiming")]

    /// <summary>Gold the deed costs. <c>0</c> means it is not sold — the quest below is its price.
    /// Phase 38 tunes economy balance; this is the sink it will tune.</summary>
    [Export] public int PriceGold { get; set; }

    /// <summary>Quest that must be <em>completed</em> before the deed can be claimed at all. Empty
    /// leaves it ungated, in which case a price is what stands between the player and the door.</summary>
    [Export] public string RequiredQuestId { get; set; } = string.Empty;

    /// <summary>
    /// Fast-travel node registered when the holding is claimed (a <c>travel.*</c> id) — the roadmap's
    /// tie between housing and Phase 25. Required: a property the player buys and then cannot return
    /// to is worth less than the gold it cost, so the validator insists on one.
    /// </summary>
    [Export] public string TravelNodeId { get; set; } = string.Empty;

    [ExportGroup("Placement")]

    /// <summary>
    /// Centre of the area the owner may place props in (Phase 37C), in <b>world</b> coordinates —
    /// <c>PersistentSpawnDirector</c> parents what it spawns to the bootstrap root at the origin, so
    /// its transforms are world-space and this must match. Remember a cell scene is authored at its
    /// own origin and moved to the cell's <c>Center</c> by the streamer, so a point read off a
    /// <c>.tscn</c> needs that centre added before it goes here.
    /// </summary>
    [Export] public Vector3 PlacementCenter { get; set; } = Vector3.Zero;

    /// <summary>
    /// Horizontal radius of that area. <c>0</c> is a holding you may not build in at all — refusing
    /// everywhere, never succeeding everywhere (see <see cref="PlacementCheck.Resolve"/>). The
    /// validator rejects a negative one, and rejects a positive one whose centre falls outside the
    /// region's bounds: a yard the player cannot reach is gold spent on nothing.
    /// </summary>
    [Export] public float PlacementRadius { get; set; }
}
