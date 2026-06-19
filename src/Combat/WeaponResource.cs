using Godot;

namespace Embervale.Combat;

/// <summary>
/// Resource-driven definition of a melee weapon. Designers author <c>.tres</c>
/// files ("IronSword", "WarAxe", "Dagger") that tune damage, timing and feel
/// without code. A <see cref="MeleeWeaponComponent"/> consumes one to drive its
/// attack. This is the seed of the equipment/loot pipeline (Phases 6–7).
/// </summary>
[GlobalClass]
public partial class WeaponResource : Resource
{
    [Export] public string DisplayName { get; set; } = "Weapon";
    [Export] public DamageType DamageType { get; set; } = DamageType.Physical;

    [ExportGroup("Damage")]
    [Export] public float BaseDamage { get; set; } = 12f;
    [Export] public float PoiseDamage { get; set; } = 20f;
    [Export] public float StaminaCost { get; set; } = 12f;

    [ExportGroup("Timing (seconds, scaled by AttackSpeed)")]
    [Export] public float WindupTime { get; set; } = 0.15f;
    [Export] public float ActiveTime { get; set; } = 0.12f;
    [Export] public float RecoveryTime { get; set; } = 0.28f;

    /// <summary>Animation/feel speed multiplier; combines with the wielder's AttackSpeed stat.</summary>
    [Export] public float AttackSpeed { get; set; } = 1f;

    [ExportGroup("Combo")]
    [Export] public int ComboLength { get; set; } = 3;

    /// <summary>Extra damage multiplier applied at the final combo hit (the finisher).</summary>
    [Export] public float FinisherMultiplier { get; set; } = 1.5f;
}
