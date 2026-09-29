using Embervale.Combat.Actions;
using Embervale.Entities;
using Embervale.Movement;
using Embervale.Stats;
using Godot;

namespace Embervale.Combat;

/// <summary>
/// Dodge with invulnerability frames (Phase 29E; directional/curve/recovery upgrade in the movement pass).
/// On <see cref="TryDodge"/> it resolves a <see cref="DodgeKind"/> — a <b>roll</b> toward held input or a
/// shorter, cheaper <b>backstep</b> when none is held — spends stamina, and drives an ease-out burst through
/// the <see cref="LocomotionComponent"/> while the owner's <see cref="CombatComponent"/> ignores damage for the
/// i-frame window. A short recovery tail follows; presses during the roll, its recovery or a committed swing
/// are buffered rather than dropped, and an attack may cancel the roll once the i-frames have closed.
/// Back-to-back dodges cost more stamina, and a winded owner (<see cref="StatsComponent.IsWinded"/>) cannot
/// dodge at all. Every timing and cost is an export knob; the arithmetic lives in <see cref="Dodge"/>.
///
/// <para>Only the player carries one (<c>PlayerFactory</c>); no enemy or companion dodges, so none of this
/// reaches AI.</para>
/// </summary>
[GlobalClass]
public partial class DodgeComponent : EntityComponent
{
    [ExportGroup("Roll (direction held)")]
    [Export] public float StaminaCost { get; set; } = 22f;

    /// <summary>Speed at the push-off; the burst eases down from here (<see cref="Dodge.SpeedAt"/>).
    /// With the defaults the roll covers ≈3.5 m.</summary>
    [Export] public float RollSpeed { get; set; } = 15.5f;

    [Export] public float RollDuration { get; set; } = 0.45f;

    /// <summary>I-frames open this long after the roll starts…</summary>
    [Export] public float IFrameStart { get; set; } = 0.05f;

    /// <summary>…and last this long (the brief startup keeps the roll from being a free panic button).</summary>
    [Export] public float IFrameDuration { get; set; } = 0.30f;

    /// <summary>Seconds after the motion ends before another dodge may start.</summary>
    [Export] public float RollRecovery { get; set; } = 0.15f;

    [ExportGroup("Backstep (no direction held)")]
    [Export] public float BackstepStaminaCost { get; set; } = 12f;
    [Export] public float BackstepSpeed { get; set; } = 11f;
    [Export] public float BackstepDuration { get; set; } = 0.28f;
    [Export] public float BackstepIFrameStart { get; set; } = 0.03f;

    /// <summary>Shorter than the roll's: the backstep is the cheap spacing tool, not the panic button.</summary>
    [Export] public float BackstepIFrameDuration { get; set; } = 0.14f;

    [Export] public float BackstepRecovery { get; set; } = 0.12f;

    /// <summary>Burst speed at the end of the motion as a fraction of its peak (ease-out floor).</summary>
    [ExportGroup("Feel")]
    [Export(PropertyHint.Range, "0,1,0.05")] public float EndSpeedFraction { get; set; } = 0.2f;

    /// <summary>Ease-out exponent: 1 is a linear slowdown, higher front-loads the push.</summary>
    [Export] public float EaseExponent { get; set; } = 1.6f;

    /// <summary>Input length (0–1) below which a press is a backstep rather than a roll.</summary>
    [Export(PropertyHint.Range, "0,1,0.05")] public float DirectionDeadzone { get; set; } = 0.2f;

    /// <summary>Fraction of the motion after which an attack press cancels the dodge into a swing.
    /// Past the i-frame end for both kinds by default, so no swing ever starts invulnerable.</summary>
    [Export(PropertyHint.Range, "0,1,0.05")] public float AttackCancelFraction { get; set; } = 0.78f;

    /// <summary>How long a dodge or attack press stays buffered while it cannot yet be honoured.</summary>
    [Export] public float BufferWindow { get; set; } = 0.2f;

    /// <summary>A dodge starting within this many seconds of the last one finishing counts as chained.</summary>
    [ExportGroup("Anti-spam")]
    [Export] public float ChainWindow { get; set; } = 0.5f;

