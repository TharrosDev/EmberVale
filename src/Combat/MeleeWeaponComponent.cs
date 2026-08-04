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

    /// <summary>How long an attack press stays buffered while committed (Phase 29G), in seconds.</summary>
    [Export]
    public float BufferWindow { get; set; } = 0.18f;

    /// <summary>The swing volume, injected by the actor's factory/scene.</summary>
    public Hitbox? Hitbox { get; set; }

    public int ComboIndex { get; private set; }

    private StatsComponent? _stats;
    private CombatComponent? _combat;
    private Phase _phase = Phase.Idle;
    private double _timer;
    private double _buffer;

    protected override void OnInitialize()
    {
        _stats = Entity!.GetComponent<StatsComponent>();
        _combat = Entity.GetComponent<CombatComponent>();
    }

    /// <summary>True during the committed window (wind-up + active) — no new swing or dodge can start
    /// (Phase 29G); a press here is buffered instead of dropped.</summary>
    public bool IsCommitted => _phase is Phase.Windup or Phase.Active;

    /// <summary>Requests a swing. Starts one if able; if pressed mid-commit it's <b>buffered</b> and
    /// auto-released the instant the swing reaches its cancel window (Phase 29G). Returns true if a swing
    /// started now.</summary>
    public bool TryAttack()
    {
        if (Weapon == null)
        {
            return false;
        }

        if (IsCommitted)
        {
            _buffer = BufferWindow; // queue it — released when the commit ends, so an early press still lands
            return false;
        }

        return StartSwing();
    }

    private bool StartSwing()
    {
        if (Weapon == null || _combat is { IsStaggered: true })
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

        // The telegraph is told the *effective* wind-up, not the authored one: a phase buff or a
        // slow debuff moves the danger window, and a cue that ignores that is worse than none.
        EventBus.Instance?.Publish(new AttackPerformedEvent(Entity!, ComboIndex, (float)_timer));
        return true;
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_buffer > 0d)
        {
            _buffer -= delta;
        }

        // Release a buffered swing as soon as we leave the commit window (cancelling recovery into the
        // next combo hit, or starting fresh from idle).
        if (AttackBuffer.ShouldRelease(_buffer, IsCommitted) && StartSwing())
        {
            _buffer = 0d;
            return;
        }

        if (_phase == Phase.Idle)
        {
            return;
        }

        // Interrupt (36C): a stagger during the wind-up cancels the swing outright — the hitbox
        // never opens. Only the wind-up is interruptible; once the blow is live it is committed,
        // which is what keeps the punish window a readable thing to aim for rather than a race.
        // Before this a stagger only stopped a swing from *starting*, so staggering a boss mid-
        // wind-up did nothing at all and the blow landed anyway.
        if (_phase == Phase.Windup && _combat is { IsStaggered: true })
        {
            CancelSwing();
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
                SetWindup(false);
                break;
        }
    }

    /// <summary>Drops the swing mid-wind-up and tells anything presenting it to stop.</summary>
    private void CancelSwing()
    {
        _phase = Phase.Idle;
        ComboIndex = 0;
        _buffer = 0d;   // a queued press must not fire the instant the stagger lifts
        SetWindup(false);
        Hitbox?.Deactivate();
        EventBus.Instance?.Publish(new AttackInterruptedEvent(Entity!));
    }

    /// <summary>Mirrors the wind-up window onto the combat component, which is where incoming poise
    /// damage is resolved and therefore where a phase's wind-up vulnerability has to be applied.</summary>
    private void SetWindup(bool inWindup)
    {
        if (_combat != null)
        {
            _combat.InWindup = inWindup;
        }
    }

    private void EnterPhase(Phase phase)
    {
        _phase = phase;
        SetWindup(phase == Phase.Windup);
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
        if (Weapon == null)
        {
            return;
        }

        bool isFinisher = ComboIndex == Mathf.Max(1, Weapon.ComboLength) - 1;
        float baseDamage = Weapon.BaseDamage * (isFinisher ? Weapon.FinisherMultiplier : 1f);

        (float amount, bool isCrit) = CombatMath.RollAttack(baseDamage, _stats);
        var packet = new DamagePacket(amount, Weapon.DamageType, Entity, isCrit, Weapon.PoiseDamage);
        Hitbox?.Activate(packet);
    }

    private float AttackSpeed()
    {
        float weaponSpeed = Weapon?.AttackSpeed ?? 1f;
        float statSpeed = _stats?.GetValue(StatType.AttackSpeed) ?? 1f;
        return Mathf.Max(0.1f, weaponSpeed * statSpeed);
    }
}
