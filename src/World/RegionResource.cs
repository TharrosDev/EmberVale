using Godot;

namespace Embervale.World;

/// <summary>
/// A designer-authored region of the world (Phase 25): a named area within a <see cref="Realm"/>,
/// composed of one or more streamable sub-cells, with nominal bounds, an atmosphere bias, and links
/// to neighbouring regions (the seed of the map + fast-travel graph). Authored as a <c>.tres</c>
/// under <c>data/regions/</c> and indexed by <see cref="RegionDatabase"/> — a new region is a new
/// resource, no code.
///
/// The <see cref="RegionStreamer"/> (25B) reads <see cref="Cells"/> to load/unload scenes by
/// distance, and the map/fast-travel work (25E–25G) reads <see cref="Neighbours"/> and the
/// discovery graph.
/// </summary>
[GlobalClass]
public partial class RegionResource : Resource
{
    /// <summary>Stable id, e.g. "region.ember_crown". The save header + database key.</summary>
    [Export] public string Id { get; set; } = "region.unknown";

    [Export] public string DisplayName { get; set; } = "Unknown Region";

    /// <summary>The realm this region belongs to (the lore taxonomy it rolls up under).</summary>
    [Export] public Realm Realm { get; set; } = Realm.EmberCrown;

    /// <summary>Where the player appears when entering this region via a hard transition (Phase 25C).
    /// The transition handler teleports the player here; neighbour portals are placed relative to it.</summary>
    [Export] public Vector3 SpawnPoint { get; set; } = Vector3.Zero;

    /// <summary>
    /// Where this region's outbound portals stand, in world space (Phase 38M2). <c>Vector3.Zero</c> —
    /// the default — keeps the original behaviour of parking them at
    /// <c>SpawnPoint + (0, -1.2, -4)</c>, so no region that does not care is affected.
    ///
    /// The Ember Crown authors one because the Crossway Post is where the crossing *is*: 38M put a
    /// toll on the road and the road has a gate now, so a portal four metres from the player's bed
    /// would be the wardens charging for a door nobody uses.
    ///
    /// ⚠️ <b>One point per region, so a region with two neighbours would stack both portals on it.</b>
    /// The third region (the Ashen Wilds) made that real: see <see cref="NeighbourPortalPoints"/>,
    /// which places a door per neighbour and falls back to this point for any neighbour it leaves zero.
    /// </summary>
    [Export] public Vector3 PortalPoint { get; set; } = Vector3.Zero;

    /// <summary>
    /// Per-neighbour door placement, parallel to <see cref="Neighbours"/> (the Ashen Wilds, 2026-09).
    /// Entry <c>i</c> is where the portal to <c>Neighbours[i]</c> stands in THIS region's world space;
    /// a missing or zero entry falls back to <see cref="PortalPoint"/>, so a region with one crossing
    /// authors nothing here.
    /// </summary>
    [Export] public Godot.Collections.Array<Vector3> NeighbourPortalPoints { get; set; } = new();

    /// <summary>
    /// Per-origin landing, parallel to <see cref="Neighbours"/>: where a traveller arriving FROM
    /// <c>Neighbours[i]</c> is put down, so walking back through a border crossing lands at that
    /// crossing rather than at the region's spawn. Y is clearance above the ground, like
    /// <see cref="SpawnPoint"/>. A missing or zero entry lands at <see cref="SpawnPoint"/> (the
    /// original behaviour).
    /// </summary>
    [Export] public Godot.Collections.Array<Vector3> NeighbourArrivalPoints { get; set; } = new();

    /// <summary>Where the portal to <paramref name="neighbourId"/> stands (see
    /// <see cref="NeighbourPortalPoints"/>); zero means "no authored point".</summary>
    public Vector3 PortalPointFor(string neighbourId) => PerNeighbour(NeighbourPortalPoints, neighbourId, PortalPoint);

    /// <summary>Where a traveller from <paramref name="fromRegionId"/> lands (see
    /// <see cref="NeighbourArrivalPoints"/>).</summary>
    public Vector3 ArrivalPointFrom(string fromRegionId) => PerNeighbour(NeighbourArrivalPoints, fromRegionId, SpawnPoint);

    private Vector3 PerNeighbour(Godot.Collections.Array<Vector3> points, string neighbourId, Vector3 fallback)
    {
        int index = Neighbours.IndexOf(neighbourId);
        return index >= 0 && index < points.Count && points[index] != Vector3.Zero ? points[index] : fallback;
    }

    /// <summary>
    /// Story flag the player must carry before portals <em>into</em> this region appear (Phase 33D).
    /// Empty = always open. Declared on the destination rather than per-link, so a region that is
    /// meant to be earned is earned from every direction, and the bootstrap needs no special cases.
    /// </summary>
    [Export] public string UnlockFlagId { get; set; } = string.Empty;

    /// <summary>The streamable sub-cells that make up this region. The <see cref="RegionStreamer"/>
    /// loads/unloads these by distance (Phase 25B). The procedural sandbox keeps an always-loaded
    /// base and lists its peripheral cells here.</summary>
    [Export] public Godot.Collections.Array<RegionCellResource> Cells { get; set; } = new();

    /// <summary>Shared terrain/material language consumed by every authored cell presentation.</summary>
    [Export] public WorldEnvironmentProfileResource? EnvironmentProfile { get; set; }