    /// <summary>Extra cost per chained dodge, as a fraction of the base cost.</summary>
    [Export] public float ChainSurcharge { get; set; } = 0.35f;

    /// <summary>Ceiling on the chained cost multiplier.</summary>
    [Export] public float MaxChainMultiplier { get; set; } = 2f;

    private LocomotionComponent? _locomotion;
    private StatsComponent? _stats;
    private CombatComponent? _combat;
    private MountComponent? _mount;
    private CharacterActionComponent? _weapon;

    private bool _active;
    private DodgeKind _kind;
    private Vector3 _dir;
    private float _elapsed;
    private int _chain;
    private double _sinceLastEnd = double.MaxValue;

    private double _dodgeBuffer;
    private Vector3 _bufferedDirection;
    private double _attackBuffer;

    /// <summary>True from the push-off until the recovery tail ends.</summary>
    public bool IsDodging => _active;

    /// <summary>The kind of the running (or last) dodge.</summary>
    public DodgeKind Kind => _kind;

    protected override void OnInitialize()
    {
        _locomotion = Entity!.GetComponent<LocomotionComponent>();
        _stats = Entity!.GetComponent<StatsComponent>();
        _combat = Entity!.GetComponent<CombatComponent>();
        _mount = Entity!.GetComponent<MountComponent>();
        _weapon = Entity!.GetComponent<CharacterActionComponent>();
    }

    protected override void OnTeardown()
    {
        // Never leave the owner stranded invulnerable — or sliding — if the component is torn down
        // mid-roll.
        End(cancelDash: true);
        _dodgeBuffer = 0d;
        _attackBuffer = 0d;
    }

    /// <summary>
    /// Requests a dodge. <paramref name="direction"/> is the world-space wish direction the controller
    /// supplies (the router orients it by body yaw, so it is camera-relative in practice); its length picks
    /// the kind — held input rolls that way, neutral input backsteps away from the body's facing (which is
    /// away from the target while locked on). A press that cannot be honoured yet because a swing is
    /// committed or a dodge is still running is buffered for <see cref="BufferWindow"/>. Returns true if a
    /// dodge began now.
    /// </summary>
    public bool TryDodge(Vector3 direction)
    {
        // 39B: a mounted rider does not shoulder-roll. It is the one combat verb riding takes away —
        // melee, block and casting all work from the saddle — because a roll from up there has no
        // reading at all, and 39A's gallop is already the mounted evade. Refused before the stamina
        // check so it costs nothing, and silently: a dodge press is a panic reflex and a toast in
        // that moment is noise.
        if (_mount is { IsMounted: true })
        {
            return false;
        }

        // Dodge can't interrupt a committed swing, and can't restart itself before its recovery ends —
        // but the press is kept, so an early input becomes the evade the instant it is allowed.
        if (_active || (_weapon?.IsCommitted ?? false))
        {
            _dodgeBuffer = BufferWindow;
            _bufferedDirection = direction;
            return false;
        }

        return Begin(direction);
    }

    /// <summary>
    /// The router's attack press, offered here first. Returns true when the dodge consumed it — held in a
    /// buffer until <see cref="Dodge.CanCancelIntoAttack"/> opens — and false when there is no dodge to
    /// respect (the caller attacks normally). A press that arrives inside the cancel window ends the dodge
    /// and starts the weapon's roll-attack (a quick lunging cut, <see cref="CharacterActionComponent.TryRollAttack"/>)
    /// right here, and returns true so the caller does not swing a second time.
    /// </summary>
    public bool InterceptAttack()
    {
        if (!_active)
        {
            return false;
        }

        if (Dodge.CanCancelIntoAttack(_elapsed, Duration(), AttackCancelFraction))
        {
            End(cancelDash: true);
            _weapon?.TryRollAttack();
            return true;
        }

        _attackBuffer = BufferWindow;
        return true;
    }

