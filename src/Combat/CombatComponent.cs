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

    /// <summary>Fraction of poise damage a (mistimed) block still takes, so a held guard can be broken.</summary>
    [Export]
    public float BlockPoiseFactor { get; set; } = 0.5f;

    private StatsComponent? _stats;
    private float _poise;
    private double _staggerTimer;
    private float _blockElapsed;
    private bool _wasBlocking;

    /// <summary>Set by a controller (player input / AI) to raise the guard.</summary>
    public bool IsBlocking { get; set; }

    /// <summary>While true the entity ignores all incoming damage — the dodge i-frame window (Phase 29E).</summary>
    public bool IsInvulnerable { get; set; }

    public bool IsStaggered => _staggerTimer > 0d;

    public float PoiseNormalized => MaxPoise <= 0f ? 0f : Mathf.Clamp(_poise / MaxPoise, 0f, 1f);

    protected override void OnInitialize()
    {
        _stats = Entity!.GetComponent<StatsComponent>();
        _poise = MaxPoise;
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

        // Track time since the guard was raised — the parry window measures from that moment.
        _blockElapsed = IsBlocking ? (_wasBlocking ? _blockElapsed + (float)delta : 0f) : 0f;
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

        float amount = packet.Amount;
        bool blocked = false;

        if (IsBlocking)
        {
            // Timed block within the parry window: negate the hit and stagger the attacker (riposte opening).
            if (Parry.IsParry(_blockElapsed, ParryWindow))
            {
                packet.Source?.GetComponent<CombatComponent>()?.Stagger(ParryStaggerDuration);
                EventBus.Instance?.Publish(new EntityParriedEvent(Entity, packet.Source));
                return new DamageResult(0f, false, true, packet.Type);
            }

            // Mistimed/held block: chip through, costs stamina (no stamina → guard broken, full hit).
            if (_stats.GetCurrent(StatType.Stamina) >= BlockStaminaCost)
            {
                _stats.ModifyCurrent(StatType.Stamina, -BlockStaminaCost);
                amount *= 1f - BlockMitigation;
                blocked = true;
            }
        }

        float final = CombatMath.Mitigate(amount, packet.Type, _stats);
        _stats.ApplyDamage(final, packet.Source);

        // A block still chips poise (BlockPoiseFactor) so a held guard can be broken into a stagger.
        _poise -= blocked ? packet.PoiseDamage * BlockPoiseFactor : packet.PoiseDamage;
        if (_poise <= 0f)
        {
            _poise = MaxPoise;
            _staggerTimer = StaggerDuration;
            EventBus.Instance?.Publish(new EntityStaggeredEvent(Entity));
        }

        EventBus.Instance?.Publish(
            new DamageDealtEvent(packet.Source, Entity, final, packet.Type, packet.IsCrit, blocked));

        return new DamageResult(final, packet.IsCrit, blocked, packet.Type);
    }
}
