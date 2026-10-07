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
    /// <summary>Uniform scale on the authored model — lets a boss reuse a humanoid body at its own size.
    /// Match <see cref="CapsuleHeight"/> to the scaled model.</summary>
    [Export] public float ModelScale { get; set; } = 1f;

    [Export] public Color PlaceholderTint { get; set; } = new(0.45f, 0.45f, 0.48f);

    [ExportGroup("Behaviour")]
    /// <summary>Which <see cref="AIProfileResource"/> drives it (Phase 34A).</summary>
    [Export] public string AiProfileId { get; set; } = EnemyAIComponent.DefaultProfileId;

    /// <summary>Faction membership — AI aggression keys off the player's standing with it.</summary>
    [Export] public string FactionId { get; set; } = string.Empty;

    /// <summary>Spells it knows. Non-empty gives it a <see cref="Magic.SpellcastingComponent"/> and,
    /// with a standoff AI profile, turns it into a caster.</summary>
    [Export] public Godot.Collections.Array<string> KnownSpellIds { get; set; } = new();

    /// <summary>A breath weapon (Phase 35C): the id of a <see cref="SpellDelivery.Cone"/> spell this
    /// creature channels at its target. Must also appear in <see cref="KnownSpellIds"/> — the breath
    /// is cast through the ordinary spellcasting path, not around it. Empty = no breath.</summary>
    [Export] public string BreathSpellId { get; set; } = string.Empty;

    /// <summary>Seconds a breath channel is held open once it starts.</summary>
    [Export] public float BreathDuration { get; set; } = 1.6f;

    /// <summary>A conversation this creature will hold (Phase 35F): the id of a
    /// <see cref="Dialogue.DialogueResource"/>, which gives it a <see cref="Dialogue.DialogueComponent"/>
    /// so the player's interact raycast can talk to it. Empty for anything that only fights.
    /// A creature that talks should also sit in a faction the player is not hostile to, or it will
    /// attack before the prompt is ever readable.</summary>
    [Export] public string DialogueId { get; set; } = string.Empty;

    [ExportGroup("Body")]
    [Export] public float CapsuleRadius { get; set; } = 0.4f;
    [Export] public float CapsuleHeight { get; set; } = 1.8f;

    /// <summary>Per-zone hurtboxes (Phase 35A). Empty — every archetype before the dragon — gives the
    /// one whole-body capsule hurtbox instead, so this costs existing content nothing.</summary>
    [Export] public Godot.Collections.Array<HitZoneResource> HitZones { get; set; } = new();

    /// <summary>Where spells and a breath leave this body, from its feet in metres (negative Z is
    /// forward). Zero, the default, is the chest point every archetype has always cast from: three
    /// quarters of the capsule height, just ahead of the axis. A long-necked body sets it to the
    /// mouth, because the capsule no longer says where the head is: a 22 m dragon breathing from
    /// its chest is breathing from eight metres behind its own jaws.</summary>
    [Export] public Vector3 CastOrigin { get; set; } = Vector3.Zero;

    /// <summary>Build this archetype as a <see cref="BossEntity"/> rather than a plain
    /// <see cref="EnemyEntity"/>, so the Phase 28C boss healthbar and the 28D corruption-on-kill loop
    /// resolve it through the <c>ServiceLocator</c>. World bosses and the dragons set it.</summary>
    [Export] public bool IsBoss { get; set; }

    /// <summary>The <see cref="BossResource"/> driving its fight structure — phases, per-phase
    /// abilities, enrage (Phase 36A). Only meaningful alongside <see cref="IsBoss"/>; the content
    /// validator rejects one without the other rather than letting it be a silent no-op. Empty on a
    /// boss that wants the default three-stage escalation.</summary>
    [Export] public string BossId { get; set; } = string.Empty;

    /// <summary>Give it the directional bite/wing/tail attack set (Phase 35A) instead of one swing
    /// arc. Needs <see cref="HitZones"/> to be worth anything — it is the same body, attacking.</summary>
    [Export] public bool DirectionalMelee { get; set; }

    [ExportGroup("Combat")]
    [Export] public float MaxPoise { get; set; } = 40f;

    /// <summary>What kind of body this is when it is hit — how far a broken guard actually moves it.
    /// See <c>PoiseReaction</c>; the default is the humanoid the game is balanced around.</summary>
    [Export] public Combat.ReactionClass Reaction { get; set; } = Combat.ReactionClass.Humanoid;
    [Export] public float StaminaRegen { get; set; } = 12f;

    /// <summary>Mana regenerated per second — the pacing dial for a caster archetype. The default
    /// matches <see cref="Stats.StatsComponent"/>'s, so a non-caster never needs to set it.</summary>
    [Export] public float ManaRegen { get; set; } = 4f;
    [Export] public int XpValue { get; set; } = 30;

    [ExportGroup("Presentation")]
    /// <summary>A weapon model drawn in the right hand, hung on the hand socket through
    /// <see cref="Animation.EquipmentPresentationComponent"/> like the player's sword. Visual only:
    /// the blow still comes from <see cref="WeaponPath"/>. Empty (the default) holds nothing, and a
    /// rig with no hand bone holds nothing either.</summary>
    [Export] public string HeldWeaponPath { get; set; } = string.Empty;

    /// <summary>Uniform scale on <see cref="HeldWeaponPath"/>, on top of <see cref="ModelScale"/>:
    /// the weapon hangs under the body's rig, so it already grows with the body. This is for a
    /// body authored large at <c>ModelScale</c> 1, or a weapon meant to be oversized for its
    /// wielder.</summary>
    [Export] public float HeldWeaponScale { get; set; } = 1f;

    /// <summary>A flat colour laid over every surface of the authored model. Transparent (the
    /// default) leaves the model's own materials alone. Only for archetypes that share one mesh and
    /// would otherwise be the same creature at two sizes.</summary>
    [Export] public Color BodyTint { get; set; } = new(0f, 0f, 0f, 0f);
}
