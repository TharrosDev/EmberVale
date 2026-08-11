namespace Embervale.World;

/// <summary>
/// What a map location <em>is</em> (Phase 39.5A) — the fine-grained kind, which picks the marker
/// glyph and the label the legend and the info panel show.
///
/// ⚠️ <b>Every member here exists because content exists.</b> The list was derived from the realm's
/// 23 shops, 13 <see cref="Economy.ServiceKind"/>s and 15 cells rather than from a checklist of
/// things an RPG map might want, which is the brief's §12 rule. Adding a member with no authored
/// location behind it re-creates exactly the empty-promise heading 37.5E refused for the journal's
/// Failed section.
/// </summary>
public enum MapCategory
{
    // ── Settlements ──────────────────────────────────────────────────────────────────────────
    /// <summary>The realm's seat. One per region at most.</summary>
    Capital,

    /// <summary>A walled or gated settlement with several trades.</summary>
    Town,

    /// <summary>A handful of buildings and one or two trades.</summary>
    Village,

    /// <summary>A garrison, post or waystation — staffed, but not a home.</summary>
    Outpost,

    /// <summary>A homestead or working camp: occupied, unfortified.</summary>
    Camp,

    /// <summary>Unsettled country. Named so the player can reason about the gaps between places.</summary>
    Wilds,

    // ── Trade ────────────────────────────────────────────────────────────────────────────────
    /// <summary>Forge work: weapons, armour, repair, ironmongery.</summary>
    Smith,

    /// <summary>General goods — the merchant who buys a bit of everything.</summary>
    Merchant,

    /// <summary>Herbs, reagents, potions.</summary>
    Alchemist,

    /// <summary>Food, drink and travel rations.</summary>
    Provisioner,

    /// <summary>Cloth, leather, rope and sail — the weaver, tanner, chandler and joiner.</summary>
    Outfitter,

    /// <summary>Gems, curios and the appraiser's trade in what they are worth.</summary>
    Jeweller,

    /// <summary>Books, scrolls and tomes.</summary>
    Scriptorium,

    // ── Services ─────────────────────────────────────────────────────────────────────────────
    /// <summary>A bed: rest to an hour, and every resource stat refilled.</summary>
    Inn,

    /// <summary>A vault. Persistent storage without the property gate.</summary>
    Bank,

    /// <summary>Mounts.</summary>
    Stable,

    /// <summary>Teaches recipes and sells access, never a perk rank.</summary>
    Trainer,

    /// <summary>A board: contracts, commissions, hired blades.</summary>
    Contracts,

    /// <summary>A forge, workbench or alchemy table the player may use themselves.</summary>
    Crafting,

    /// <summary>The wager pit and the fighting ring.</summary>
    Arena,

    // ── Exploration ──────────────────────────────────────────────────────────────────────────
    /// <summary>A working mine or quarry.</summary>
    Mine,

    /// <summary>A delve, lair or ruin with something in it that fights.</summary>
    Dungeon,

    /// <summary>A thing you navigate by: a tower, a monument, a standing stone, a wreck.</summary>
    Landmark,

    // ── Travel ───────────────────────────────────────────────────────────────────────────────
    /// <summary>A gate, bridge or crossing — the way in or out.</summary>
    Gate,

    /// <summary>An attuned fast-travel waystone.</summary>
    Waystone,

    // ── Personal ─────────────────────────────────────────────────────────────────────────────
    /// <summary>A holding the player owns.</summary>
    Home,

    /// <summary>A marker the player placed themselves.</summary>
    Waypoint,
}

/// <summary>
/// The coarse grouping a <see cref="MapCategory"/> belongs to (Phase 39.5A).
///
/// This is the brief's §13 answer in one type: <b>the group picks the marker's shape and the filter
/// panel's rows; the category picks its glyph.</b> Twenty-six categories drawn as twenty-six subtly
/// different icons is the "30 markers that look almost identical" failure the brief names — six
/// silhouettes the eye can separate at a glance, each carrying a distinguishing glyph, is not.
/// It also means colour is never doing the work alone, which is what <see cref="UI.ColorVision"/>
/// exists for.
/// </summary>
public enum MapGroup
{
    Settlement,
    Trade,
    Service,
    Exploration,
    Travel,
    Personal,
}

