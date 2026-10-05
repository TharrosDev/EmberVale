using System.Collections.Generic;
using Embervale.Core;
using Embervale.Core.Diagnostics;
using Embervale.Core.Events;
using Embervale.Companions;
using Embervale.Core.Services;
using Embervale.Enemies;
using Embervale.Entities;
using Embervale.Items;
using Embervale.Magic;
using Embervale.Player;
using Embervale.Quests;
using Embervale.Localization;
using Embervale.Stats;
using Embervale.UI;
using Embervale.World;
using Godot;

namespace Embervale.Debugging;

/// <summary>
/// The HUD screenshot harness — <c>godot --path . -- --hudshots</c> (39.5B).
///
/// ⚠️ <b>THIS IS THE TOOL THE REPO HAS BEEN MISSING FOR TWO SUB-PHASES.</b> 39.5A shipped three
/// screen-space defects through a fully green battery and named the gap; 39.5B then built a minimap,
/// a HUD slot, an overlay and a visibility system with no way to look at any of them. The reason is
/// structural and neither half of the toolchain closes it: <c>--play</c> boots the world but cannot
/// press a key, and the Godot MCP drives the <b>editor</b>, where the HUD does not exist at all
/// because <see cref="Bootstrap.UICompositionRoot"/> constructs it at runtime.
///
/// So this drives the real HUD, in a real session, through real state, and renders each state to a
/// PNG an agent can actually open. It is <b>not</b> a test: it asserts nothing and gates nothing.
/// Its whole job is to turn "reviewed against the API" into "looked at".
///
/// ⚠️ <b>Every state is driven through the authoritative system, never by poking the HUD.</b> Low
/// health is <see cref="StatsComponent.SetCurrent"/>; a status chip is
/// <see cref="StatusEffectsComponent.Apply"/>; the menu state is <see cref="UiState.Open"/>. A
/// harness that set the widgets directly would photograph itself rather than the HUD, and would go
/// on producing perfect screenshots after the bindings broke.
/// </summary>
public sealed partial class HudShots : ShotHarness
{
    protected override string Flag => "--hudshots";

    protected override string OutputDir => "user://hudshots";

    protected override string? ValidateShotState(string name)
    {
        if (Player() is not { } player)
            return "player is not registered";
        if (player.GetComponent<StatsComponent>() is not { } stats)
            return "player has no StatsComponent";
        if (player.GetComponent<PlayerCameraRig>()?.Camera is not { Current: true })
            return "player has no current gameplay camera";
        if (Hud() is null)
            return "GameHud is missing";
        if (name == "02-health-low" && stats.GetCurrent(StatType.Health) > stats.GetMax(StatType.Health) * 0.2f)
            return "low-health state was not reached";
        if (name == "03-mana-low" && stats.GetCurrent(StatType.Mana) > stats.GetMax(StatType.Mana) * 0.1f)
            return "low-mana state was not reached";
        if (name == "04-endurance-empty" && stats.GetCurrent(StatType.Stamina) > 0.01f)
            return "empty-stamina state was not reached";
        if (name == "05b-quest-tracked" && player.GetComponent<QuestLogComponent>()?.Tracked is null)
            return "no active tracked quest";
        if (name is "05b2-tracker-campaign" or "05b3-tracker-hint")
        {
            if (player.GetComponent<QuestLogComponent>()?.Tracked?.Quest.Id != QuestShotFixtures.AshWind)
                return "the campaign fixture quest is not the tracked quest";
            if (!Loc.Has("chapter.ch.1.title"))
                return "chapter title text did not resolve";
        }
        if (name == "05b3-tracker-hint" && Hud() is { TrackerHintVisible: false })
            return "the objective hint did not appear after the dwell";
        if (name == "07c-boss-epithet" && QuestShotFixtures.FindFirst<BossFrame>(GetTree().Root) is not { Visible: true })
            return "the boss frame is not showing";
        if (name == "10-objective-toast" && QuestShotFixtures.FindFirst<Toast>(GetTree().Root) is null)
            return "no toast is on screen";
        if (name == "11-chapter-banner" &&
            QuestShotFixtures.FindFirst<ChapterBanner>(GetTree().Root) is not { Showing: "ch.1" })
            return "the chapter banner is not showing";
        if (name.StartsWith("05c-") || name.StartsWith("05d-"))
        {
            if (ServiceLocator.Instance is not { } locator || !locator.TryGet(out MapService map) || map.Waypoint is null)
                return "requested waypoint was not set";
        }
        if (name == "08-menu-open" && !UiState.MenuOpen)
            return "menu-open state was not reached";
        if (name == "09-menu-closed" && UiState.MenuOpen)
            return "menu-closed state was not reached";
        return null;
    }

