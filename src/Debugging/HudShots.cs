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
        if (name.StartsWith("01") && name != "01-exploration" && WheelFailure(name) is { } wheelFailure)
            return wheelFailure;
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
        if (name == "10b-toast-stack" && QuestShotFixtures.FindFirst<Notifications>(GetTree().Root) is { } feed)
        {
            // Three where three fit. A short screen (853x533 logical) has room for fewer between the
            // tracker and the minimap, and the feed is right to hold the rest back there.
            int shown = feed.LiveToastsForCapture;
            if (shown == 0 || shown + feed.QueuedForCapture < 3)
                return $"{shown} toast(s) on screen and {feed.QueuedForCapture} waiting, expected three notices";
            if (GetViewport().GetVisibleRect().Size.Y >= 700f && shown < 3)
                return $"{shown} toast(s) on screen, expected 3";
        }
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

        // The spell row and the wheel, on a caster that knows every learnable spell. First, here,
        // while the pools are full and no status is on the player: a silence would unlight the row.
        // Closed: the glyph on its disc, the hold badge and the ghost of the previous spell.
        Shot("01a-spell-row", WheelShotFixtures.TeachEverything);

        // Open on a favourite, through the wheel's own capture hooks (no gate, no sound, no fade).
        Shot("01b-wheel-favourite", () => Wheel()?.OpenForCapture(WheelShotFixtures.FavouritePoint(0)));

        // A school fanned out, the cursor on one of its spells: straight up is Fire.
        Shot("01c-wheel-school-fan", () => Wheel()?.HoverForCapture(new Vector2(0f, -1.2f)));

        // A favourite part-way through its cooldown, under the cursor so the readout names it.
        Shot("01d-wheel-cooling", () =>
        {
            _coolingSlot = WheelShotFixtures.Cool();
            Wheel()?.HoverForCapture(WheelShotFixtures.FavouritePoint(Mathf.Max(0, _coolingSlot)));
        });

        // No mana: every spell that costs any is dimmed and shows its cost, one under the cursor.
        Shot("01e-wheel-unaffordable", () =>
        {
            _unaffordableSlot = WheelShotFixtures.Unaffordable(_coolingSlot);
            Wheel()?.HoverForCapture(WheelShotFixtures.FavouritePoint(Mathf.Max(0, _unaffordableSlot)));
        });

        // Shut, and the save's own spells and full pools back for every shot after it.
        Shot("01f-wheel-closed", () =>
        {
            Wheel()?.CloseForCapture();
            WheelShotFixtures.Restore();
            Stats()?.RefillResources();
        });

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
        Shot("05b-quest-tracked", () =>
        {
            RestoreLevel(); // the hotbar-states shot may have lowered it to have something to lock
            StartAndTrackAQuest();
        });

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
        // It also puts the HUD options back to what the settings held for every shot after it.
        Shot("07b-boss-hostile", () =>
        {
            RestoreHudOptions();
            StageBossPressure();
        });

        // A boss with an epithet card and its own intro line, staged through the frame's own entry point.
        Shot("07c-boss-epithet", StageBossEpithet);

        // The visibility rule this sub-phase added — the one shot that proves a HUD is ABSENT.
        // The boss's captioned line goes first: a caption is held, not spent, while a menu is up, so
        // it would come back when the menu closes and sit in every shot from 09 to 10b.
        Shot("08-menu-open", () =>
        {
            QuestShotFixtures.FindFirst<SubtitleLayer>(GetTree().Root)?.Dismiss();
            UiState.Open(this);
        });

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

    private SpellWheel? Wheel() => QuestShotFixtures.FindFirst<SpellWheel>(GetTree().Root);

    // The favourite slots the cooling and unaffordable shots put under the cursor.
    private int _coolingSlot = -1;
    private int _unaffordableSlot = -1;

    /// <summary>What a spell-row or wheel shot failed to reach, or null. Read back from the HUD row,
    /// the wheel and the caster, not from what the shot asked for.</summary>
    private string? WheelFailure(string name)
    {
        if (WheelShotFixtures.Caster() is not { } caster)
            return "player has no SpellcastingComponent";
        if (name == "01f-wheel-closed")
            return Wheel() is { IsOpen: true } ? "the wheel is still open" : null;
        if (caster.SpellCount < WheelShotFixtures.LearnableCount())
            return $"the caster knows {caster.SpellCount} spell(s), expected all {WheelShotFixtures.LearnableCount()}";
        if (name == "01a-spell-row")
        {
            if (Hud() is not { } hud)
                return "GameHud is missing";
            if (caster.Selected is not { } prepared || hud.SpellRowGlyphForCapture != prepared.Id)
                return "the spell row does not show the prepared spell's glyph";
            if (!hud.SpellWheelHintForCapture)
                return "the spell row's hold badge is not showing";
            if (caster.PreviousSpellId.Length == 0 || hud.SpellGhostForCapture != caster.PreviousSpellId)
                return "the spell row does not ghost the previous spell";
            return null;
        }

        if (Wheel() is not { IsOpen: true } wheel)
            return "the wheel is not open";
        SpellWheelPick pick = wheel.Hovered;
        switch (name)
        {
            case "01b-wheel-favourite":
                return pick.Kind == SpellWheelPickKind.Favourite && pick.SpellId.Length > 0
                    ? null : $"the cursor is on {pick.Kind}, expected a pinned favourite";
            case "01c-wheel-school-fan":
                return pick.Kind == SpellWheelPickKind.Spell ? null : $"the cursor is on {pick.Kind}, expected a spell in a school's fan";
            case "01d-wheel-cooling":
                return _coolingSlot < 0 ? "no favourite has a cooldown"
                    : pick.Kind != SpellWheelPickKind.Favourite || pick.Index != _coolingSlot ? "the cursor is not on the cooling favourite"
                    : SpellDatabase.Get(pick.SpellId) is { } cooling && caster.CooldownOf(cooling) > 0f ? null
                    : "the favourite under the cursor is not cooling down";
            case "01e-wheel-unaffordable":
                return _unaffordableSlot < 0 ? "no favourite costs mana"
                    : pick.Kind != SpellWheelPickKind.Favourite || pick.Index != _unaffordableSlot ? "the cursor is not on the unaffordable favourite"
                    : SpellDatabase.Get(pick.SpellId) is { } dear && Stats() is { } stats &&
                      stats.GetCurrent(StatType.Mana) < caster.EffectiveManaCost(dear) ? null
                    : "the favourite under the cursor can be afforded";
            default:
                return null;
        }
    }

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
        int level = LevelBelowAGate(player);
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

    // The capture player's progression as the save had it, while a shot holds its level down.
    private static Godot.Collections.Dictionary? _heldProgression;

    /// <summary>
    /// The capture player's level, lowered if it has to be for some consumable to be out of reach.
    ///
    /// ⚠️ <b>This is why '05a2-hotbar-states' failed with "slot 5 is Empty, expected Locked".</b> The
    /// shot looks for the lowest level requirement ABOVE the player, and the save the harness loads
    /// is level 50: above every consumable in the catalogue. Nothing was gated, so nothing was
    /// assigned and the cell stayed empty. The hotbar was right.
    ///
    /// The level goes down through <see cref="Progression.ProgressionComponent.Load"/>, the
    /// component's own restore path and the only writer of a level that is not earned XP, to one
    /// below the catalogue's highest requirement; <see cref="RestoreLevel"/> puts back what the save
    /// held before the next shot. A save already below a gate is left alone.
    /// </summary>
    private static int LevelBelowAGate(IEntity player)
    {
        if (player.GetComponent<Progression.ProgressionComponent>() is not { } progression)
        {
            return 1;
        }

        int highest = 0;
        foreach (ItemResource item in ItemDatabase.All.Values)
        {
            if (item is ConsumableItemResource consumable)
            {
                highest = Mathf.Max(highest, consumable.RequiredLevel);
            }
        }

        if (highest < 2 || progression.Level < highest)
        {
            return progression.Level; // already below a gate, or the catalogue has none to be below
        }

        _heldProgression ??= progression.Save();
        Godot.Collections.Dictionary lowered = progression.Save();
        lowered["level"] = highest - 1;
        lowered["xp"] = 0;
        progression.Load(lowered);
        return progression.Level;
    }

    /// <summary>Puts the capture player's progression back to what the save held.</summary>
    private static void RestoreLevel()
    {
        if (_heldProgression != null && Player()?.GetComponent<Progression.ProgressionComponent>() is { } progression)
        {
            progression.Load(_heldProgression);
        }

        _heldProgression = null;
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

    // The HUD options the settings held before the first options shot overwrote them.
    private bool _hudOptionsHeld;
    private int[] _heldModes = System.Array.Empty<int>();
    private float _heldScale = 1f;
    private float _heldSafeZone;

    /// <summary>
    /// Writes HUD options into the live settings and announces them the way applying the options
    /// screen does. The harness never saves them, and the announcement is the event alone: a full
    /// <see cref="SettingsService.Apply"/> would also re-apply the window mode and UI scale this
    /// harness set for itself. What was there is kept for <see cref="RestoreHudOptions"/>, because
    /// the settings object is the live one: the shots after these have to be taken with the options
    /// the shots before them had, and anything else that saves it must not write these values out.
    /// </summary>
    private void SetHudOptions(HudPreset preset, float scale = 1f, float safeZone = 0f)
    {
        if (ServiceLocator.Instance is not { } locator || !locator.TryGet(out SettingsService settings))
        {
            return;
        }

        if (!_hudOptionsHeld)
        {
            _hudOptionsHeld = true;
            _heldModes = (int[])settings.Current.HudElementModes.Clone();
            _heldScale = settings.Current.HudScale;
            _heldSafeZone = settings.Current.HudSafeZone;
        }

        settings.Current.HudElementModes = HudPresets.ToSaved(HudPresets.Modes(preset));
        settings.Current.HudScale = scale;
        settings.Current.HudSafeZone = safeZone;
        EventBus.Instance?.Publish(new SettingsAppliedEvent(settings.Current));
    }

    /// <summary>Puts back the HUD options the options shots replaced, and announces them.</summary>
    private void RestoreHudOptions()
    {
        if (!_hudOptionsHeld || ServiceLocator.Instance is not { } locator ||
            !locator.TryGet(out SettingsService settings))
        {
            return;
        }

        _hudOptionsHeld = false;
        settings.Current.HudElementModes = _heldModes;
        settings.Current.HudScale = _heldScale;
        settings.Current.HudSafeZone = _heldSafeZone;
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

    /// <summary>
    /// Raises three notices in one frame through the events the feed already answers, into a feed
    /// emptied of the objective toast the shot before raised.
    ///
    /// The third was a companion's remark, and with subtitles on a remark is a caption, not a toast
    /// (<see cref="Notifications.PushBark"/>): the stack this shot exists to photograph came out two
    /// deep. A pickup is a toast under every setting.
    /// </summary>
    private void StageToastStack()
    {
        if (Player() is not { } player || QuestShotFixtures.FindFirst<Notifications>(GetTree().Root) is not { } feed)
        {
            return;
        }

        // Under an ordinary tracker. The five-objective campaign fixture the shots before this one
        // track is the tallest tracker the game can draw, and under it two toasts are all that fit
        // above the minimap at 1280x720: the feed holds the third back, which is the rule working
        // and not the stack this shot is for.
        if (QuestShotFixtures.Log() is { } log)
        {
            foreach (QuestProgress progress in log.Quests)
            {
                if (progress.Status == QuestStatus.Active && !progress.Quest.IsLedger &&
                    progress.Quest.Id != QuestShotFixtures.AshWind)
                {
                    log.Track(progress.Quest.Id);
                    break;
                }
            }
        }

        feed.EndCombatForCapture();
        feed.ClearShownForCapture();
        EventBus.Instance?.Publish(new Progression.LeveledUpEvent(player, 7, 1));
        EventBus.Instance?.Publish(new GameSavedEvent("shots", true));
        if (ItemDatabase.Get("item.ammo.arrows") is { } arrows)
        {
            EventBus.Instance?.Publish(new ItemPickedUpEvent(player, arrows, 12));
            feed.FlushLootForCapture();
        }
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