/// <summary>
/// Marker priority (Phase 39.5A) — the brief's §14, and the mechanism that keeps a dense city
/// readable without clustering.
///
/// A tier is <em>how far away this is still worth knowing about</em>, not how important it is.
/// A capital matters at world scale; a tanner's stall matters when you are standing in the market.
/// </summary>
public enum MapTier
{
    /// <summary>Drawn at every zoom: settlements, regions, major landmarks, the player's holdings.</summary>
    Primary,

    /// <summary>Drawn from regional zoom in: dungeons, mines, gates, waystones, arenas.</summary>
    Secondary,

    /// <summary>Drawn at local zoom only: individual shops, stalls and counter services.</summary>
    Detail,
}

/// <summary>
/// Pure lookups over <see cref="MapCategory"/> (Phase 39.5A). Static and Godot-free so the test
/// suite can exercise them — the <c>CompassMath</c> / <c>ScheduleMath</c> / <c>CameraRigMath</c>
/// precedent, and the reason none of this logic lives in the <c>Control</c> that draws it.
/// </summary>
public static class MapCategories
{
    /// <summary>Which shape-group a category is drawn and filtered under.</summary>
    public static MapGroup GroupOf(MapCategory category) => category switch
    {
        MapCategory.Capital or MapCategory.Town or MapCategory.Village or
        MapCategory.Outpost or MapCategory.Camp or MapCategory.Wilds => MapGroup.Settlement,

        MapCategory.Smith or MapCategory.Merchant or MapCategory.Alchemist or
        MapCategory.Provisioner or MapCategory.Outfitter or MapCategory.Jeweller or
        MapCategory.Scriptorium => MapGroup.Trade,

        MapCategory.Inn or MapCategory.Bank or MapCategory.Stable or MapCategory.Trainer or
        MapCategory.Contracts or MapCategory.Crafting or MapCategory.Arena => MapGroup.Service,

        MapCategory.Mine or MapCategory.Dungeon or MapCategory.Landmark => MapGroup.Exploration,

        MapCategory.Gate or MapCategory.Waystone => MapGroup.Travel,

        MapCategory.Home or MapCategory.Waypoint => MapGroup.Personal,

        _ => MapGroup.Exploration,
    };

    /// <summary>
    /// The tier a category defaults to when its <c>.tres</c> does not override it.
    ///
    /// ⚠️ A <see cref="MapLocationResource"/> may still author its own tier, and one case genuinely
    /// needs to: the realm's <em>notable</em> smith is a landmark you navigate by, while a stall
    /// selling the same trade in a market row is not. Category cannot know which is which.
    /// </summary>
    public static MapTier DefaultTier(MapCategory category) => GroupOf(category) switch
    {
        MapGroup.Settlement => MapTier.Primary,
        MapGroup.Personal => MapTier.Primary,
        MapGroup.Exploration => MapTier.Secondary,
        MapGroup.Travel => MapTier.Secondary,
        MapGroup.Service => MapTier.Detail,
        MapGroup.Trade => MapTier.Detail,
        _ => MapTier.Detail,
    };

    /// <summary>Locale key for a category's display name — <c>map.category.smith</c>.</summary>
    public static string NameKey(MapCategory category) =>
        $"map.category.{Snake(category.ToString())}";

    /// <summary>Locale key for a group's display name — <c>map.group.settlement</c>.</summary>
    public static string NameKey(MapGroup group) => $"map.group.{Snake(group.ToString())}";

    /// <summary>Every category in a group, in declaration order. Drives the filter panel's rows.</summary>
    public static System.Collections.Generic.IEnumerable<MapCategory> InGroup(MapGroup group)
    {
        foreach (MapCategory category in System.Enum.GetValues<MapCategory>())
        {
            if (GroupOf(category) == group)
            {
                yield return category;
            }
        }
    }

    /// <summary>"Provisioner" -> "provisioner". Single-word members only, so no boundary handling.</summary>
    private static string Snake(string pascal) => pascal.ToLowerInvariant();
}