    private bool Begin(Vector3 direction)
    {
        Vector3 flat = new(direction.X, 0f, direction.Z);
        DodgeKind kind = Dodge.Resolve(flat.LengthSquared(), DirectionDeadzone);
        int chain = Dodge.NextChain(_chain, _sinceLastEnd, ChainWindow);
        float cost = Dodge.ChainedCost(
            kind == DodgeKind.Roll ? StaminaCost : BackstepStaminaCost, chain, ChainSurcharge, MaxChainMultiplier);

        bool grounded = _locomotion?.IsGrounded ?? false;
        float stamina = _stats?.GetCurrent(StatType.Stamina) ?? 0f;
        bool staggered = _combat?.IsStaggered ?? false;
        bool winded = _stats?.IsWinded ?? false;
        if (!Dodge.CanStart(grounded, stamina, cost, _active, staggered, winded))
        {
            return false;
        }

        Basis basis = Entity!.Body.GlobalTransform.Basis;
        _dir = kind == DodgeKind.Roll ? flat.Normalized() : basis.Z; // backstep: away from facing (-Z)
        _kind = kind;
        _chain = chain;
        _elapsed = 0f;
        _active = true;
        _dodgeBuffer = 0d;
        _attackBuffer = 0d;

        // A roll is an escape from anything wound up: a held charge is dropped, not swung.
        _weapon?.CancelCharge();

        _stats?.ModifyCurrent(StatType.Stamina, -cost);
        _locomotion?.StartDash(_dir, SpeedAt(0f), Duration());
        return true;
    }

    public override void _PhysicsProcess(double delta)
    {
        _sinceLastEnd += delta;
        _dodgeBuffer -= delta;
        _attackBuffer -= delta;

        if (!_active)
        {
            if (_dodgeBuffer > 0d && !(_weapon?.IsCommitted ?? false))
            {
                _dodgeBuffer = 0d;
                Begin(_bufferedDirection);
            }

            return;
        }

        _elapsed += (float)delta;

        // A stagger landing mid-roll cancels it — i-frames don't persist into a stagger-lock.
        if (_combat is { IsStaggered: true })
        {
            End(cancelDash: true);
            return;
        }

        // A buffered attack becomes the roll-attack the instant the cancel window opens.
        if (_attackBuffer > 0d && Dodge.CanCancelIntoAttack(_elapsed, Duration(), AttackCancelFraction))
        {
            _attackBuffer = 0d;
            End(cancelDash: true);
            _weapon?.TryRollAttack();
            return;
        }

        float duration = Duration();
        if (Dodge.IsFinished(_elapsed, duration, Kind == DodgeKind.Roll ? RollRecovery : BackstepRecovery))
        {
            End(cancelDash: false);
            return;
        }

        if (_elapsed < duration)
        {
            // Re-arm the burst every tick at the eased speed with the time left: LocomotionComponent's dash
            // is a flat velocity, and this is how the curve reaches it without a second motor.
            _locomotion?.StartDash(_dir, SpeedAt(_elapsed), duration - _elapsed);
        }
        else if (_locomotion is { IsDashing: true })
        {
            // Recovery: the motion is over on this clock, so it is over on locomotion's too.
            _locomotion.CancelDash();
        }

        if (_combat != null)
        {
            (float start, float length) = Kind == DodgeKind.Roll
                ? (IFrameStart, IFrameDuration)
                : (BackstepIFrameStart, BackstepIFrameDuration);
            _combat.IsInvulnerable = Dodge.IsInvulnerable(_elapsed, start, start + length);
        }
    }

    /// <summary>Ends the dodge. Invulnerability always drops; the burst is cancelled on an interrupt
    /// (stagger, attack-cancel, teardown) — a roll that ran its course has already ended its dash on the
    /// same clock. Clearing this component's flag alone once left a staggered owner sliding at roll speed
    /// with no i-frames, which is why both are cleared together.</summary>
    private void End(bool cancelDash)
    {
        if (_combat != null)
        {
            _combat.IsInvulnerable = false;
        }

        if (!_active)
        {
            return;
        }

        _active = false;
        _sinceLastEnd = 0d;
        if (cancelDash || (_locomotion?.IsDashing ?? false))
        {
            _locomotion?.CancelDash();
        }
    }

    private float Duration() => _kind == DodgeKind.Roll ? RollDuration : BackstepDuration;

    private float SpeedAt(float elapsed) => Dodge.SpeedAt(
        elapsed, Duration(), _kind == DodgeKind.Roll ? RollSpeed : BackstepSpeed, EndSpeedFraction, EaseExponent);
}
