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

    // --- magic upgrade 2026-09: defaults are "off", so an existing status is unchanged ---

    [ExportGroup("Control and rules (magic upgrade)")]
    /// <summary>What this status does to the bearer beyond stats: roots, silences, stuns, marks.</summary>
    [Export] public StatusControl Controls { get; set; } = StatusControl.None;

    /// <summary>False protects the status from Dispel and Cleanse (deep curses).</summary>
    [Export] public bool Dispellable { get; set; } = true;

    /// <summary>Seconds after this status ends during which the bearer is immune to a fresh controlling
    /// status (diminishing returns: a rooted or silenced actor is never chain-locked).</summary>
    [Export] public float ControlImmunitySeconds { get; set; } = 0f;

    /// <summary>Stack count at which the status detonates and is consumed (Kindle). 0 = never.</summary>
    [Export] public int DetonateAtStacks { get; set; } = 0;

    /// <summary>Damage of that detonation per stack, in the status's school.</summary>
    [Export] public float DetonateDamagePerStack { get; set; } = 0f;

    /// <summary>Radius of that detonation; 0 hits only the bearer.</summary>
    [Export] public float DetonateRadius { get; set; } = 0f;

    /// <summary>On the bearer's death the status jumps to living hostiles within this radius
    /// (Stinging Swarm). 0 = it dies with the bearer.</summary>
    [Export] public float SpreadOnDeathRadius { get; set; } = 0f;

    /// <summary>Fraction of incoming damage this status changes: positive amplifies (a mark), negative
    /// absorbs (a ward). 0 = none.</summary>
    [Export] public float DamageTakenModifier { get; set; } = 0f;

    [ExportGroup("Rules (magic upgrade, status group)")]
    /// <summary>Fixed damage a ward absorbs before it breaks (Arcane Ward). Pairs with a negative
    /// <see cref="DamageTakenModifier"/>, which is the fraction of each hit the ward takes. 0 = no pool.</summary>
    [Export] public float AbsorbAmount { get; set; } = 0f;

    /// <summary>Extra ward capacity per point of the caster's SpellPower.</summary>
    [Export] public float AbsorbPerSpellPower { get; set; } = 0f;

    /// <summary>Mana an entirely unspent ward returns to its caster when it expires (scaled by the
    /// unspent share). Broken or dispelled wards return nothing.</summary>
    [Export] public float ExpiryManaReturn { get; set; } = 0f;

    /// <summary>Mana the bearer regains for each kill it makes while this is active (Soul Echo).</summary>
    [Export] public float ManaOnKill { get; set; } = 0f;

    /// <summary>Status applied to everything a detonation hits, the bearer included (Kindle ignites).</summary>
    [Export] public string DetonateAppliesStatusId { get; set; } = string.Empty;

    /// <summary>Locale key stem: <c>magic.status.kindled</c> for <c>status.kindled</c>.</summary>
    public string LocKey => "magic.status." + (Id.StartsWith("status.", System.StringComparison.Ordinal) ? Id[7..] : Id);

    /// <summary>Player-visible name through <c>Loc</c>, falling back to <see cref="DisplayName"/>.</summary>
    public string LocalName => Embervale.Localization.Loc.Has(LocKey + ".name")
        ? Embervale.Localization.Loc.T(LocKey + ".name") : DisplayName;

    /// <summary>Player-visible one-line description through <c>Loc</c>, empty when none is authored.</summary>
    public string LocalDescription => Embervale.Localization.Loc.Has(LocKey + ".desc")
        ? Embervale.Localization.Loc.T(LocKey + ".desc") : string.Empty;

    public bool HasDamageOverTime => DamagePerTick > 0f && TickInterval > 0f;

    public bool HasHealOverTime => HealPerTick > 0f && TickInterval > 0f;

    /// <summary>Any per-tick effect (damage or healing) that needs the tick timer advanced.</summary>
    public bool HasTickEffect => HasDamageOverTime || HasHealOverTime;

    public bool HasStatModifier => ModValue != 0f;
}
