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
    /// <summary>An authored cast timeline, when this spell wants one. Empty derives a shape from
    /// <see cref="CastMode"/> — see <c>SpellActions.For</c>.</summary>
    [Export] public Embervale.Combat.Actions.ActionDefinitionResource? CastAction { get; set; }

    [Export] public CastMode CastMode { get; set; } = CastMode.Instant;

    /// <summary>Minimum corruption tier the caster must have reached to learn this spell
    /// (Phase 23H). <see cref="CorruptionTier.Untainted"/> (the default) leaves a spell
    /// ungated; a higher value marks it a corrupted variant unlocked only by corruption.</summary>
    [Export] public CorruptionTier MinCorruptionTier { get; set; } = CorruptionTier.Untainted;

    /// <summary>Whether this spell belongs in the player's spellbook at all (Phase 34D). The
    /// spellbook lists every spell in the database, so a monster's loadout would otherwise appear
    /// as purchasable — set false for enemy-only spells. Default true: every spell authored before
    /// the caster roster is a player spell.</summary>
    [Export] public bool PlayerLearnable { get; set; } = true;

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

    /// <summary>Full opening angle of a <see cref="SpellDelivery.Cone"/> cast, in degrees — 60 is a
    /// wedge you step out of, 160 is a wall you run from. The cone's <em>length</em> is
    /// <see cref="ImpactRadius"/>, so a cone needs no range field of its own. Ignored by every other
    /// delivery shape.</summary>
    [Export] public float ConeAngleDegrees { get; set; } = 0f;

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

    // --- magic upgrade 2026-09: every field below defaults to "off", so a spell authored before it is unchanged ---

    [ExportGroup("Committed cast (magic upgrade)")]
    /// <summary>Seconds of visible wind-up before the spell leaves the hand. 0 derives it from the cast
    /// action (<c>SpellActions.For</c>). A stagger during the wind-up cancels the cast (poise is
    /// symmetric), so this is also how long a caster is open to being interrupted.</summary>
    [Export] public float WindupSeconds { get; set; } = 0f;

    /// <summary>Seconds after release before the caster can act again. 0 derives it.</summary>
    [Export] public float RecoverySeconds { get; set; } = 0f;

    /// <summary>Poise damage a hit deals. 0 keeps the current default.</summary>
    [Export] public float PoiseDamage { get; set; } = 0f;

    /// <summary>False makes the spell unblockable: a guard does not stop it (still never parryable).</summary>
    [Export] public bool Blockable { get; set; } = true;

    /// <summary>False lets the cast finish through a stagger (hyperarmoured casting, bosses, Barkskin).</summary>
    [Export] public bool Interruptible { get; set; } = true;

    /// <summary>Hit-stop and shake weight of the blow, 0..1. Presentation only, scaled by <c>CombatComfort</c>.</summary>
    [Export] public float ImpactWeight { get; set; } = 0.5f;

    [ExportGroup("Ground, barrier, dash (magic upgrade)")]
    /// <summary>Furthest point a <see cref="SpellDelivery.Ground"/> or <see cref="SpellDelivery.Barrier"/>
    /// cast can be placed from the caster, metres. The aim point is clamped to it.</summary>
    [Export] public float PlaceRange { get; set; } = 18f;

    /// <summary>Seconds a Ground cast telegraphs before it lands. The ground ring shows exactly this.</summary>
    [Export] public float GroundDelay { get; set; } = 0f;

    /// <summary>Pull toward the centre for a Ground spell that draws foes in (Gravity Well), m/s. 0 = none.</summary>
    [Export] public float PullStrength { get; set; } = 0f;

    /// <summary>Seconds a <see cref="SpellDelivery.Barrier"/> stands.</summary>
    [Export] public float BarrierDuration { get; set; } = 0f;

    /// <summary>Width of a barrier across the aim, metres.</summary>
    [Export] public float BarrierWidth { get; set; } = 4f;

    /// <summary>Damage a barrier absorbs before it breaks. 0 = it cannot be broken, only expire.</summary>
    [Export] public float BarrierHealth { get; set; } = 0f;

    /// <summary>A barrier eats projectiles and spells (Pyre Wall burns arrows, Bulwark stops all).</summary>
    [Export] public bool BarrierBlocksProjectiles { get; set; } = true;

    /// <summary>A barrier is solid to bodies (Glacial Bulwark). False = a hazard you can walk into.</summary>
    [Export] public bool BarrierBlocksBodies { get; set; } = false;

    /// <summary>Metres a <see cref="SpellDelivery.Dash"/> travels along the aim.</summary>
    [Export] public float DashDistance { get; set; } = 0f;

    /// <summary>Radius around the dash line that a dash strikes.</summary>
    [Export] public float DashHitRadius { get; set; } = 1.4f;

    [ExportGroup("Costs and identity (magic upgrade)")]
    /// <summary>Health the caster pays (Soul Tithe). Refused rather than killing the caster.</summary>
    [Export] public float HealthCost { get; set; } = 0f;

    /// <summary>Status the caster gains on release (Soul Tithe's echo, a buff on a hit spell). Empty = none.</summary>
    [Export] public string SelfStatusEffectId { get; set; } = string.Empty;

    /// <summary>Status the spell strips from its target as it lands, adding damage per stack. Empty = none.</summary>
    [Export] public string ConsumesStatusId { get; set; } = string.Empty;

    /// <summary>Extra damage fraction per stack of <see cref="ConsumesStatusId"/> consumed (0 = none).</summary>
    [Export] public float BonusPerConsumedStack { get; set; } = 0f;

    [ExportGroup("Casting core additions (magic upgrade)")]
    /// <summary>Extra foes a projectile strikes beyond the first, flying on through each (Flame Lance).
    /// 0 = it stops on the first foe it hits.</summary>
    [Export] public int PierceCount { get; set; } = 0;

    /// <summary>Extra foes struck at full charge on top of <see cref="PierceCount"/>, scaled by how full the
    /// charge was (rounded). Charged casts only.</summary>
    [Export] public int PierceChargeBonus { get; set; } = 0;

    /// <summary>Extra status duration at full charge. 1 doubles the normal duration; 0 keeps it unchanged.</summary>
    [Export] public float StatusDurationChargeBonus { get; set; } = 0f;

    /// <summary>A ground spell crushes a guard only when its impact centre directly strikes the actor.</summary>
    [Export] public bool DirectHitGuardBreak { get; set; } = false;

    /// <summary>Hit points of a summoned totem (Lifebloom Totem). 0 uses the default; a totem can always be destroyed.</summary>
    [Export] public float SummonHealth { get; set; } = 0f;

    /// <summary>Fraction of normal movement kept while this spell is channelled. 1 opts out of the slow.</summary>
    [Export] public float ChannelMoveScale { get; set; } = 0.6f;

    /// <summary>A lingering zone or area also afflicts the caster standing in it with the spell's status
    /// (Blizzard: you can be caught in your own). Status only, never damage.</summary>
    [Export] public bool AffectsCaster { get; set; } = false;

    /// <summary>Seconds a Ground spell with <see cref="PullStrength"/> but no zone keeps drawing foes in
    /// after it lands. 0 uses 0.6.</summary>
    [Export] public float PullSeconds { get; set; } = 0f;

    public bool HasStatusEffect => !string.IsNullOrEmpty(StatusEffectId);

    public bool HasSelfStatus => !string.IsNullOrEmpty(SelfStatusEffectId);
}
