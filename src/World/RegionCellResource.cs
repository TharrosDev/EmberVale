using Godot;

namespace Embervale.World;

/// <summary>
/// One sub-cell of a <see cref="RegionResource"/> (Phase 25B): a scene the
/// <see cref="RegionStreamer"/> instances at <see cref="Center"/> when the player enters the region,
/// and keeps resident until they leave it. Authored as a sub-resource inside the region's
/// <c>.tres</c>.
///
/// ⚠️ <b>There is no <c>LoadRadius</c> any more (38M2).</b> It was the distance at which the streamer
/// brought the cell in, and it went with the distance rule itself — a field the streamer no longer
/// reads is a number the next author would tune to no effect, which is the failure mode this repo
/// keeps writing down. <see cref="SafeRadius"/> below is a different field and is still live.
/// </summary>
[GlobalClass]
public partial class RegionCellResource : Resource
{
    /// <summary>Stable id, <c>&lt;region&gt;.&lt;cell&gt;</c> (e.g. "ember_crown.waystone").</summary>
    [Export] public string Id { get; set; } = "region.cell";

    /// <summary>The cell scene to instance, by the §2.6h-2 convention
    /// <c>res://scenes/regions/&lt;region&gt;/&lt;cell&gt;.tscn</c>.</summary>
    [Export(PropertyHint.File, "*.tscn")] public string ScenePath { get; set; } = "";

    /// <summary>World-space centre the cell is placed at.</summary>
    [Export] public Vector3 Center { get; set; } = Vector3.Zero;

    /// <summary>
    /// Planar radius around <see cref="Center"/> in which the ambient spawners must not drop enemies
    /// (Phase 38K). <c>0</c> — the default — means this cell is not a safe area, which is every cell
    /// authored before the Embermarket, so the field arrives inert.
    ///
    /// This exists because a settlement can be more than one cell. The region's own
    /// <c>SafeZoneCenter</c>/<c>SafeZoneRadius</c> is a single bubble over the town square; widening it
    /// to reach a market district a street away also smothers the encounters around the wilds cells,
    /// which is the whole of the region's pressure. A district declares its own bubble instead, and
    /// <see cref="SafeZones"/> holds them all.
    ///
    /// ⚠️ Scripted spawns (quests, world events with a fixed point) bypass safe zones entirely, so this
    /// never makes a district un-attackable by design — only by accident.
    /// </summary>
    [Export] public float SafeRadius { get; set; }

    /// <summary>
    /// Trade tags this place is <b>awash in</b> — goods worth less here than anywhere else in the realm
    /// (Phase 38G). Ore twenty metres from the seam, fish pulled out of the water behind the stall.
    ///
    /// ⚠️ <b>Empty is not "unfinished", it is the reference.</b> The town square and the Embermarket
    /// author nothing on purpose: a multiplier applied everywhere is a multiplier nowhere, and the two
    /// districts where the player learns what things cost are what the mine and the coast are read
    /// against. That was also 38G's parking notice — a system that reads identically in all three
    /// settlements is correct, validated and completely imperceptible.
    ///
    /// A tag here and in <see cref="Demand"/> at once is authoring nonsense and <c>--validate</c>
    /// refuses it. The vocabulary is <c>TradeTags</c>, held to by the same validator.
    /// </summary>
    [Export] public Godot.Collections.Array<string> Surplus { get; set; } = new();

    /// <summary>
    /// Trade tags this place is <b>short of</b> — goods worth more here, because everything of the kind
    /// came up the road (Phase 38G). Nothing grows in a hole in the ground.
    ///
    /// ⚠️ <b>A surplus at one end and a demand at the other is what makes a carry pay</b>, and it takes
    /// both: one side moving is still a loss, because the buy/sell spread it has to clear is about
    /// 1.84× at a specialist. `RegionDemand` carries the arithmetic.
    /// </summary>
    [Export] public Godot.Collections.Array<string> Demand { get; set; } = new();
}
