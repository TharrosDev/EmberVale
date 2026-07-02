using Embervale.Combat;
using Embervale.Corruption;
using Godot;

namespace Embervale.Magic;

/// <summary>
/// A designer-authored spell: the data a <see cref="SpellcastingComponent"/> needs
/// to resolve a cast. Authored as a <c>.tres</c> under <c>data/spells/</c> and
/// indexed by <see cref="SpellDatabase"/> — a new spell is a new resource, no code.
///
/// A spell's <see cref="School"/> is its <see cref="DamageType"/>, so it flows
/// straight through the existing damage pipeline (mitigation/resistance) and tints
/// its projectile via <see cref="SpellSchools"/>. <see cref="Delivery"/> selects the
/// shape (bolt / burst / self); the remaining fields are read per shape.
/// </summary>
[GlobalClass]
public partial class SpellResource : Resource
{
    /// <summary>Stable id, e.g. "spell.firebolt". The save/database key.</summary>
    [Export] public string Id { get; set; } = "spell.unknown";

    [Export] public string DisplayName { get; set; } = "Unknown Spell";

    [Export(PropertyHint.MultilineText)]
    public string Description { get; set; } = string.Empty;

    /// <summary>The damage school; also the <see cref="DamageType"/> of the hit.</summary>
    [Export] public DamageType School { get; set; } = DamageType.Fire;

    [Export] public SpellDelivery Delivery { get; set; } = SpellDelivery.Projectile;

    /// <summary>How the cast plays out over time (Phase 29.5A): Instant, Charged or Channeled.</summary>
    [Export] public CastMode CastMode { get; set; } = CastMode.Instant;

    /// <summary>Minimum corruption tier the caster must have reached to learn this spell
    /// (Phase 23H). <see cref="CorruptionTier.Untainted"/> (the default) leaves a spell
    /// ungated; a higher value marks it a corrupted variant unlocked only by corruption.</summary>
    [Export] public CorruptionTier MinCorruptionTier { get; set; } = CorruptionTier.Untainted;

    [ExportGroup("Costs")]
    [Export] public float ManaCost { get; set; } = 10f;

    /// <summary>Seconds before the spell can be cast again.</summary>
    [Export] public float Cooldown { get; set; } = 1f;

    [ExportGroup("Effect")]
    /// <summary>Base damage before SpellPower scaling (0 for pure heals/buffs).</summary>
    [Export] public float BaseDamage { get; set; } = 0f;

    /// <summary>Health restored to the caster for a <see cref="SpellDelivery.Self"/> cast.</summary>
    [Export] public float Healing { get; set; } = 0f;

    /// <summary>Optional status effect id applied to those the spell affects (or the
    /// caster, for a Self cast). Resolved via <see cref="StatusEffectDatabase"/>.</summary>
    [Export] public string StatusEffectId { get; set; } = string.Empty;

    [ExportGroup("Delivery")]
    /// <summary>Metres a projectile travels before it expires (Projectile only).</summary>
    [Export] public float Range { get; set; } = 30f;

    /// <summary>Projectile travel speed in metres/second (Projectile only).</summary>
    [Export] public float ProjectileSpeed { get; set; } = 18f;

    /// <summary>
    /// Burst radius in metres. For a <see cref="SpellDelivery.Area"/> cast this is the
    /// nova radius around the caster; for a Projectile, a value &gt; 0 makes it
    /// detonate as an area-of-effect on impact instead of hitting a single target.
    /// </summary>
    [Export] public float ImpactRadius { get; set; } = 0f;

    [ExportGroup("Mastery (skill-point buy/upgrade)")]
    /// <summary>Skill points to learn (buy) this spell from the character screen.</summary>
    [Export] public int LearnCost { get; set; } = 1;

    /// <summary>Skill points per rank-up.</summary>
    [Export] public int UpgradeCost { get; set; } = 1;

    /// <summary>Highest rank the spell can be upgraded to (1 = no upgrades).</summary>
    [Export] public int MaxRank { get; set; } = 3;

    /// <summary>Added damage/healing fraction per rank above the first (rank 2 = +1×this, etc.).</summary>
    [Export] public float DamagePerRank { get; set; } = 0.3f;

    [ExportGroup("Cast timing (Phase 29.5A)")]
    /// <summary>Seconds of holding to reach full charge (Charged casts).</summary>
    [Export] public float ChargeTime { get; set; } = 1.2f;

    /// <summary>Damage/healing multiplier at full charge; a min-charge release deals 1x (Charged casts).</summary>
    [Export] public float MaxChargeMultiplier { get; set; } = 2.5f;

    /// <summary>Seconds between channel ticks (Channeled casts).</summary>
    [Export] public float ChannelTickInterval { get; set; } = 0.2f;

    /// <summary>Mana drained per second while channeling (Channeled casts).</summary>
    [Export] public float ChannelManaPerSecond { get; set; } = 14f;

    [ExportGroup("Signature mechanics (Phase 29.5G)")]
    /// <summary>If &gt; 0, a projectile steers toward the nearest hostile within this radius each frame
    /// (Ball Lightning). 0 = flies straight.</summary>
    [Export] public float HomingRange { get; set; } = 0f;

    /// <summary>If &gt; 0, a Self cast teleports the caster this many metres along their aim (Blink).</summary>
    [Export] public float BlinkDistance { get; set; } = 0f;

    /// <summary>If &gt; 0, the cast spawns a lingering zone that re-detonates the spell at the caster
    /// every <see cref="ZoneTickInterval"/> for this many seconds (Blizzard). Uses <see cref="ImpactRadius"/>
    /// as its radius.</summary>
    [Export] public float ZoneDuration { get; set; } = 0f;

    /// <summary>Seconds between a lingering zone's pulses (defaults to 1 when unset).</summary>
    [Export] public float ZoneTickInterval { get; set; } = 1f;

    /// <summary>If &gt; 0, the cast summons a totem that heals the caster by <see cref="Healing"/> every
    /// <see cref="SummonTickInterval"/> for this many seconds (Lifebloom Totem).</summary>
    [Export] public float SummonDuration { get; set; } = 0f;

    /// <summary>Seconds between a summoned totem's pulses (defaults to 1 when unset).</summary>
    [Export] public float SummonTickInterval { get; set; } = 1f;

    public bool HasStatusEffect => !string.IsNullOrEmpty(StatusEffectId);
}