    public override void _Ready()
    {
        base._Ready();

        // The harness authors exact health/resource states below. Ambient encounters must not race
        // those writes between drive and capture or two identical runs produce different evidence.
        // Combat still runs for the world; only the disposable capture player ignores damage.
        if (Player()?.GetComponent<Combat.CombatComponent>() is { } combat)
        {
            combat.IsInvulnerable = true;
        }
    }

    /// <summary>
    /// The states worth looking at, in the order the brief's §69 asks for them.
    ///
    /// Ordered so each builds on the last rather than resetting: the resources drain in sequence, the
    /// statuses land on the drained bars, and the menu shot comes last because it is the only one
    /// that changes what is on screen rather than what the widgets say.
    /// </summary>
    protected override void BuildShotList()
    {
        Shot("01-exploration", () => Stats()?.RefillResources());

        Shot("02-health-low", () => SetFraction(StatType.Health, 0.18f));

        Shot("03-mana-low", () => SetFraction(StatType.Mana, 0.08f));

        Shot("04-endurance-empty", () => SetFraction(StatType.Stamina, 0f));

        Shot("05-statuses", ApplyStatuses);

        // Spacing audit: a filled hotbar and a companion in the party card, so the bottom edge of the HUD is
        // photographed in its busiest honest state rather than with an empty dock and no party strip.
        Shot("05a-party-hotbar", StageHotbarAndParty);

        // ⚠️ The save this harness loads has no active quest, so without this the tracker — and the
        // distance/bearing readout that is one of 39.5B's headline changes — never appears in a single
        // image. A capture set that silently omits the feature under review is the failure mode this
        // whole tool exists to prevent.
        Shot("05b-quest-tracked", StartAndTrackAQuest);

        // The campaign tracker: a chapter label above the title, the spine in the main-quest colour, a done
        // step, the current step and an optional step carrying its "Optional" tag. Built in memory because no
        // authored quest carries the new fields yet.
        Shot("05b2-tracker-campaign", () =>
        {
            if (QuestShotFixtures.Log() is { } log)
            {
                QuestShotFixtures.StartTrackedMainQuest(log);
            }
        });

        // The objective hint, which only appears after the player has sat on one step for a while
        // (TrackerRules.HintDelaySeconds). The harness advances the dwell clock rather than waiting.
        Shot("05b3-tracker-hint", () => Hud()?.AdvanceTrackerDwell(TrackerRules.HintDelaySeconds + 5f));

        // ⚠️ The compass's destination channel — chevron, distance, and the edge arrow for a mark
        // behind you — is invisible in every other shot, because the one authored quest destination
        // is cross-region and resolves to no position. Without these two the 39.5C compass rebuild
        // would ship with half of it never once rendered, which is exactly the gap 39.5B left.
        Shot("05c-waypoint-ahead", () => SetWaypointRelative(forward: 60f, right: 12f));

        Shot("05d-waypoint-behind", () => SetWaypointRelative(forward: -80f, right: -30f));

        Shot("06-night", () => SetHour(23));

        Shot("07-dawn", () => SetHour(6));

        // Hostile convergence: low resources + statuses + tracked quest + boss priority + queued
        // quest notice. This is the frame that proves the top-centre suppression contract under load.
        Shot("07b-boss-hostile", StageBossPressure);

        // A boss with an epithet card and its own intro line, staged through the frame's own entry point.
        Shot("07c-boss-epithet", StageBossEpithet);

        // The visibility rule this sub-phase added — the one shot that proves a HUD is ABSENT.
        Shot("08-menu-open", () => UiState.Open(this));

        Shot("09-menu-closed", () => UiState.Close(this));

        // Last, and in this order, because both are transient: a toast lives a few seconds and the banner
        // holds the lower third for about five, and either would otherwise sit in every later frame.
        // Finishing the current step opens the next one, which the feed folds into ONE toast.
        Shot("10-objective-toast", () => QuestShotFixtures.Log()?.DebugAdvance(QuestShotFixtures.AshWind, 1));

        // Spacing audit: three toasts at once, the stack's worst case against the tracker and the minimap.
        Shot("10b-toast-stack", StageToastStack);

        Shot("11-chapter-banner", () =>
        {
            QuestShotFixtures.HoldChapterBanners(false);
            QuestShotFixtures.FindFirst<ChapterBanner>(GetTree().Root)?.Request("ch.1");
        });
    }

    // --- State drivers, all through the owning system ------------------------

    private static IEntity? Player() =>
        ServiceLocator.Instance is { } locator && locator.TryGet(out PlayerCharacter player) ? player : null;

    private static StatsComponent? Stats() => Player()?.GetComponent<StatsComponent>();

