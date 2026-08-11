using Godot;

namespace Embervale.World;

/// <summary>
/// One place on the world map (Phase 39.5A): what it is, what it offers, and what authoritative
/// record it stands for.
///
/// ⚠️ <b>IT CARRIES NO COORDINATES, AND THAT IS THE WHOLE DESIGN.</b> A location's position is the
/// <see cref="MapLocationComponent"/>'s transform in the cell scene — authored where the thing
/// actually stands, parented to the stall or building it names. Authoring an X/Z here as well would
/// create precisely the two-records-one-truth split the brief's §4 forbids, and it would rot the
/// first time someone nudged a market stall: the pin would keep pointing at where the stall used to
/// be, and nothing would report it. This mirrors <see cref="TravelNodeComponent"/>, which has always
/// worked this way ("a node carries its own position/region — authored where it sits, not in a
/// database").
///
/// So this resource answers *what and why*; the scene answers *where*. Neither can drift from the
/// other, because neither one duplicates the other.
///
/// ⚠️ The link fields (<see cref="ShopId"/>, <see cref="ServiceId"/>, <see cref="DialogueId"/>) are
/// the same rule again pointed at a different database: the map never restates a shop's wares, a
/// service's price or an NPC's name — it holds the id and asks. A price the map printed itself would
/// be a price that disagreed with the counter the day someone changed a markup.
/// </summary>
[GlobalClass]
public partial class MapLocationResource : Resource
{
    /// <summary>Stable id, <c>location.&lt;district&gt;.&lt;name&gt;</c> (see <c>docs/IDS.md</c>).
    /// District-scoped rather than region-scoped for the reason 38L scoped shops that way: sixteen
    /// locations all reading <c>ember_crown</c> tells a reader nothing about where they are.</summary>
    [Export] public string Id { get; set; } = "location.unknown";

    /// <summary>Locale key for the display name. Never a literal — CLAUDE.md §6.</summary>
    [Export] public string NameKey { get; set; } = string.Empty;

    /// <summary>Locale key for the one-line description in the info panel. May be empty.</summary>
    [Export] public string DescriptionKey { get; set; } = string.Empty;

    [ExportGroup("Classification")]

    /// <summary>What this place is. Picks the marker glyph and the legend entry.</summary>
    [Export] public MapCategory Category { get; set; } = MapCategory.Landmark;

    /// <summary>
    /// Zoom priority. Leave <c>true</c> to take <see cref="MapCategories.DefaultTier"/> for the
    /// category, which is right nearly always; set <c>false</c> and author <see cref="Tier"/> for the
    /// exception the category cannot know about — a famous smithy is a landmark, a market stall
    /// selling the same trade is not.
    /// </summary>
    [Export] public bool TierFromCategory { get; set; } = true;

    /// <summary>Explicit tier, honoured only when <see cref="TierFromCategory"/> is false.</summary>
    [Export] public MapTier Tier { get; set; } = MapTier.Detail;

    /// <summary>The resolved tier — category default unless this resource overrides it.</summary>
    public MapTier EffectiveTier =>
        TierFromCategory ? MapCategories.DefaultTier(Category) : Tier;

    [ExportGroup("Where")]

    /// <summary>The cell this sits in (a <c>region.cell</c> id). Validated against
    /// <see cref="RegionDatabase"/>. Used for the breadcrumb and for grouping, never for position.</summary>
    [Export] public string CellId { get; set; } = string.Empty;

    // ⚠️ THERE IS NO DistrictKey HERE, AND THAT IS A DECISION (39.5A).
    //
    // The brief asked for districts. Embervale does not have any: what looks like a district
    // convention — the Embermarket's merchants being shop.embermarket.* rather than
    // shop.ember_crown.* — is an id namespace whose segments are exactly the cell names, so a
    // district label would have read "The Embermarket › The Embermarket" on every pin. Authoring a
    // field that every .tres sets to empty is the stub 40B forbids, so the field is not here.
    //
    // The condition for adding it, so this is a check rather than a verdict: WHEN A SETTLEMENT'S
    // CELL AUTHORS MORE THAN ONE NAMED QUARTER. Today every settlement is one cell and one name.

    [ExportGroup("Links")]

    /// <summary>The shop that trades here (a <c>shop.*</c> id), or empty. Validated.</summary>
    [Export] public string ShopId { get; set; } = string.Empty;

    /// <summary>The service sold here (a <c>service.*</c> id), or empty. Validated.</summary>
    [Export] public string ServiceId { get; set; } = string.Empty;

    /// <summary>The conversation of whoever keeps this place (a <c>dialogue.*</c> id), or empty.
    /// This is how the map names an NPC without holding a second copy of their name.</summary>
    [Export] public string DialogueId { get; set; } = string.Empty;

    /// <summary>The holding claimed here (a <c>property.*</c> id), or empty. Validated — and every
    /// property must be named by some location, the same rule shops and services get.</summary>
    [Export] public string PropertyId { get; set; } = string.Empty;

    /// <summary>The fast-travel node here (a <c>travel.*</c> id), or empty. ⚠️ Not validated, for the
    /// reason IDS.md already records for <c>travel.*</c>: travel nodes live in scenes and are
    /// discovered at runtime, so there is no database to check the id against.</summary>
    [Export] public string TravelNodeId { get; set; } = string.Empty;

    [ExportGroup("Visibility")]

    /// <summary>
    /// Reveal the moment the containing cell loads, rather than on approach.
    ///
    /// True for anything you can see from outside — a town, a keep, a tower on a hill. False for
    /// anything you have to walk up to, which is what stops arriving in a city from dumping forty
    /// pins on the map at once and deleting exploration.
    /// </summary>
    [Export] public bool RevealWithCell { get; set; }

    /// <summary>A story flag that must be set before this location can be discovered at all, or
    /// empty for none. ⚠️ Story flags have no database (IDS.md), so a typo here is silent — the same
    /// hole every <c>flag.*</c> reference has.</summary>
    [Export] public string RequiredFlagId { get; set; } = string.Empty;
}
