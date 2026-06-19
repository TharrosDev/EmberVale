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

    private StatsComponent? _stats;
    private float _poise;
    private double _staggerTimer;

    /// <summary>Set by a controller (player input / AI) to raise the guard.</summary>
    public bool IsBlocking { get; set; }

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
    }

    /// <summary>Resolves an incoming hit and applies it. Returns the resolved result.</summary>
    public DamageResult ReceiveDamage(DamagePacket packet)
    {
        if (_stats == null || !_stats.IsAlive || Entity == null)
        {
            return default;
        }

        float amount = packet.Amount;
        bool blocked = false;

        if (IsBlocking && _stats.GetCurrent(StatType.Stamina) >= BlockStaminaCost)
        {
            _stats.ModifyCurrent(StatType.Stamina, -BlockStaminaCost);
            amount *= 1f - BlockMitigation;
            blocked = true;
        }

        float final = CombatMath.Mitigate(amount, packet.Type, _stats);
        _stats.ApplyDamage(final);

        if (!blocked)
        {
            _poise -= packet.PoiseDamage;
            if (_poise <= 0f)
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
