using Embervale.Core.Events;
using Embervale.Entities;
using Embervale.Stats;
using Godot;

namespace Embervale.Combat;

/// <summary>
/// The attacker-side melee driver: a wind-up / active / recovery state machine
/// fed by a <see cref="WeaponResource"/>. During the active window it opens the
/// assigned <see cref="Hitbox"/> with a freshly rolled <see cref="DamagePacket"/>.
/// Chaining an attack during recovery advances a combo counter (the final hit is
/// a stronger finisher). Swings cost stamina and are blocked while staggered.
/// </summary>
[GlobalClass]
public partial class MeleeWeaponComponent : EntityComponent
{
    private enum Phase
    {
        Idle,
        Windup,
        Active,
        Recovery,
    }

    [Export]
    public WeaponResource? Weapon { get; set; }

    /// <summary>The swing volume, injected by the actor's factory/scene.</summary>
    public Hitbox? Hitbox { get; set; }

    public int ComboIndex { get; private set; }

    private StatsComponent? _stats;
    private CombatComponent? _combat;
    private Phase _phase = Phase.Idle;
    private double _timer;

    protected override void OnInitialize()
    {
        _stats = Entity!.GetComponent<StatsComponent>();
        _combat = Entity.GetComponent<CombatComponent>();
    }

    /// <summary>Requests a swing. Returns true if one was started.</summary>
    public bool TryAttack()
    {
        if (Weapon == null || _phase is Phase.Windup or Phase.Active)
        {
            return false;
        }

        if (_combat is { IsStaggered: true })
        {
            return false;
        }

        if (_stats != null && _stats.GetCurrent(StatType.Stamina) < Weapon.StaminaCost)
        {
            return false;
        }

        // Continuing from recovery advances the combo; a fresh swing resets it.
        ComboIndex = _phase == Phase.Recovery
            ? (ComboIndex + 1) % Mathf.Max(1, Weapon.ComboLength)
            : 0;

        _stats?.ModifyCurrent(StatType.Stamina, -Weapon.StaminaCost);
        EnterPhase(Phase.Windup);
        EventBus.Instance?.Publish(new AttackPerformedEvent(Entity!, ComboIndex));
        return true;
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_phase == Phase.Idle)
        {
            return;
        }

        _timer -= delta;
        if (_timer > 0d)
        {
            return;
        }

        switch (_phase)
        {
            case Phase.Windup:
                OpenHitbox();
                EnterPhase(Phase.Active);
                break;
            case Phase.Active:
                Hitbox?.Deactivate();
                EnterPhase(Phase.Recovery);
                break;
            case Phase.Recovery:
                _phase = Phase.Idle;
                ComboIndex = 0;
                break;
        }
    }

    private void EnterPhase(Phase phase)
    {
        _phase = phase;
        float speed = AttackSpeed();
        _timer = phase switch
        {
            Phase.Windup => Weapon!.WindupTime / speed,
            Phase.Active => Weapon!.ActiveTime / speed,
            Phase.Recovery => Weapon!.RecoveryTime / speed,
            _ => 0d,
        };
    }

    private void OpenHitbox()
    {
        if (Hitbox == null || Weapon == null)
        {
            return;
        }

        bool isFinisher = ComboIndex == Mathf.Max(1, Weapon.ComboLength) - 1;
        float baseDamage = Weapon.BaseDamage * (isFinisher ? Weapon.FinisherMultiplier : 1f);

        (float amount, bool isCrit) = CombatMath.RollAttack(baseDamage, _stats);
        var packet = new DamagePacket(amount, Weapon.DamageType, Entity, isCrit, Weapon.PoiseDamage);
        Hitbox.Activate(packet);
    }

    private float AttackSpeed()
    {
        float weaponSpeed = Weapon?.AttackSpeed ?? 1f;
        float statSpeed = _stats?.GetValue(StatType.AttackSpeed) ?? 1f;
        return Mathf.Max(0.1f, weaponSpeed * statSpeed);
    }
}
