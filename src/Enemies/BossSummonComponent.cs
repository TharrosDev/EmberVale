using Embervale.Core;
using Embervale.Core.Diagnostics;
using Embervale.Core.Events;
using Embervale.Core.Services;
using Embervale.Dialogue;
using Embervale.Entities;
using Embervale.Interaction;
using Embervale.Localization;
using Embervale.Player;
using Embervale.Quests;
using Godot;

namespace Embervale.Enemies;

/// <summary>
/// The arena entry trigger (Phase 28A): an interactable "challenge" object (a brazier) that, on the
/// player's <c>E</c>, summons the Iron King <b>once</b> and registers it as the active
/// <see cref="BossEntity"/> in the <see cref="ServiceLocator"/> (the hook the 28C healthbar and 28D
/// corruption loop resolve). Mirrors <see cref="World.RegionTransitionComponent"/>: a trigger that only
/// kicks off intent. While a boss is alive it stays inert; once he dies it re-arms (the cell can be
/// re-challenged until 28D persists his defeat). This node is the seed for the Phase 36 BossController —
/// the intro lock (28C) and phase logic (28B) graft on here.
///
/// <b>Gated (Phase 33D):</b> the arena is reachable from the first minute of the slice, so without a
/// gate a level-1 player can light this and be flattened by the Iron King before the story has said
/// anything. <see cref="RequiredQuestId"/> holds the brazier cold until the player has earned the
/// fight, and — just as importantly — the prompt says <em>why</em>, because an inert brazier that
/// gives no reason reads as a bug rather than a gate.
/// </summary>
[GlobalClass]
public partial class BossSummonComponent : InteractableComponent
{
    /// <summary>Where the boss appears, relative to this brazier (world axes) — out in the arena.</summary>
    [Export] public Vector3 SpawnOffset { get; set; } = new(0f, 0f, -12f);

    /// <summary>
    /// Quest that must be <em>completed</em> before this challenge can be issued. Empty means
    /// ungated. Exported rather than hardcoded so the Phase 34+ bosses reuse the same gate with
    /// their own prerequisite, and so the slice's pacing stays a content decision.
    /// </summary>
    [Export] public string RequiredQuestId { get; set; } = "quest.warband.heart";

    private BossEntity? _boss;

    public override string Prompt =>
        AlreadyDefeated() ? string.Empty
        : GateMet() ? Loc.T("boss.challenge_prompt")
        : Loc.T("boss.challenge_locked");

    public override void Interact(IEntity instigator)
    {
        if (AlreadyDefeated())
        {
            return; // his defeat persists — the brazier is cold
        }

        if (_boss != null && IsInstanceValid(_boss))
        {
            return; // already fighting him
        }

        if (!GateMet())
        {
            return; // not yet earned — the prompt has already told the player why
        }

        if (Entity?.Body is not { } brazier || brazier.GetParent() is not Node arena)
        {
            Log.Warn("BossSummonComponent: no arena parent to spawn the Iron King into.");
            return;
        }

        // Through the registry (36B): he is an authored archetype now, not a bespoke factory. The
        // pattern-match is the guard — if data/enemies/IronKing.tres ever loses IsBoss, the builder
        // returns a plain EnemyEntity, and registering that as the ServiceLocator's BossEntity would
        // break the healthbar and the defeat loop in ways that look like anything but their cause.
        if (EnemyTemplateRegistry.Create(GameIds.Enemies.IronKing, Vector3.Zero) is not BossEntity boss)
        {
            Log.Error($"'{GameIds.Enemies.IronKing}' did not build a BossEntity; the brazier stays cold.");
            return;
        }

        arena.AddChild(boss);
        boss.GlobalPosition = brazier.GlobalPosition + SpawnOffset;

        _boss = boss;
        ServiceLocator.Instance?.Register(boss);
        boss.TreeExited += OnBossGone;
        EventBus.Instance?.Publish(new BossEncounterStartedEvent(boss, "boss.name"));
        Log.Info("The Iron King rises to meet your challenge.");
    }

    private void OnBossGone()
    {
        _boss = null;
        ServiceLocator.Instance?.Unregister<BossEntity>();
    }

    /// <summary>Whether the prerequisite quest has been completed (or there is no prerequisite).
    /// A missing player/quest log fails closed: better a brazier that won't light than a boss fight
    /// summoned into a half-built world.</summary>
    private bool GateMet()
    {
        if (string.IsNullOrEmpty(RequiredQuestId))
        {
            return true;
        }

        return ServiceLocator.Instance is { } sl
            && sl.TryGet(out PlayerCharacter player)
            && player.GetComponent<QuestLogComponent>() is { } log
            && log.IsCompleted(RequiredQuestId);
    }

    private static bool AlreadyDefeated() =>
        ServiceLocator.Instance is { } sl
        && sl.TryGet(out PlayerCharacter player)
        && player.GetComponent<StoryFlagsComponent>() is { } flags
        && flags.Has(BossEncounterDirector.DefeatedFlag);
}
