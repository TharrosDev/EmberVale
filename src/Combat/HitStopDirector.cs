using Embervale.Combat.Actions;
using Embervale.Core;
using Embervale.Core.Events;
using Godot;

namespace Embervale.Combat;

/// <summary>
/// Hit-stop: a brief global freeze-frame when a blow lands on or from the player, scaled by the blow's
/// weight (<see cref="HitStop.Plan"/>: outcome, damage, kind) and by the player's comfort setting
/// (<see cref="CombatComfort.HitStop"/>, capped by Reduced Motion). Dips <see cref="Engine.TimeScale"/>
/// for the computed window, then restores — timed off real wall-clock (<see cref="Time.GetTicksMsec"/>)
/// and run <see cref="Node.ProcessModeEnum.Always"/> so the restore is immune to the freeze it just
/// applied.
///
/// <para><b>It cannot stick the clock.</b> Every stop is clamped to <see cref="HitStop.MaxMs"/> from the
/// moment it began and cannot be extended past it; a budget (<see cref="HitStopLimiter"/>) stops a
/// flurry from stacking; leaving play (a pause, a menu, a load) restores at once; the director's own
/// exit restores; and it only hands the clock back if it is still the value it set, so another time
/// effect (the boss defeat slow-mo) is never overwritten. It yields to such an effect instead of
/// fighting it. Only blows the player deals or takes count — two NPCs trading blows never freeze the
/// world.</para>
/// </summary>
public partial class HitStopDirector : Node
{
    /// <summary>Time scale during a hard freeze — 0 is a true freeze-frame. A tuning knob.</summary>
    public const float FreezeTimeScale = 0.0f;

    /// <summary>
    /// The scale the clock runs at when no time effect holds it, and so the value a freeze (or the
    /// boss defeat beat) hands back when it ends. 1 in play. The <c>timescale</c> dev command sets
    /// it together with <see cref="Engine.TimeScale"/>: without it the first landed hit put a
    /// sped-up script back to 1x for the rest of its run.
    /// </summary>
    public static float BaseTimeScale { get; set; } = 1f;

    private readonly HitStopLimiter _limiter = new();
    private bool _active;
    private ulong _started;
    private ulong _until;
    private float _appliedScale;

    /// <summary>Whether a freeze is being held right now.</summary>
    public bool IsFreezing => _active;

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        EventBus.Instance?.Subscribe<HitConfirmedEvent>(OnHit);
    }

    public override void _ExitTree()
    {
        EventBus.Instance?.Unsubscribe<HitConfirmedEvent>(OnHit);
        Restore();
    }

    private void OnHit(HitConfirmedEvent e)
    {
        if (!e.ByPlayer && !e.OnPlayer)
        {
            return;
        }

        float actionScale = e.Source?.GetComponent<CharacterActionComponent>()?.Current?.HitStopScale ?? 1f;

        // A spell's own ImpactWeight scales the stop (0.5, the default, is the weight spells had).
        if (e.Kind == HitKind.Spell && e.Weight > 0f)
        {
            actionScale *= Magic.SpellRules.HitStopScale(e.Weight);
        }

        Engage(HitStop.Plan(e.Outcome, e.Kind, e.Amount, e.Staggered, actionScale));
    }

    /// <summary>Applies a stop, after the comfort scale and the budget. Public so a caller with its own
    /// notion of weight (and the probe) can drive it; the rules for pause, menus and other time
    /// effects apply the same way.</summary>
    public void Engage(HitStopPlan plan) => Engage(plan, LiveComfort.Get().HitStop);

    /// <summary>Applies a stop of <paramref name="milliseconds"/> at <paramref name="timeScale"/>, with
    /// the live comfort scale unless <paramref name="comfort"/> is given (0..1). A plain-argument door
    /// for callers that cannot build a <see cref="HitStopPlan"/> — the headless probe is one.</summary>
    public void EngageFor(int milliseconds, float timeScale, float comfort = -1f) =>
        Engage(new HitStopPlan(milliseconds, timeScale), comfort < 0f ? LiveComfort.Get().HitStop : comfort);

    /// <summary>As <see cref="Engage(HitStopPlan)"/> with an explicit comfort scale.</summary>
    public void Engage(HitStopPlan plan, float comfort)
    {
        plan = HitStop.Scale(plan, comfort);
        if (plan.IsNone)
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
        if (!_active && Engine.TimeScale < BaseTimeScale - 0.01f)
        {
            return;
        }

        ulong now = Time.GetTicksMsec();
        int granted = _limiter.Admit(now, plan.Milliseconds);
        if (granted <= 0)
        {
            return;
        }

        if (!_active)
        {
            _started = now;
        }

        // A stronger/later hit extends the freeze, but never past MaxMs from when it began.
        ulong until = System.Math.Min(now + (ulong)granted, _started + (ulong)HitStop.MaxMs);
        if (!_active || until > _until)
        {
            _until = until;
        }

        // The deepest dip in play wins while stops overlap.
        _appliedScale = _active ? Mathf.Min(_appliedScale, plan.TimeScale) : plan.TimeScale;
        Engine.TimeScale = _appliedScale;
        _active = true;
    }

    public override void _Process(double delta)
    {
        if (!_active)
        {
            return;
        }

        // Bail the freeze if we left play (pause/menu/load) so it never bleeds into a cutscene, and
        // never let it outlive its own deadline (the clock is wall time, so this fires at TimeScale 0).
        if (GameManager.Instance is not { IsPlaying: true } || UiState.MenuOpen ||
            Time.GetTicksMsec() >= _until)
        {
            Restore();
        }
    }

    /// <summary>Ends a freeze now and hands the clock back.</summary>
    public void Restore()
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
        if (Mathf.IsEqualApprox((float)Engine.TimeScale, _appliedScale))
        {
            Engine.TimeScale = BaseTimeScale;
        }
    }
}
