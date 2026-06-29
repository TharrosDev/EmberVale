using Embervale.Core;
using Embervale.Core.Events;
using Godot;

namespace Embervale.Enemies;

/// <summary>
/// The boss fight's flow beats (Phase 28C): a brief <b>intro lock</b> when the boss is summoned and a
/// <b>slow-mo defeat</b> when he dies — the hooks the Phase 43 cinematics extend. Pure timing/flow; the
/// healthbar, intro title and defeat banner are the HUD's job (it reacts to the same events).
///
/// Runs <see cref="Node.ProcessModeEnum.Always"/> so it still restores <see cref="Engine.TimeScale"/>
/// even if the player pauses mid-defeat, and times off real wall-clock (<see cref="Time.GetTicksMsec"/>)
/// so the restore is immune to the slow-down it just applied.
/// </summary>
public partial class BossEncounterDirector : Node
{
    private const ulong IntroLockMs = 2500;
    private const ulong DefeatSlowMs = 1000;
    private const float DefeatTimeScale = 0.35f;

    private ulong _introUntil;
    private ulong _defeatUntil;
    private bool _locked;
    private bool _slowed;

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        EventBus.Instance?.Subscribe<BossEncounterStartedEvent>(OnStarted);
        EventBus.Instance?.Subscribe<EntityDiedEvent>(OnDied);
    }

    public override void _ExitTree()
    {
        EventBus.Instance?.Unsubscribe<BossEncounterStartedEvent>(OnStarted);
        EventBus.Instance?.Unsubscribe<EntityDiedEvent>(OnDied);
        RestoreTime();
        ReleaseLock();
    }

    private void OnStarted(BossEncounterStartedEvent e)
    {
        UiState.Open(this);
        _locked = true;
        _introUntil = Time.GetTicksMsec() + IntroLockMs;
    }

    private void OnDied(EntityDiedEvent e)
    {
        if (e.Entity is not BossEntity)
        {
            return;
        }

        Engine.TimeScale = DefeatTimeScale;
        _slowed = true;
        _defeatUntil = Time.GetTicksMsec() + DefeatSlowMs;
    }

    public override void _Process(double delta)
    {
        ulong now = Time.GetTicksMsec();

        if (_locked && now >= _introUntil)
        {
            ReleaseLock();
        }

        if (_slowed && now >= _defeatUntil)
        {
            RestoreTime();
        }
    }

    private void ReleaseLock()
    {
        if (_locked)
        {
            UiState.Close(this);
            _locked = false;
        }
    }

    private void RestoreTime()
    {
        if (_slowed)
        {
            Engine.TimeScale = 1f;
            _slowed = false;
        }
    }
}
