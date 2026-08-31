using Embervale.Core.Diagnostics;
using Embervale.Core.Events;
using Embervale.Entities;
using Embervale.Stats;
using Godot;

namespace Embervale.Combat;

/// <summary>
/// The defender-side combat brain for an entity. It owns poise/stagger state and
/// blocking, resolves incoming <see cref="DamagePacket"/>s through
/// <see cref="CombatMath"/>, applies the result to the <see cref="StatsComponent"/>,
/// and raises combat events. A <see cref="Hurtbox"/> routes hits here.
/// </summary>
[GlobalClass]
public partial class CombatComponent : EntityComponent
{
    /// <summary>
    /// Faction id used to prevent friendly fire. A <see cref="Hitbox"/> ignores
    /// hurtboxes whose owner shares its team. 0 = player, 1 = hostile, others are
    /// independent (e.g. neutral training targets).
    /// </summary>
    [Export]
    public int Team { get; set; }

    [Export]
    public float MaxPoise { get; set; } = 50f;

    /// <summary>Poise recovered per second while not staggered.</summary>
    [Export]
    public float PoiseRegen { get; set; } = 20f;

    [Export]
    public float StaggerDuration { get; set; } = 0.6f;

    /// <summary>Fraction of damage negated while blocking (0..1).</summary>
    [Export]
    public float BlockMitigation { get; set; } = 0.7f;

    [Export]
    public float BlockStaminaCost { get; set; } = 10f;

    /// <summary>A hit landing within this many seconds of raising the guard is parried (Phase 29F).</summary>
    [Export]
    public float ParryWindow { get; set; } = 0.2f;

    /// <summary>How long a parried attacker is staggered — the riposte opening.</summary>
    [Export]
    public float ParryStaggerDuration { get; set; } = 1.1f;

    /// <summary>Stamina a parry costs. With the one-parry-per-guard-raise latch this stops free
    /// tap-block parry-spam from dominating the read (DESIGN §1.4).</summary>
    [Export]
    public float ParryStaminaCost { get; set; } = 12f;

    /// <summary>Fraction of poise damage a (mistimed) block still takes, so a held guard can be broken.</summary>
    [Export]
    public float BlockPoiseFactor { get; set; } = 0.5f;

    /// <summary>
    /// Half-width of the arc a guard covers, in degrees from the defender's facing.
    ///
    /// ⚠️ <b>A GUARD USED TO COVER EVERY DIRECTION AT ONCE.</b> <see cref="IsBlocking"/> is a plain
    /// bool and <see cref="ReceiveDamage"/> asked nothing else, so a held block absorbed — and could
    /// parry — a blow landing squarely in the defender's back. That removes the whole point of pack
    /// flanking (34A) and of the boss's own repositioning, and it applies to the player and every
    /// enemy alike. 100° each way is a shield's honest cover: generous enough that a hit from the
    /// side quarter still counts, narrow enough that being surrounded is a real problem.
    /// </summary>
    [Export]
    public float GuardArcDegrees { get; set; } = 100f;

    private StatsComponent? _stats;
    private float _poise;
    private double _staggerTimer;
    private float _blockElapsed;
    private bool _wasBlocking;
    private bool _parryConsumed;

    /// <summary>Set by a controller (player input / AI) to raise the guard.</summary>
    public bool IsBlocking { get; set; }

    /// <summary>While true the entity ignores all incoming damage — the dodge i-frame window (Phase 29E).</summary>
    public bool IsInvulnerable { get; set; }

    public bool IsStaggered => _staggerTimer > 0d;

    /// <summary>True while this actor is in its own attack wind-up (written by
    /// <see cref="MeleeWeaponComponent"/>, which owns that window). Read here because incoming poise
    /// damage is resolved here — see <see cref="WindupPoiseMultiplier"/>.</summary>
    public bool InWindup { get; set; }

