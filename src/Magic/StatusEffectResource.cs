using Embervale.Combat;
using Embervale.Stats;
using Godot;

namespace Embervale.Magic;

/// <summary>
/// A designer-authored status effect: a timed condition applied to an entity by a
/// spell (and, later, by traps, terrain or items). It can deal damage over time
/// (<see cref="DamagePerTick"/> at <see cref="TickInterval"/>) and/or apply a single
/// stat modifier (<see cref="ModStat"/>/<see cref="ModType"/>/<see cref="ModValue"/>),
/// which covers burns, chills/slows and buffs alike. Authored as a <c>.tres</c> under
/// <c>data/status_effects/</c> and indexed by <see cref="StatusEffectDatabase"/>.
///
/// One stat modifier per effect keeps the resource simple and authorable (mirroring
/// <see cref="Embervale.Progression.PerkResource"/>); compound effects can be authored
/// as several effects applied together.
/// </summary>
[GlobalClass]
public partial class StatusEffectResource : Resource
{
    /// <summary>Stable id, e.g. "status.burning". The database/lookup key.</summary>
    [Export] public string Id { get; set; } = "status.unknown";

    [Export] public string DisplayName { get; set; } = "Unknown Effect";

    /// <summary>The school this effect belongs to (tinting / future resistances).</summary>
    [Export] public DamageType School { get; set; } = DamageType.Fire;

    /// <summary>Beneficial effects (buffs) are surfaced differently in UI and are not
    /// treated as hostile afflictions.</summary>
    [Export] public bool IsBeneficial { get; set; } = false;

    /// <summary>Total lifetime in seconds. Re-applying refreshes to this value.</summary>
    [Export] public float Duration { get; set; } = 4f;

    [ExportGroup("Damage Over Time")]
    /// <summary>Damage dealt each tick (0 = no DoT). Credited to the effect's source.</summary>
    [Export] public float DamagePerTick { get; set; } = 0f;

    /// <summary>Health restored to the bearer each tick (0 = no HoT) — Nature regrowth (Phase 29.5B).</summary>
    [Export] public float HealPerTick { get; set; } = 0f;

    /// <summary>Seconds between DoT/HoT ticks.</summary>
    [Export] public float TickInterval { get; set; } = 1f;

    /// <summary>How many times the effect can stack on one bearer (Fire ignite, Phase 29.5B). 1 = no
    /// stacking (re-applying only refreshes duration); higher multiplies the per-tick DoT by the
    /// current stack count.</summary>
    [Export] public int MaxStacks { get; set; } = 1;

    [ExportGroup("Stat Modifier")]
    [Export] public StatType ModStat { get; set; } = StatType.MoveSpeed;
    [Export] public ModifierType ModType { get; set; } = ModifierType.PercentMult;

    /// <summary>Modifier value (0 = no stat modifier). e.g. -0.5 PercentMult = a 50% slow.</summary>
    [Export] public float ModValue { get; set; } = 0f;

    public bool HasDamageOverTime => DamagePerTick > 0f && TickInterval > 0f;

    public bool HasHealOverTime => HealPerTick > 0f && TickInterval > 0f;

    /// <summary>Any per-tick effect (damage or healing) that needs the tick timer advanced.</summary>
    public bool HasTickEffect => HasDamageOverTime || HasHealOverTime;

    public bool HasStatModifier => ModValue != 0f;
}
