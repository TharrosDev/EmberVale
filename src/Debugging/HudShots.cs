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
using Embervale.Settings;
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
        if (name == "05b4-tracker-folded" && Hud() is { } folded &&
            (folded.TrackerRowsForCapture != TrackerFoldRules.MaxLines || folded.TrackerFoldedForCapture != 2))
            return $"the tracker drew {folded.TrackerRowsForCapture} objective(s) and folded {folded.TrackerFoldedForCapture}, expected 3 and 2";
        if (name == "05a2-hotbar-states")
        {
            if (QuestShotFixtures.FindFirst<HotbarPanel>(GetTree().Root) is not { } bar)
                return "the hotbar panel is missing";
            if (bar.SlotStateForCapture(CoolingSlot) != HotbarSlotState.Cooling)
                return $"slot {CoolingSlot + 1} is {bar.SlotStateForCapture(CoolingSlot)}, expected Cooling";
            if (bar.SlotStateForCapture(LockedSlot) != HotbarSlotState.Locked)
                return $"slot {LockedSlot + 1} is {bar.SlotStateForCapture(LockedSlot)}, expected Locked";
        }
        if (name.StartsWith("13") || name.StartsWith("14"))
        {
            if (Hud() is not { } hud)
                return "GameHud is missing";
            if (HudOptionsFailure(name, hud) is { } failure)
                return failure;
        }
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

        HoldRegeneration();
    }

    /// <summary>
    /// Stops the capture player's pools refilling, for the same reason it ignores damage.
    ///
    /// ⚠️ <b>This is why 02, 03 and 04 failed their own prerequisites.</b> Each drains a pool with
    /// <see cref="StatsComponent.SetCurrent"/> and is checked half a second later. The player
    /// regenerates health at 3 a second, mana at 4 and stamina at 15, and none of those waits for
    /// anything here: the delays that hold regeneration back are reset by taking a hit or spending
    /// stamina, and the harness does neither. So by the capture the "empty" stamina bar had refilled
    /// and health and mana had climbed back over the lines the shots are named for, and the harness
    /// (correctly) refused to call those frames evidence.
    ///
    /// The rates are the stats' own properties, so this is the same authority the drains use. The
    /// tick still runs, which matters: it is what notices stamina at zero and sets the winded state
    /// the fourth shot exists to show.
    /// </summary>
    private static void HoldRegeneration()
    {
        if (Stats() is { } stats)
        {
            stats.HealthRegen = 0f;
            stats.StaminaRegen = 0f;
            stats.ManaRegen = 0f;
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

        // The cell states that are not "ready": one consumable waiting out its cooldown (the wipe, and
        // its seconds once nine or fewer are left) and one the player is too low a level to use (the
        // padlock). Both through the hotbar's own Activate and the item's own level requirement.
        Shot("05a2-hotbar-states", StageHotbarStates);

        // ⚠️ The save this harness loads has no active quest, so without this the tracker — and the
        // distance/bearing readout that is one of 39.5B's headline changes — never appears in a single
        // image. A capture set that silently omits the feature under review is the failure mode this
        // whole tool exists to prevent.
        Shot("05b-quest-tracked", StartAndTrackAQuest);

        // The campaign tracker: a chapter label above the title, the spine in the main-quest colour, the
        // current step, an optional step carrying its "Optional" tag and the next locked step, with the
        // rest folded into a count. Built in memory because no authored quest carries the new fields yet.
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

        // The same five-objective quest, checked for the fold: three lines and "+2 more".
        Shot("05b4-tracker-folded", () =>
        {
            if (QuestShotFixtures.Log() is { } log)
            {
                QuestShotFixtures.StartTrackedMainQuest(log);
            }
        });

        // ⚠️ The compass's destination channel — chevron, distance, and the edge arrow for a mark
        // behind you — is invisible in every other shot, because the one authored quest destination
        // is cross-region and resolves to no position. Without these two the 39.5C compass rebuild
        // would ship with half of it never once rendered, which is exactly the gap 39.5B left.
        Shot("05c-waypoint-ahead", () => SetWaypointRelative(forward: 60f, right: 12f));

        Shot("05d-waypoint-behind", () => SetWaypointRelative(forward: -80f, right: -30f));

        Shot("06-night", () => SetHour(23));

        Shot("07-dawn", () => SetHour(6));

        // The player's HUD options, each driven through the settings the options screen writes.
        // Before the boss shots, because a boss on screen is combat and would hold a Dynamic HUD up.
        //
        // Dynamic at rest: full pools, no blow landed, nothing just changed. Nearly everything steps
        // back. Then the same preset the moment the player is hit.
        Shot("13-dynamic-exploration", () =>
        {
            Stats()?.RefillResources();
            SetHudOptions(HudPreset.Dynamic);
            Hud()?.SettleDynamicForCapture();
        });

        Shot("13b-dynamic-combat", TakeABlow);

        // Minimal, under a second blow: the navigation aids are gone for good and the vitals, hotbar
        // and crosshair are up only because of the fight.
        Shot("13c-minimal-combat", () =>
        {
            SetHudOptions(HudPreset.Minimal);
            TakeABlow();
        });

        Shot("14a-hud-scale-085", () => SetHudOptions(HudPreset.Full, scale: 0.85f));

        Shot("14b-hud-scale-125", () => SetHudOptions(HudPreset.Full, scale: 1.25f));

        Shot("14c-safe-zone-10", () => SetHudOptions(HudPreset.Full, safeZone: 0.1f));

        // Hostile convergence: low resources + statuses + tracked quest + boss priority + queued
        // quest notice. This is the frame that proves the top-centre suppression contract under load.
        // It also puts the HUD options back to their defaults for every shot after it.
        Shot("07b-boss-hostile", () =>
        {
            SetHudOptions(HudPreset.Full);
            StageBossPressure();
        });

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

    private const int CoolingSlot = 0;
    private const int LockedSlot = 4;

    /// <summary>
    /// Uses a real consumable that has a cooldown, and assigns one the player is too low a level for.
    /// Both are found in the item database rather than named, so the shot follows the catalogue: the
    /// longest cooldown the player may use right now, and the lowest level requirement above them.
    /// Health is low from the earlier shots, so a restorative is not refused for being pointless.
    /// </summary>
    private static void StageHotbarStates()
    {
        if (Player() is not { } player ||
            player.GetComponent<InventoryComponent>() is not { } pack ||
            player.GetComponent<HotbarComponent>() is not { } bar)
        {
            return;
        }

        HoldRegeneration();
        int level = player.GetComponent<Progression.ProgressionComponent>()?.Level ?? 1;
        ConsumableItemResource? timed = null;
        ConsumableItemResource? gated = null;
        foreach (ItemResource item in ItemDatabase.All.Values)
        {
            if (item is not ConsumableItemResource consumable)
            {
                continue;
            }

            if (consumable.RequiredLevel > level)
            {
                if (gated == null || consumable.RequiredLevel < gated.RequiredLevel)
                {
                    gated = consumable;
                }
            }
            else if (BetterCooldown(consumable.CooldownSeconds, timed?.CooldownSeconds ?? 0f) &&
                     ConsumableEffectsComponent.Check(player, consumable) == ConsumeRefusal.None)
            {
                timed = consumable;
            }
        }

        if (timed != null)
        {
            pack.AddItem(timed, 3);
            bar.Assign(CoolingSlot, timed.Id);
            bar.Activate(CoolingSlot);
        }

        if (gated != null)
        {
            pack.AddItem(gated, 2);
            bar.Assign(LockedSlot, gated.Id);
        }
    }

    /// <summary>Whether a cooldown makes the better picture: one short enough to print its seconds
    /// (and long enough to outlast the hold before the capture) beats one that only shows the wipe,
    /// and within either kind the longer wins.</summary>
    private static bool BetterCooldown(float candidate, float best)
    {
        static bool Numbered(float seconds) => seconds >= 3f && seconds <= HotbarRules.NumeralSeconds;
        if (candidate <= 0f)
        {
            return false;
        }

        return Numbered(candidate) != Numbered(best) ? Numbered(candidate) : candidate > best;
    }

    /// <summary>
    /// Writes HUD options into the live settings and announces them the way applying the options
    /// screen does. The settings are never saved, and the announcement is the event alone: a full
    /// <see cref="SettingsService.Apply"/> would also re-apply the window mode and UI scale this
    /// harness set for itself.
    /// </summary>
    private static void SetHudOptions(HudPreset preset, float scale = 1f, float safeZone = 0f)
    {
        if (ServiceLocator.Instance is not { } locator || !locator.TryGet(out SettingsService settings))
        {
            return;
        }

        settings.Current.HudElementModes = HudPresets.ToSaved(HudPresets.Modes(preset));
        settings.Current.HudScale = scale;
        settings.Current.HudSafeZone = safeZone;
        EventBus.Instance?.Publish(new SettingsAppliedEvent(settings.Current));
    }

    /// <summary>The player takes a hit: the event a landed blow raises, which is what the HUD's
    /// combat reading listens for, and some health gone so the bars have something to say.</summary>
    private static void TakeABlow()
    {
        if (Player() is not { } player)
        {
            return;
        }

        SetFraction(StatType.Health, 0.6f);
        Vector3 at = player.Body is { } body ? body.GlobalPosition : Vector3.Zero;
        EventBus.Instance?.Publish(new Combat.HitConfirmedEvent(
            null, player, 12f, Combat.DamageType.Physical, Combat.HitOutcome.Hit, Combat.HitKind.Normal,
            false, at, ByPlayer: false, OnPlayer: true));
    }

    /// <summary>What a HUD-options shot failed to reach, or null. Each is read back from the HUD's own
    /// answers (<see cref="GameHud.Shows"/>, <see cref="GameHud.LayoutWidth"/>), not from the settings
    /// just written, so a preset that was saved and never applied does not pass.</summary>
    private string? HudOptionsFailure(string name, GameHud hud)
    {
        switch (name)
        {
            case "13-dynamic-exploration":
                foreach (HudElement element in new[]
                         { HudElement.Vitals, HudElement.Minimap, HudElement.QuestTracker, HudElement.Clock })
                {
                    if (hud.Shows(element))
                        return $"{element} is still showing under the Dynamic preset at rest";
                }
                return null;

            case "13b-dynamic-combat":
                foreach (HudElement element in new[] { HudElement.Vitals, HudElement.Hotbar, HudElement.Crosshair })
                {
                    if (!hud.Shows(element))
                        return $"{element} did not come up under the Dynamic preset in combat";
                }
                return null;

            case "13c-minimal-combat":
                if (!hud.Shows(HudElement.Vitals))
                    return "the vitals did not come up under the Minimal preset in combat";
                foreach (HudElement element in new[]
                         { HudElement.Compass, HudElement.Minimap, HudElement.QuestTracker, HudElement.Clock })
                {
                    if (hud.Shows(element))
                        return $"{element} is showing under the Minimal preset";
                }
                return null;

            case "14a-hud-scale-085":
                return LayoutFailure(hud, 0.85f, 0f);
            case "14b-hud-scale-125":
                return LayoutFailure(hud, 1.25f, 0f);
            case "14c-safe-zone-10":
                return LayoutFailure(hud, 1f, 0.1f);
            default:
                return null;
        }
    }

    private string? LayoutFailure(GameHud hud, float scale, float safeZone)
    {
        float expected = HudMetrics.LayoutWidth(GetViewport().GetVisibleRect().Size.X, scale, safeZone);
        return Mathf.Abs(hud.LayoutWidth - expected) > 2f
            ? $"the HUD lays out {hud.LayoutWidth:0} wide, expected {expected:0} at scale {scale} and safe zone {safeZone}"
            : null;
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