    /// <summary>How much extra poise damage this actor takes while <see cref="InWindup"/>. Authored
    /// per boss phase (<c>BossPhaseResource.WindupPoiseMultiplier</c>) and pushed here by
    /// <c>BossController</c>; <c>1</c> — the default, and every non-boss — is no change.</summary>
    public float WindupPoiseMultiplier { get; set; } = 1f;

    public float PoiseNormalized => MaxPoise <= 0f ? 0f : Mathf.Clamp(_poise / MaxPoise, 0f, 1f);

    protected override void OnInitialize()
    {
        _stats = Entity!.GetComponent<StatsComponent>();
        ValidateAuthoring();
        _poise = MaxPoise;
    }

    /// <summary>
    /// Shouts about knobs that are outside the range the pipeline can honour.
    ///
    /// ⚠️ <b>EVERY ONE OF THESE USED TO PRODUCE BROKEN GAMEPLAY IN SILENCE.</b> A
    /// <see cref="BlockMitigation"/> over 1 makes <c>amount *= 1 - BlockMitigation</c> negative, and
    /// negative damage is healing — an enemy authored at 1.2 heals itself by guarding. A
    /// <see cref="MaxPoise"/> of 0 or less means <c>_poise &lt;= 0</c> on the first hit, so the actor
    /// staggers on every blow forever. Neither shows up as an error anywhere; they show up as a
    /// fight that feels wrong. The values are also clamped where they are used, so a bad
    /// <c>.tres</c> is loud AND survivable rather than loud and broken.
    /// </summary>
    private void ValidateAuthoring()
    {
        string who = Entity?.DisplayName ?? "an actor";
        if (BlockMitigation is < 0f or > 1f)
        {
            Log.Error($"{who}: BlockMitigation is {BlockMitigation}, outside 0..1. " +
                      "Over 1 would heal on block; clamped.");
        }

        if (BlockPoiseFactor < 0f)
        {
            Log.Error($"{who}: BlockPoiseFactor is {BlockPoiseFactor}; a negative factor restores " +
                      "poise on a blocked hit. Clamped to 0.");
        }

        if (MaxPoise <= 0f)
        {
            Log.Error($"{who}: MaxPoise is {MaxPoise}. Poise starts empty, so the actor would " +
                      "stagger on every hit for the rest of its life. Poise breaks are disabled " +
                      "for it instead.");
        }

        if (GuardArcDegrees is <= 0f or > 360f)
        {
            Log.Error($"{who}: GuardArcDegrees is {GuardArcDegrees}; a guard covers nothing at or " +
                      "below 0. Clamped to 1..360.");
        }
    }

    /// <summary>
    /// Is <paramref name="source"/> inside the arc a raised guard covers? An attacker with no body
    /// (a trap, a status tick, a scripted hit) counts as in front: there is no direction to judge,
    /// and refusing the block for that reason would be a worse guess than allowing it.
    /// </summary>
    private bool IsInGuardArc(IEntity? source)
    {
        if (Entity?.Body is not Node3D self || source?.Body is not Node3D attacker)
        {
            return true;
        }

        Vector3 toAttacker = attacker.GlobalPosition - self.GlobalPosition;
        toAttacker.Y = 0f;
        if (toAttacker.LengthSquared() < 1e-4f)
        {
            return true; // standing inside the defender; no meaningful bearing
        }

        Vector3 forward = -self.GlobalTransform.Basis.Z;
        forward.Y = 0f;
        if (forward.LengthSquared() < 1e-4f)
        {
            return true;
        }

        float arc = Mathf.Clamp(GuardArcDegrees, 1f, 360f);
        return forward.Normalized().Dot(toAttacker.Normalized()) >=
               Mathf.Cos(Mathf.DegToRad(arc));
    }

