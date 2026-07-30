using Godot;

namespace Embervale.Enemies;

/// <summary>Which page of the bestiary a creature is filed under (Phase 34G).</summary>
// APPEND ONLY: ordinals are authored into data/bestiary/*.tres — never reorder/insert/remove.
public enum BestiaryCategory
{
    Humanoid,
    Beast,
    Undead,
    Construct,
    Elemental,
    Ashen,
    Boss,
}

/// <summary>
/// One creature's page in the Ash Hunters' field journal (Phase 34G): what it is called, what is
/// known about it, and how much hunting it takes to learn that.
///
/// Keyed by <b>template id</b> rather than living on <see cref="EnemyArchetypeResource"/>, because
/// three creatures have no archetype at all — the goblin, the Iron King and the Ashen Acolyte are
/// built by bespoke factories and exist only in <see cref="EnemyTemplateRegistry"/>. A bestiary
/// missing the game's first enemy and its first boss would be absurd, so entries cover every
/// registered id and the <see cref="ContentValidator"/> enforces that both ways.
/// </summary>
[GlobalClass]
public partial class BestiaryEntryResource : Resource
{
    /// <summary>The enemy template this documents, e.g. <c>enemy.wolf</c>.</summary>
    [Export] public string Id { get; set; } = "enemy.unknown";

    /// <summary>Name override, needed only for the bespoke creatures that have no archetype to read
    /// a <c>NameKey</c> from. Empty means "use the archetype's".</summary>
    [Export] public string NameKey { get; set; } = string.Empty;

    /// <summary>Loc key for the page body. The enemy path has nowhere else to hang lore.</summary>
    [Export] public string LoreKey { get; set; } = string.Empty;

    [Export] public BestiaryCategory Category { get; set; } = BestiaryCategory.Humanoid;

    /// <summary>Kills before the full entry opens. 1 means the first kill tells you everything —
    /// which is how a boss you fight once is authored.</summary>
    [Export] public int KillsToKnow { get; set; } = 5;
}
