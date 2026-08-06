using Embervale.Core;
using Embervale.Core.Events;
using Godot;

namespace Embervale.Combat;

/// <summary>
/// Hit-stop (Phase 29A): a brief global freeze-frame when a hit lands, scaled by the hit's weight
/// (<see cref="HitStop.DurationMs"/>), so blows read with impact. Dips <see cref="Engine.TimeScale"/>
/// to <see cref="FreezeTimeScale"/> for the computed window, then restores — timed off real wall-clock
/// (<see cref="Time.GetTicksMsec"/>) and run <see cref="Node.ProcessModeEnum.Always"/> so the restore is
/// immune to the freeze it just applied. Suppressed during pause/menus/cutscene, and yields to any other
/// time effect (the boss defeat slow-mo) rather than fighting it.
/// </summary>
public partial class HitStopDirector : Node
{
    /// <summary>Time scale during the freeze — 0 is a true freeze-frame; raise toward 1 for a softer
    /// slow. A tuning knob.</summary>
    public const float FreezeTimeScale = 0.0f;

    private bool _active;
    private ulong _until;

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        EventBus.Instance?.Subscribe<DamageDealtEvent>(OnDamage);
        EventBus.Instance?.Subscribe<EntityStaggeredEvent>(OnStaggered);
    }

    public override void _ExitTree()
    {
        EventBus.Instance?.Unsubscribe<DamageDealtEvent>(OnDamage);
        EventBus.Instance?.Unsubscribe<EntityStaggeredEvent>(OnStaggered);
        Restore();
    }

    private void OnDamage(DamageDealtEvent e) =>
        Engage(HitStop.DurationMs(e.Amount, e.IsCrit, e.IsBlocked, staggered: false));

    private void OnStaggered(EntityStaggeredEvent e) =>
        Engage(HitStop.DurationMs(0f, isCrit: false, isBlocked: false, staggered: true));

    private void Engage(int durationMs)
    {
        if (durationMs <= 0)
        {
            return;
        }

        // Off during pause / menus / cutscene (the boss intro lock raises UiState too).
        if (GameManager.Instance is not { IsPlaying: true } || UiState.MenuOpen)
        {
            return;
        }

        // Don't hijack another time effect (e.g. the boss defeat slow-mo); they never overlap live
        // combat, but this keeps the two from fighting over Engine.TimeScale.
        if (!_active && Engine.TimeScale < 0.99f)
        {
            return;
        }

        ulong until = Time.GetTicksMsec() + (ulong)durationMs;
        if (!_active || until > _until)
        {
            _until = until; // a stronger/later hit extends the freeze
        }

        Engine.TimeScale = FreezeTimeScale;
        _active = true;
    }

    public override void _Process(double delta)
    {
        if (!_active)
        {
            return;
        }

        // Bail the freeze if we left play (pause/menu/load) so it never bleeds into a cutscene.
        if (GameManager.Instance is not { IsPlaying: true } || Time.GetTicksMsec() >= _until)
        {
            Restore();
        }
    }

    private void Restore()
    {
        if (!_active)
        {
            return;
        }

        _active = false;

        // Only hand the clock back if it is still the one we took. Engage() yields to another time
        // effect, but that check only covered *acquiring* it — an effect that starts mid-freeze was
        // still cancelled on release. The boss defeat slow-mo does exactly that: it runs off
        // EntityDiedEvent, which a damage-over-time tick can land inside a freeze this director is
        // holding, and the unconditional 1f then wiped the death beat back to full speed.
        if (Mathf.IsEqualApprox((float)Engine.TimeScale, FreezeTimeScale))
        {
            Engine.TimeScale = 1f;
        }
    }
}
