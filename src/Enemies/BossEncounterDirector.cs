using Embervale.Core;
using Embervale.Core.Diagnostics;
using Embervale.Core.Events;
using Embervale.Core.Services;
using Embervale.Dialogue;
using Embervale.Entities;
using Embervale.Items;
using Embervale.Player;
using Godot;

namespace Embervale.Enemies;

/// <summary>
/// A boss fight's flow beats: an <b>intro lock</b> as it begins and a <b>slow-mo defeat</b> when the
/// boss falls, then its guaranteed reward and the conversation that offers the ember — the hooks the
/// Phase 43 cinematics extend. Pure timing/flow; the healthbar, intro title and defeat banner are the
/// HUD's job (it reacts to the same events).
///
/// <b>Phase 36E:</b> all of it now comes from the dead boss's own <see cref="BossResource"/>, resolved
/// through its <see cref="BossController"/>. Every value here used to be a constant naming the Iron
/// King while the handler fired for <em>any</em> <see cref="BossEntity"/> — and since 36A the dragons
/// are among them. The reward was correctly guarded, but the defeat dialogue was queued regardless,
/// so killing any dragon re-opened his "absorb the flame?" choice and its +25 corruption, once per
/// boss kill, for as long as there were bosses. <see cref="BossDefeat.Resolve"/> is that decision,
/// made in one place and unit-tested.
///
/// Runs <see cref="Node.ProcessModeEnum.Always"/> so it still restores <see cref="Engine.TimeScale"/>
/// even if the player pauses mid-defeat, and times off real wall-clock (<see cref="Time.GetTicksMsec"/>)
/// so the restore is immune to the slow-down it just applied.
/// </summary>
public partial class BossEncounterDirector : Node
{
    /// <summary>Story flag set when the Iron King dies — the brazier reads it to stay cold and
    /// Frostfang Reach unlocks behind it. Authored as his <c>DefeatFlagId</c> in
    /// <c>data/bosses/IronKing.tres</c>; named here because <see cref="BossSummonComponent"/> and the
    /// region gate both ask for it by name.</summary>
    public const string DefeatedFlag = "flag.iron_king_defeated";

    private ulong _introUntil;
    private ulong _defeatUntil;
    private bool _locked;
    private bool _slowed;

    /// <summary>Conversation queued behind the defeat beat, or empty. Set only when the defeat
    /// actually paid out — that gating is the whole of the 36E fix.</summary>
    private string _pendingDialogueId = string.Empty;

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
        // Cinematic lock: it holds the player still to watch the boss's entrance, so the world
        // must keep running underneath it — pausing here would freeze the very thing being watched.
        UiState.Open(this, pausesWorld: false);
        _locked = true;
        _introUntil = Time.GetTicksMsec() + Milliseconds(Fight(e.Boss)?.IntroLockSeconds ?? 2.5f);
    }

    private void OnDied(EntityDiedEvent e)
    {
        if (e.Entity is not BossEntity boss)
        {
            return;
        }

        BossResource? fight = Fight(boss);
        Engine.TimeScale = fight?.DefeatTimeScale ?? 0.35f;
        _slowed = true;
        _defeatUntil = Time.GetTicksMsec() + Milliseconds(fight?.DefeatSlowSeconds ?? 1f);

        // Reward and conversation are decided together, first-time-only. Queuing the dialogue outside
        // that decision is exactly what let one boss's choice re-open on another boss's death.
        _pendingDialogueId = GrantDefeatRewards(fight);
    }

    /// <summary>The dead boss's authored fight, or null for a <see cref="BossEntity"/> with no controller.</summary>
    private static BossResource? Fight(IEntity boss) => boss.GetComponent<BossController>()?.Fight;

    private static ulong Milliseconds(float seconds) => (ulong)Mathf.Max(0f, seconds * 1000f);

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
            if (_pendingDialogueId.Length > 0)
            {
                string id = _pendingDialogueId;
                _pendingDialogueId = string.Empty;
                OpenDefeatDialogue(id);
            }
        }
    }

    // --- Defeat reward + corruption beat ------------------------------------

    /// <summary>Pays out a defeat and returns the conversation to open after the beat (empty for
    /// none). A boss with no authored flag still plays its beat and its music — it simply has nothing
    /// to grant, which is every lair boss, whose defeat its own spawner records instead.</summary>
    private static string GrantDefeatRewards(BossResource? fight)
    {
        if (fight == null || ServiceLocator.Instance is not { } sl || !sl.TryGet(out PlayerCharacter player))
        {
            return string.Empty;
        }

        EventBus.Instance?.Publish(new MusicCueRequestedEvent(fight.DefeatMusicCue));

        StoryFlagsComponent? flags = player.GetComponent<StoryFlagsComponent>();
        bool alreadyDefeated = flags is { } known && known.Has(fight.DefeatFlagId);

        // No flag means nothing to record, and therefore nothing to guard a second grant with — so
        // such a boss grants nothing rather than granting again every time it dies.
        BossDefeat.Outcome outcome = fight.DefeatFlagId.Length == 0
            ? BossDefeat.Outcome.None
            : BossDefeat.Resolve(alreadyDefeated, fight.RewardItemId, fight.DefeatDialogueId);

        if (outcome.GrantReward &&
            player.GetComponent<InventoryComponent>() is { } inventory &&
            ItemDatabase.Get(fight.RewardItemId) is { } relic)
        {
            inventory.AddItem(relic, Mathf.Max(1, fight.RewardQuantity));
            Log.Info($"The victor's due: {relic.DisplayName}.");
        }

        if (outcome.SetFlag)
        {
            flags?.Set(fight.DefeatFlagId);
        }

        return outcome.OpenDialogue ? fight.DefeatDialogueId : string.Empty;
    }

    private static void OpenDefeatDialogue(string dialogueId)
    {
        if (ServiceLocator.Instance is { } sl && sl.TryGet(out PlayerCharacter player)
            && DialogueDatabase.Get(dialogueId) is { } dialogue)
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