    /// <summary>
    /// The versioned, art-directable recipe the region's geography is generated from — macro relief,
    /// mountain character, erosion, climate and hydrology.
    ///
    /// ⚠️ <b>A REGION WITHOUT ONE FALLS BACK TO THE PRE-GENERATOR NOISE FIELD</b>, which is two
    /// octaves of value noise and no geography at all. That fallback exists so a legacy region
    /// reproduces its exact old ground rather than silently moving under its own buildings; it is
    /// not a default anybody should ship, and <c>ValidateRegions</c> fails a region that omits this.
    /// One generator, one profile per region — never a forked generator per realm.
    /// </summary>
    [Export] public WorldGenerationProfileResource? GenerationProfile { get; set; }

    /// <summary>Shipping limits for the fully resident region, consumed by validation and telemetry.</summary>
    [Export] public WorldPerformanceBudgetResource? PerformanceBudget { get; set; }

    /// <summary>Nominal world-space extents of the region, for the streamer's distance budget and the
    /// map. The flat sandbox uses a generous box (its floor is an infinite plane).</summary>
    [Export] public Aabb Bounds { get; set; } = new(new Vector3(-256f, -16f, -256f), new Vector3(512f, 64f, 512f));

    [ExportGroup("Atmosphere bias")]
    /// <summary>The weather state this region favours/starts in (a <c>weather.*</c> id).</summary>
    [Export] public string DefaultWeatherId { get; set; } = "weather.clear";

    /// <summary>The day phase that best characterises the region (a mood hint; not a hard lock).</summary>
    [Export] public DayPhase DayPhaseBias { get; set; } = DayPhase.Day;

    [ExportGroup("Safe zone")]
    /// <summary>Centre (world space) of the region's single safe bubble — its town — where the ambient
    /// spawners keep enemies and hostile events out (Phase 27D follow-up). <see cref="SafeZoneRadius"/>
    /// 0 = no safe zone. Scripted spawns bypass it; see <see cref="SafeZones"/>.</summary>
    [Export] public Vector3 SafeZoneCenter { get; set; } = Vector3.Zero;

    [Export] public float SafeZoneRadius { get; set; }

    [ExportGroup("Magic")]
    /// <summary>The strength of the fading <b>Weave</b> in this region (Phase 29.5E), in [0,1]:
    /// 1 = magic flows full, lower = the Weave is failing here (ordinary casts weaken and cost more,
    /// corrupted casts grow stronger and cheaper). Dev-tunable mood dial; see <see cref="Weave"/>.</summary>
    [Export(PropertyHint.Range, "0,1,0.05")] public float WeavePotency { get; set; } = 1f;

    [ExportGroup("Graph")]
    /// <summary>Ids of directly-reachable neighbouring regions (the map + fast-travel adjacency).</summary>
    [Export] public Godot.Collections.Array<string> Neighbours { get; set; } = new();

    /// <summary>When true, only encounters and world events that name this region in their
    /// <c>RegionIds</c> roll here; the realm-agnostic ones (goblins, wolves, bandits) stay out. For a
    /// realm sealed off from the rest of the world.</summary>
    [Export] public bool ScopedContentOnly { get; set; }

    [ExportGroup("Toll")]

    /// <summary>
    /// Gold the road wardens take to let the player walk in through a portal (Phase 38M). <c>0</c> —
    /// the default — is an untolled road, so every region authored before the Crossway Post is
    /// unaffected without being edited (38I's rule that the default is the ungated case).
    ///
    /// Declared on the <em>destination</em> for the same reason <see cref="UnlockFlagId"/> is: one
    /// gate on one road, charged from whichever side the player approaches, and no per-link table for
    /// the bootstrap to special-case. It is charged in <c>WorldSessionDirector.OnRegionTransitionRequested</c>
    /// — the one place the portal and the <c>region</c> dev command both arrive — and <b>never</b> on
    /// fast travel, which pays <see cref="Economy.TravelFee"/> instead. See <see cref="Economy.TollFee"/>.
    /// </summary>
    [Export] public int TollGold { get; set; }

    /// <summary>Story flag that exempts the bearer permanently — the warden's permit. Empty on an
    /// untolled region.</summary>
    [Export] public string TollPermitFlagId { get; set; } = string.Empty;

    /// <summary>
    /// Story flag that covers <em>one</em> crossing and is cleared as it is used — what a bribe buys.
    /// Consumed rather than kept so the cheap route stays a repeating decision: a permanent pass for
    /// less gold than the permit would simply be the permit, and the toll would stop being a sink
    /// after one bribe.
    /// </summary>
    [Export] public string TollPassFlagId { get; set; } = string.Empty;

    /// <summary>
    /// The regions whose travellers pay <see cref="TollGold"/> (the Ashen Wilds, 2026-09). Empty — the
    /// default — charges arrivals from every direction, which was right while the Ember Crown had one
    /// border. It has more than one now, and the Crossway wardens do not stand at the Ashen Breach.
    /// </summary>
    [Export] public Godot.Collections.Array<string> TollFromRegionIds { get; set; } = new();

    /// <summary>The toll an arrival from <paramref name="fromRegionId"/> owes (0 = untolled road).</summary>
    public int TollFrom(string fromRegionId) =>
        TollGold > 0 && (TollFromRegionIds.Count == 0 || TollFromRegionIds.Contains(fromRegionId)) ? TollGold : 0;
}
