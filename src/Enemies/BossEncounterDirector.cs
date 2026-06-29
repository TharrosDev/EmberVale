using Embervale.Core;
using Embervale.Core.Diagnostics;
using Embervale.Core.Events;
using Embervale.Core.Services;
using Embervale.Dialogue;
using Embervale.Items;
using Embervale.Player;
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

    /// <summary>Story flag set on the player when the Iron King dies — persists his defeat across
    /// save/load (the brazier reads it to stay cold). Shared with <see cref="BossSummonComponent"/>.</summary>
    public const string DefeatedFlag = "flag.iron_king_defeated";
    private const string RelicItemId = "item.relic.iron_heart";
    private const string AbsorbDialogueId = "dialogue.iron_king_absorb";
    private const string DefeatMusicCue = "music.boss_defeat";

    private ulong _introUntil;
    private ulong _defeatUntil;
    private bool _locked;
    private bool _slowed;
    private bool _absorbPending;

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

        // Guaranteed reward + persist his defeat now; the "absorb the flame?" choice opens after the beat.
        GrantDefeatRewards();
        _absorbPending = true;
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
            if (_absorbPending)
            {
                _absorbPending = false;
                OpenAbsorbDialogue();
            }
        }
    }

    // --- Defeat reward + corruption beat (Phase 28D) ------------------------

    private void GrantDefeatRewards()
    {
        if (ServiceLocator.Instance is not { } sl || !sl.TryGet(out PlayerCharacter player))
        {
            return;
        }

        StoryFlagsComponent? flags = player.GetComponent<StoryFlagsComponent>();
        if (flags != null && flags.Has(DefeatedFlag))
        {
            return; // already rewarded — never double-grant
        }

        if (player.GetComponent<InventoryComponent>() is { } inventory && ItemDatabase.Get(RelicItemId) is { } relic)
        {
            inventory.AddItem(relic, 1);
            Log.Info($"The Iron King's heart is yours: {relic.DisplayName}.");
        }

        flags?.Set(DefeatedFlag);
        EventBus.Instance?.Publish(new MusicCueRequestedEvent(DefeatMusicCue));
    }

    private static void OpenAbsorbDialogue()
    {
        if (ServiceLocator.Instance is { } sl && sl.TryGet(out PlayerCharacter player)
            && DialogueDatabase.Get(AbsorbDialogueId) is { } dialogue)
        {
            EventBus.Instance?.Publish(new DialogueStartedEvent(player, player, dialogue));
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