    private GameHud? Hud() => QuestShotFixtures.FindFirst<GameHud>(GetTree().Root);

    private static void SetFraction(StatType type, float fraction)
    {
        if (Stats() is { } stats)
        {
            stats.SetCurrent(type, stats.GetMax(type) * fraction);
        }
    }

    /// <summary>Applies whatever status effects the game actually has, up to three — the row's
    /// crowding is the thing being looked at, and inventing effects to fill it would photograph a
    /// HUD this game cannot produce (§73).</summary>
    private static void ApplyStatuses()
    {
        if (Player() is not { } player ||
            player.GetComponent<StatusEffectsComponent>() is not { } effects)
        {
            return;
        }

        int applied = 0;
        foreach (StatusEffectResource definition in StatusEffectDatabase.All())
        {
            effects.Apply(definition, player);
            if (++applied >= 3)
            {
                // Some real definitions tick damage or healing. Freeze their timers after the UI
                // has received the authentic applications so later named resource states are not
                // silently rewritten by the screenshot fixture itself.
                effects.ProcessMode = ProcessModeEnum.Disabled;
                return;
            }
        }
    }

    /// <summary>Puts a few real consumables on the hotbar (with counts) and recruits a companion.</summary>
    private static void StageHotbarAndParty()
    {
        if (Player() is not { } player)
        {
            return;
        }

        if (player.GetComponent<InventoryComponent>() is { } pack && player.GetComponent<HotbarComponent>() is { } bar)
        {
            string[] ids = { "item.potion.health", "item.food.field_ration", "item.potion.stamina", "item.food.smoked_fish" };
            for (int i = 0; i < ids.Length; i++)
            {
                if (ItemDatabase.Get(ids[i]) is { } item)
                {
                    pack.AddItem(item, 3 + i);
                    bar.Assign(i, ids[i]);
                }
            }
        }

        if (ServiceLocator.Instance is { } locator && locator.TryGet(out CompanionRoster roster))
        {
            roster.Recruit("companion.kael");
        }
    }

    /// <summary>Raises three notices in one frame through the events the feed already answers.</summary>
    private void StageToastStack()
    {
        if (Player() is not { } player)
        {
            return;
        }

        EventBus.Instance?.Publish(new Progression.LeveledUpEvent(player, 7, 1));
        EventBus.Instance?.Publish(new GameSavedEvent("shots", true));
        QuestShotFixtures.FindFirst<Notifications>(GetTree().Root)?.PushBark("companion.kael", "shot.boss.intro");
    }

    /// <summary>Starts the first quest the player can actually take and tracks it, so the tracker,
    /// its objective rows and the distance/bearing readout are all on screen to be looked at.</summary>
    private static void StartAndTrackAQuest()
    {
        if (Player()?.GetComponent<QuestLogComponent>() is not { } log)
        {
            return;
        }

        foreach (QuestResource quest in QuestDatabase.All)
        {
            if (log.StartQuest(quest))
            {
                log.Track(quest.Id);
                return;
            }
        }
    }

    /// <summary>Drops the player's waypoint relative to where they are facing, so a shot can put a
    /// destination in front of them or deliberately behind them.</summary>
    private static void SetWaypointRelative(float forward, float right)
    {
        if (ServiceLocator.Instance is not { } locator ||
            !locator.TryGet(out PlayerCharacter player) ||
            !locator.TryGet(out MapService map))
        {
            return;
        }

        Vector3 ahead = -player.GlobalBasis.Z;
        Vector3 side = player.GlobalBasis.X;
        map.SetWaypoint(player.GlobalPosition + (ahead * forward) + (side * right));
    }

    private static void SetHour(int hour)
    {
        if (ServiceLocator.Instance is { } locator && locator.TryGet(out WorldClock clock))
        {
            clock.SetTimeOfDay(hour);
        }
    }

    private void StageBossEpithet()
    {
        if (Player() is not { } player || QuestShotFixtures.FindFirst<BossFrame>(GetTree().Root) is not { } frame)
        {
            return;
        }

        QuestShotFixtures.RegisterText();
        frame.Present(player, "THE BLACK-IRON KING", 4, QuestShotFixtures.BossEpithetKey, QuestShotFixtures.BossIntroKey);
        EventBus.Instance?.Publish(new BossPhaseChangedEvent(player, 2, 4));
    }

    private static void StageBossPressure()
    {
        if (Player() is not { } player)
        {
            return;
        }

        SetFraction(StatType.Health, 0.12f);
        EventBus.Instance?.Publish(new BossEncounterStartedEvent(player, "THE ASHEN REGENT", 4));
        EventBus.Instance?.Publish(new BossPhaseChangedEvent(player, 2, 4));
    }
}