    public override void _Process(double delta)
    {
        if (_staggerTimer > 0d)
        {
            _staggerTimer -= delta;
        }
        else if (_poise < MaxPoise)
        {
            _poise = Mathf.Min(MaxPoise, _poise + (PoiseRegen * (float)delta));
        }

        // Track time since the guard was raised — the parry window measures from that moment, and each
        // raise re-arms the single parry (so a held guard can't chain free parries).
        if (IsBlocking && !_wasBlocking)
        {
            _blockElapsed = 0f;
            _parryConsumed = false;
        }
        else if (IsBlocking)
        {
            _blockElapsed += (float)delta;
        }
        else
        {
            _blockElapsed = 0f;
        }

        _wasBlocking = IsBlocking;
    }

    /// <summary>Forces a stagger of at least <paramref name="duration"/> seconds (e.g. an attacker that was
    /// parried), resetting poise and raising the stagger event.</summary>
    public void Stagger(float duration)
    {
        _staggerTimer = Mathf.Max(_staggerTimer, duration);
        _poise = MaxPoise;
        if (Entity != null)
        {
            EventBus.Instance?.Publish(new EntityStaggeredEvent(Entity));
        }
    }

    /// <summary>Resolves an incoming hit and applies it. Returns the resolved result.</summary>
    public DamageResult ReceiveDamage(DamagePacket packet)
    {
        if (_stats == null || !_stats.IsAlive || Entity == null)
        {
            return default;
        }

        // Dodge i-frames (Phase 29E): the hit whiffs entirely — no damage, no poise, no events.
        if (IsInvulnerable)
        {
            return default;
        }

        float amount = Mathf.Max(0f, packet.Amount);
        bool blocked = false;

        // A guard covers the front, not a sphere. See GuardArcDegrees.
        if (IsBlocking && IsInGuardArc(packet.Source))
        {
            // Timed block within the parry window: negate the hit and stagger the attacker (riposte opening).
            // Costs stamina and fires at most once per guard-raise, so tap-block spam can't parry for free.
            if (Parry.IsParry(_blockElapsed, ParryWindow) && !_parryConsumed
                && _stats.GetCurrent(StatType.Stamina) >= ParryStaminaCost)
            {
                _parryConsumed = true;
                _stats.ModifyCurrent(StatType.Stamina, -ParryStaminaCost);
                packet.Source?.GetComponent<CombatComponent>()?.Stagger(ParryStaggerDuration);
                EventBus.Instance?.Publish(new EntityParriedEvent(Entity, packet.Source));
                return new DamageResult(0f, false, true, packet.Type);
            }

            // Mistimed/held block: chip through, costs stamina (no stamina → guard broken, full hit).
            if (_stats.GetCurrent(StatType.Stamina) >= BlockStaminaCost)
            {
                _stats.ModifyCurrent(StatType.Stamina, -BlockStaminaCost);
                amount *= 1f - Mathf.Clamp(BlockMitigation, 0f, 1f);
                blocked = true;
            }
        }

        // Never negative: a mitigation that over-reduced would otherwise heal the defender, which
        // is what an out-of-range BlockMitigation used to do (see ValidateAuthoring).
        float final = Mathf.Max(0f, CombatMath.Mitigate(amount, packet.Type, _stats));
        _stats.ApplyDamage(final, packet.Source);

        // A kill blow doesn't also stagger the corpse — only poise-check a survivor (avoids a
        // Staggered event firing alongside the Died event on the same hit).
        if (_stats.IsAlive)
        {
            // A block still chips poise (BlockPoiseFactor) so a held guard can be broken into a
            // stagger; a defender caught in its own wind-up takes more (36C).
            _poise -= CombatMath.PoiseDamage(
                packet.PoiseDamage, blocked, Mathf.Max(0f, BlockPoiseFactor),
                InWindup ? WindupPoiseMultiplier : 1f);
            if (MaxPoise > 0f && _poise <= 0f)
            {
                _poise = MaxPoise;
                _staggerTimer = StaggerDuration;
                EventBus.Instance?.Publish(new EntityStaggeredEvent(Entity));
            }
        }

        EventBus.Instance?.Publish(
            new DamageDealtEvent(packet.Source, Entity, final, packet.Type, packet.IsCrit, blocked));

        return new DamageResult(final, packet.IsCrit, blocked, packet.Type);
    }
}
