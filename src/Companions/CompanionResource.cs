using Godot;

namespace Embervale.Companions;

/// <summary>
/// A designer-authored companion (Phase 32C): who they are, what they fight with, how they behave in
/// the band, and where their loyalty starts. Authored as a <c>.tres</c> under <c>data/companions/</c>
/// and indexed by <see cref="CompanionDatabase"/> — a new companion is a new resource plus a recruit
/// hook, no code, which is what makes the four Beta companions content rather than engineering.
///
/// Everything <see cref="CompanionFactory"/> used to hard-code (attributes, weapon, model, faction,
/// the follower envelope) now lives here.
/// </summary>
[GlobalClass]
public partial class CompanionResource : Resource
{
    /// <summary>Stable id, e.g. <c>companion.kael</c>. The registry/roster/save key.</summary>
    [Export] public string Id { get; set; } = "companion.unknown";

    /// <summary>The <c>Loc</c> key for the companion's name, resolved at display time.</summary>
    [Export] public string NameKey { get; set; } = string.Empty;

    /// <summary>The <c>Loc</c> key for a one-line epithet ("Shieldsworn of the Ember Crown").</summary>
    [Export] public string TitleKey { get; set; } = string.Empty;

    [ExportGroup("Build")]
    /// <summary>Path to the companion's <c>AttributeSet</c> <c>.tres</c>.</summary>
    [Export] public string AttributesPath { get; set; } = CompanionFactory.DefaultAttributesPath;

    /// <summary>Path to the <c>WeaponResource</c> its melee component swings.</summary>
    [Export] public string WeaponPath { get; set; } = CompanionFactory.DefaultWeaponPath;

    /// <summary>Path to the visual model scene (falls back to a capsule when missing).</summary>
    [Export] public string ModelPath { get; set; } = CompanionFactory.DefaultModelPath;

    /// <summary>Faction the companion belongs to, so hostile factions read them correctly.</summary>
    [Export] public string FactionId { get; set; } = Core.GameIds.Factions.Villagers;

    /// <summary>Spells the companion knows. Empty = a pure melee fighter (Kael in the slice); a
    /// populated list gives them a <c>SpellcastingComponent</c> for a caster companion.</summary>
    [Export] public Godot.Collections.Array<string> KnownSpellIds { get; set; } = new();

    [ExportGroup("Behaviour")]
    /// <summary>Metres behind the player its formation slot sits.</summary>
    [Export] public float FollowDistance { get; set; } = 3.0f;

    /// <summary>Radius it scans for hostiles (scaled by the standing order).</summary>
    [Export] public float EngageRadius { get; set; } = 14f;

    /// <summary>Weapon reach — inside this it swings instead of closing.</summary>
    [Export] public float AttackRange { get; set; } = 2.1f;

    /// <summary>How far it may stray from its anchor before breaking off (scaled by the order).</summary>
    [Export] public float LeashRadius { get; set; } = 18f;

    [ExportGroup("Loyalty")]
    /// <summary>Loyalty on the day they join (0–100). A reluctant recruit starts low.</summary>
    [Export] public int StartingLoyalty { get; set; } = 40;

    /// <summary>Loyalty granted for completing this companion's loyalty quest (Phase 32E).</summary>
    [Export] public int LoyaltyQuestReward { get; set; } = 30;

    /// <summary>The quest that recruits them, if any — authored content, read by the recruit hooks.</summary>
    [Export] public string RecruitQuestId { get; set; } = string.Empty;

    /// <summary>Their loyalty quest id, if any.</summary>
    [Export] public string LoyaltyQuestId { get; set; } = string.Empty;

    /// <summary>Their conversation graph id.</summary>
    [Export] public string DialogueId { get; set; } = string.Empty;
}
