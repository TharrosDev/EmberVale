using Embervale.Entities;
using Embervale.Movement;
using Embervale.Stats;
using Godot;

namespace Embervale.Combat;

/// <summary>
/// Dodge-roll with invulnerability frames (Phase 29E). On <see cref="TryDodge"/> it spends stamina, drives
/// a burst roll through the <see cref="LocomotionComponent"/>, and opens an i-frame window during which the
/// owner's <see cref="CombatComponent"/> ignores all damage. Every timing/cost is an export knob.
/// </summary>
[GlobalClass]
public partial class DodgeComponent : EntityComponent
{
    [Export] public float StaminaCost { get; set; } = 22f;
    [Export] public float RollSpeed { get; set; } = 9f;
    [Export] public float RollDuration { get; set; } = 0.45f;

    /// <summary>I-frames open this long after the roll starts…</summary>
    [Export] public float IFrameStart { get; set; } = 0.05f;

    /// <summary>…and last this long (the brief startup keeps the roll from being a free panic button).</summary>
    [Export] public float IFrameDuration { get; set; } = 0.30f;

    private LocomotionComponent? _locomotion;
    private StatsComponent? _stats;
    private CombatComponent? _combat;
    private MountComponent? _mount;
    private bool _rolling;
    private float _elapsed;

    protected override void OnInitialize()
    {
        _locomotion = Entity!.GetComponent<LocomotionComponent>();
        _stats = Entity!.GetComponent<StatsComponent>();
        _combat = Entity!.GetComponent<CombatComponent>();
        _mount = Entity!.GetComponent<MountComponent>();
    }

    protected override void OnTeardown()
    {
        // Never leave the owner stranded invulnerable if the component is torn down mid-roll.
        if (_combat != null)
        {
            _combat.IsInvulnerable = false;
        }
    }

    /// <summary>Starts a roll in <paramref name="direction"/> (world-space; falls back to the body's
    /// facing if zero). Returns true if it began.</summary>
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

        bool grounded = _locomotion?.IsGrounded ?? false;
        float stamina = _stats?.GetCurrent(StatType.Stamina) ?? 0f;
        bool staggered = _combat?.IsStaggered ?? false;
        if (!Dodge.CanStart(grounded, stamina, StaminaCost, _rolling, staggered))
        {
            return false;
        }

        Vector3 dir = direction.LengthSquared() > 0.001f
            ? direction.Normalized()
            : -Entity!.Body.GlobalTransform.Basis.Z; // forward

        _stats?.ModifyCurrent(StatType.Stamina, -StaminaCost);
        _locomotion?.StartDash(dir, RollSpeed, RollDuration);
        _rolling = true;
        _elapsed = 0f;
        return true;
    }

    public override void _Process(double delta)
    {
        if (!_rolling)
        {
            return;
        }

        _elapsed += (float)delta;

        // A stagger landing mid-roll cancels it — i-frames don't persist into a stagger-lock.
        if (_elapsed >= RollDuration || _combat is { IsStaggered: true })
        {
            _rolling = false;
            if (_combat != null)
            {
                _combat.IsInvulnerable = false;
            }

            return;
        }

        if (_combat != null)
        {
            _combat.IsInvulnerable = Dodge.IsInvulnerable(_elapsed, IFrameStart, IFrameStart + IFrameDuration);
        }
    }
}
