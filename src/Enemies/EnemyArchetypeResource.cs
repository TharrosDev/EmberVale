using Godot;

namespace Embervale.Enemies;

/// <summary>
/// An enemy as authored data (Phase 34B, humanoids; 34C, beasts): who they are, what they're made
/// of, what they fight with, how they behave and what they drop. Authored as a <c>.tres</c> under
/// <c>data/enemies/</c> and indexed by <see cref="EnemyArchetypeDatabase"/>, which registers each one
/// with <see cref="EnemyTemplateRegistry"/> at boot.
///
/// One <see cref="EnemyArchetypeFactory"/> builds all of them. Nine near-identical hand-written
/// factories would have been nine places to fix the next time the enemy assembly changes — the
/// bespoke factories that remain (goblin, acolyte, Iron King) earn it by doing something structurally
/// different; a bandit, a soldier and a wolf differ only in numbers, and numbers belong in data.
/// </summary>
[GlobalClass]
public partial class EnemyArchetypeResource : Resource
{
    /// <summary>Stable template id, e.g. <c>enemy.bandit</c> — what encounters/quests reference.</summary>
    [Export] public string Id { get; set; } = "enemy.unknown";

    /// <summary>Name shown on the nameplate. Player-facing text, so this is a <c>Loc</c> key.</summary>
    [Export] public string NameKey { get; set; } = string.Empty;

    [ExportGroup("Build")]
    [Export] public string AttributesPath { get; set; } = string.Empty;
    [Export] public string WeaponPath { get; set; } = string.Empty;
    [Export] public string LootTablePath { get; set; } = string.Empty;

    /// <summary>Visual model scene. Empty (or unloadable) falls back to a tinted capsule, which is
    /// how every enemy in this project has started life — see the goblin before Phase 30D.</summary>
    [Export] public string ModelPath { get; set; } = string.Empty;

    /// <summary>Capsule colour used for that fallback, so the four read apart before art lands.</summary>
    [Export] public Color PlaceholderTint { get; set; } = new(0.45f, 0.45f, 0.48f);

    [ExportGroup("Behaviour")]
    /// <summary>Which <see cref="AIProfileResource"/> drives it (Phase 34A).</summary>
    [Export] public string AiProfileId { get; set; } = EnemyAIComponent.DefaultProfileId;

    /// <summary>Faction membership — AI aggression keys off the player's standing with it.</summary>
    [Export] public string FactionId { get; set; } = string.Empty;

    /// <summary>Spells it knows. Non-empty gives it a <see cref="Magic.SpellcastingComponent"/> and,
    /// with a standoff AI profile, turns it into a caster.</summary>
    [Export] public Godot.Collections.Array<string> KnownSpellIds { get; set; } = new();

    [ExportGroup("Body")]
    [Export] public float CapsuleRadius { get; set; } = 0.4f;
    [Export] public float CapsuleHeight { get; set; } = 1.8f;

    [ExportGroup("Combat")]
    [Export] public float MaxPoise { get; set; } = 40f;
    [Export] public float StaminaRegen { get; set; } = 12f;

    /// <summary>Mana regenerated per second — the pacing dial for a caster archetype. The default
    /// matches <see cref="Stats.StatsComponent"/>'s, so a non-caster never needs to set it.</summary>
    [Export] public float ManaRegen { get; set; } = 4f;
    [Export] public int XpValue { get; set; } = 30;
}
