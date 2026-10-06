using System.Linq;
using Embervale.Core;
using Embervale.Core.Services;
using Embervale.Core.Events;
using Embervale.Corruption;
using Embervale.Dialogue;
using Embervale.Economy;
using Embervale.Enemies;
using Embervale.Factions;
using Embervale.Items;
using Embervale.Localization;
using Embervale.Magic;
using Embervale.Save;
using Embervale.Player;
using Embervale.Progression;
using Embervale.Quests;
using Embervale.UI;
using Embervale.World;
using Godot;

namespace Embervale.Debugging;

/// <summary>
/// The panel screenshot harness — <c>godot --path . -- --panelshots</c> (39.5C).
///
/// ⚠️ <b>This is the half of the UI-capture gap <c>--hudshots</c> did NOT close, and it is the half
/// that has actually cost this project defects.</b> All three of 39.5A's shipped screen-space bugs
/// were on the <b>map screen</b> — a projection built before layout so the whole world drew about a
/// half-pixel origin, a correct plot on a black rectangle that read as a failed load, and fast travel
/// relocated behind a discovery gate. The maintainer found every one by opening the map, because
/// nothing here could: <c>--play</c> boots the world but cannot press <c>M</c>, and the Godot MCP
/// drives the <i>editor</i>, where these panels do not exist — <see cref="Bootstrap.UICompositionRoot"/>
/// constructs them at runtime.
///
/// <see cref="UiPanel.SetOpen"/> is public, so a panel can be opened from code with no key injection
/// at all. That is the whole trick, and it means this is a shot list rather than a new system.
///
/// ⚠️ <b>Drives the panel through its own public entry points</b> (<see cref="MapScreen.FocusLocation"/>,
/// <see cref="MapScreen.SetZoom"/>) rather than reaching past them, so what lands in the PNG is the
/// state a player reaches by searching and zooming. A capture path that bypasses the real one
/// photographs the harness.
/// </summary>
public sealed partial class PanelShots : ShotHarness
{
    protected override string Flag => "--panelshots";

    protected override string OutputDir => "user://panelshots";

    /// <summary>Set by the bootstrap — the harness never searches the tree for these.</summary>
    public MapScreen? Map { get; set; }

    public QuestLogPanel? Journal { get; set; }

    /// <summary>The character screen — added in 42A for the Guilds tab.</summary>
    public InventoryPanel? Character { get; set; }

    public VendorPanel? Vendor { get; set; }

    public DialoguePanel? Dialogue { get; set; }

    /// <summary>The two hub screens the bootstrap does not hand over, reached the way a hub step
    /// reaches them: through the node that holds all five.</summary>
    private BestiaryPanel? Bestiary => (Map?.GetParent() as IHubHost)?.HubPanel(HubTab.Bestiary) as BestiaryPanel;

    private SpellbookPanel? Spellbook => (Map?.GetParent() as IHubHost)?.HubPanel(HubTab.Spellbook) as SpellbookPanel;

    // --- Window guard ----------------------------------------------------------------------------

    /// <summary>Consecutive frames the window must hold the capture size before the loop moves on:
    /// enough for a resize to be laid out and drawn, so a frame is never read mid-change.</summary>
    private const int SteadyFrames = 8;

    /// <summary>Frames this harness will spend putting the window back before it lets the base report
    /// the wrong size instead of waiting for ever.</summary>
    private const int MaxRestoreFrames = 240;

    private int _steadyFrames;
    private int _restoreFrames;
    private bool _reportedWindow;

    /// <summary>
    /// Holds the capture loop while the window is not the size the run asked for.
    ///
    /// ⚠️ At <c>EMBERVALE_RES=1280x800</c> with <c>EMBERVALE_SHOT_UISCALE=1.5</c> this harness returned
    /// 2880x1659 frames for its first three shots and a flat one for the fourth, then captured
    /// correctly. The viewport texture is the window's size, so the WINDOW was not 1280x800 for the
    /// first seconds of the run and was put right part-way through: the flat frame is the one read as
    /// the resize landed. Nothing in the map draws to a second viewport or sizes the window, and the
    /// base sets the size once, in <c>_Ready</c>, where a window that is maximized or still being
    /// placed ignores it.
    ///
    /// So the size is asserted for as long as the run lasts rather than once: every frame, before the
    /// loop may drive or capture, the window must be windowed and at the capture size, and have been
    /// for <see cref="SteadyFrames"/>. When it is not, this says what it found (mode, size, screen and
    /// screen scale, which is the evidence the cause needs) and restores it.
    /// </summary>
    public override void _Process(double delta)
    {
        if (WindowHoldsCaptureSize())
        {
            base._Process(delta);
        }
    }

    private bool WindowHoldsCaptureSize()
    {
        if (DisplayServer.GetName() == "headless" || _restoreFrames > MaxRestoreFrames)
        {
            return true;
        }

        Vector2I want = RequestedSize();
        DisplayServer.WindowMode mode = DisplayServer.WindowGetMode();
        Vector2I size = DisplayServer.WindowGetSize();
        if (mode == DisplayServer.WindowMode.Windowed && size == want)
        {
            if (_steadyFrames < SteadyFrames)
            {
                _steadyFrames++;
                return false;
            }

            return true;
        }

        if (!_reportedWindow)
        {
            _reportedWindow = true;
            Core.Diagnostics.Log.Warn($"{Flag}: window is {mode} {size.X}x{size.Y}, capture size is {want.X}x{want.Y} " +
                     $"(screen {DisplayServer.ScreenGetSize().X}x{DisplayServer.ScreenGetSize().Y}, " +
                     $"scale {DisplayServer.ScreenGetScale():0.##}); restoring before the next shot.");
        }

        _steadyFrames = 0;
        _restoreFrames++;
        if (mode != DisplayServer.WindowMode.Windowed)
        {
            DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
        }

        DisplayServer.WindowSetSize(want);
        return false;
    }

    /// <summary>The size the run asked for, read the way <see cref="ShotHarness"/> reads it.</summary>
    private static Vector2I RequestedSize()
    {
        string size = OS.GetEnvironment("EMBERVALE_RES");
        string[] parts = (size.Length > 0 ? size : OS.GetEnvironment("EMBERVALE_SHOT_SIZE")).Split('x');
        return parts.Length == 2 && int.TryParse(parts[0], out int w) && int.TryParse(parts[1], out int h) && w >= 640 && h >= 360
            ? new Vector2I(w, h)
            : new Vector2I(1280, 720);
    }

    protected override string? ValidateShotState(string name)
    {
        if (Map is null || Journal is null || Character is null || Vendor is null || Dialogue is null)
            return "bootstrap did not provide every required panel";
        if (ValidateKnowledgeShot(name) is { } knowledge)
            return knowledge;
        if ((name.StartsWith("00-") || name.StartsWith("01-") || name.StartsWith("02-") ||
             name.StartsWith("03-") || name.StartsWith("04-") || name.StartsWith("05-")) && !Map.IsOpen)
            return "map did not open";
        if (name.StartsWith("07-") || name.StartsWith("08-") || name.StartsWith("09-") ||
            name.StartsWith("10-") || name.StartsWith("11-") || name.StartsWith("12-"))
            return Journal.IsOpen ? null : "journal did not open";
        if (name == "13-journal-closed" && Journal.IsOpen)
            return "journal did not close";

        // The campaign quest UI. Each frame proves it is the card it meant to photograph, because a journal
        // that quietly opened on another quest would still be a valid-looking PNG.
        if (name == "12b-journal-chapters")
        {
            if (!Journal.IsOpen || Journal.CurrentSection != JournalSection.Main ||
                Journal.SelectedQuestId != QuestShotFixtures.LastHearth)
                return "journal is not on the Main tab with the Last Hearth card";
            if (!Loc.Has("chapter.ch.2.ashen.title"))
                return "chapter title text did not resolve";
        }
        if (name == "12c-journal-detail" &&
            (!Journal.IsOpen || Journal.SelectedQuestId != QuestShotFixtures.AshWind))
            return "journal is not showing the Ash on the Wind card";
        if (name == "12d-journal-completed" &&
            (!Journal.IsOpen || Journal.CurrentSection != JournalSection.Completed))
            return "journal is not on the Completed tab";
        if (name == "12e-journal-errands" &&
            (!Journal.IsOpen || Journal.CurrentSection != JournalSection.Errands))
            return "journal is not on the Errands tab";
        if (name == "16b-dialogue-tags" && !Dialogue.IsOpen)
            return "dialogue panel did not open on the tags fixture";
        if ((name.StartsWith("14-") || name.StartsWith("18-") || name.StartsWith("19-")) && !Character.IsOpen)
            return "character/inventory panel did not open";
        if (name == "14b-progression-stats" && !Character.IsOpen)
            return "character panel did not stay open on the progression tab";
        if (name == "14c-gear-materials" && (!Character.IsOpen || !Character.ShowingMaterials))
            return "character panel is not showing the material bag";
        if (name.Length > 2 && name[0] == '2' && name[1] is >= '1' and <= '4')
            return ValidatePerkShot(name);
        if (name == "15-shop" && !Vendor.IsOpen)
            return "vendor panel did not open";
        if (name == "16-dialogue" && !Dialogue.IsOpen)
            return "dialogue panel did not open";
        if (name.EndsWith("-closed") && (Map.IsOpen || Journal.IsOpen || Dialogue.IsOpen))
            return "a panel expected to be closed remains open";
        return null;
    }

    /// <summary>The 2026-10 knowledge-panel states. Each proves the state it names was reached, since
    /// a rail left on the wrong tab or a page left sealed would still be a valid-looking PNG.</summary>
    private string? ValidateKnowledgeShot(string name)
    {
        switch (name)
        {
            case "05c-map-legend-collapsed":
                return Map!.LegendOpen && !Map.LegendExpanded ? null : "map rail is not on its folded legend";
            case "05d-map-legend-expanded":
                return Map!.LegendOpen && Map.LegendExpanded ? null : "map rail is not on its unfolded legend";
            case "05e-map-pin-snap":
                return Map!.SnappedId is null ? "the map cursor did not snap to a pin"
                    : Map.SelectedLocationId != SnapTarget ? "the map is not focused on the snap target"
                    : null;
            case "05f-map-travel-confirm":
                return Map!.PendingTravelId is null ? "no fast-travel confirmation is showing" : null;
            case "12f-journal-show-on-map":
                return !Map!.IsOpen ? "show on map did not open the map"
                    : Journal!.IsOpen ? "show on map left the journal open"
                    : Map.SelectedLocationId is null ? "the map opened without the quest's place selected"
                    : null;
            case "16c-dialogue-typing":
                // Reduced motion writes the line at once, and that run photographs exactly that.
                return !Dialogue!.IsOpen ? "dialogue panel did not open"
                    : UI.UiTheme.MotionEnabled && !Dialogue.IsTyping ? "the line is not being written out"
                    : UI.UiTheme.MotionEnabled && Dialogue.OptionCount != 0 ? "options are showing before the line has finished"
                    : null;
            case "16d-dialogue-complete":
                return !Dialogue!.IsOpen ? "dialogue panel did not open"
                    : Dialogue.IsTyping ? "the line is still being written out"
                    : Dialogue.OptionCount == 0 ? "the finished line has no options"
                    : null;
            case "26-bestiary-unknown":
                return BestiaryAt(BestiaryStage.Unseen);
            case "27-bestiary-sighted":
                return BestiaryAt(BestiaryStage.Sighted);
            case "28-bestiary-known":
                return BestiaryAt(BestiaryStage.Known);
            case "29-hub-spellbook":
                return Spellbook is { IsOpen: true } ? null : "spellbook did not open";
            case "29b-spellbook-pins":
                return Spellbook is not { IsOpen: true } book ? "spellbook did not open"
                    : book.PinSlotsForCapture != SpellFavouritesRules.SlotCount ? $"the pin row drew {book.PinSlotsForCapture} slot(s), expected {SpellFavouritesRules.SlotCount}"
                    : WheelShotFixtures.Caster() is not { } caster ? "player has no SpellcastingComponent"
                    : caster.SpellCount < WheelShotFixtures.LearnableCount() ? $"the caster knows {caster.SpellCount} spell(s), expected all {WheelShotFixtures.LearnableCount()}"
                    : null;
            case "29c-spellbook-pin-focused":
                return Spellbook is { IsOpen: true, PinFocusedForCapture: true } ? null : "no spell card's pin button holds focus";
            case "30-knowledge-closed":
                return Bestiary is { IsOpen: true } || Spellbook is { IsOpen: true } ? "a hub screen is still open" : null;
            default:
                return null;
        }
    }

    private string? BestiaryAt(BestiaryStage stage) =>
        Bestiary is not { IsOpen: true } bestiary ? "bestiary did not open"
        : bestiary.SelectedStage != stage ? $"the open bestiary page is {bestiary.SelectedStage}, expected {stage}"
        : null;

    private const string SnapTarget = "location.embermarket.jeweller";

    protected override void BuildShotList()
    {
        // ⚠️ Discover everything first. The map draws only what the player has found, and a save that
        // has walked one town would photograph an almost-empty realm — which would look like the map
        // working correctly and prove nothing about density, labels or clutter. This is a debug
        // harness, so revealing is honest here in a way it would never be in gameplay.
        Shot("00-open", () =>
        {
            DiscoverEverything();
            Map?.SetOpen(true);
        });

        // The realm at a glance: only Primary tier survives this zoom.
        Shot("01-realm", () => Map?.SetZoom(MapProjection.MinZoom));

        // Regional content appears — dungeons, gates, waystones.
        Shot("02-region", () => Map?.SetZoom(MapTiers.SecondaryZoom));

        // ⚠️ THE MEASURED CASE. At DetailZoom every pin is labelled, and the Embermarket's closest
        // pair sits 2.13 m apart — 19 px at 9 px/m — with labels far wider than that. If the labels
        // collide, this is the frame that shows it.
        Shot("03-detail-embermarket", () =>
        {
            Map?.FocusLocation("location.embermarket.jeweller");
            Map?.SetZoom(MapTiers.DetailZoom);
        });

        // The densest authored cell (18 locations) at the zoom that labels all of them.
        Shot("04-detail-town-hub", () =>
        {
            Map?.FocusLocation("location.ember_crown.smith");
            Map?.SetZoom(MapTiers.DetailZoom);
        });

        // A waypoint mark, at a zoom where the land and the mark are both readable.
        Shot("05-waypoint", () =>
        {
            if (ServiceLocator.Instance is { } locator && locator.TryGet(out MapService map))
            {
                map.SetWaypoint(new Vector3(0f, 0f, 40f));
            }

            Map?.SetZoom(MapProjection.DefaultZoom);
        });

        // The rail's lower half (filters, legend) is below the fold of a 720 px screen, so it gets its own
        // frame: the rail is the map's first scroll container, and this scrolls it to the end.
        Shot("05b-map-rail-bottom", () =>
        {
            if (Map is not null && QuestShotFixtures.FindFirst<ScrollContainer>(Map) is { } rail)
            {
                rail.ScrollVertical = 100000;
            }
        });

        // The rail's legend, which is also the filter: folded to one switch per group of marks, then
        // unfolded to a switch per category.
        Shot("05c-map-legend-collapsed", () => Map?.ShowLegendForCapture(expanded: false));

        Shot("05d-map-legend-expanded", () => Map?.ShowLegendForCapture(expanded: true));

        // The gamepad cursor, shown without a gamepad: the reticle at the centre of the plot, the pin it
        // has snapped to ringed, and that pin's name on a plate.
        Shot("05e-map-pin-snap", () =>
        {
            Map?.ShowPlaceForCapture();
            Map?.SnapForCapture(SnapTarget);
            Map?.SetZoom(MapTiers.DetailZoom);
        });

        // Fast travel asks before it goes: the destination and the fee, in place of the rail.
        Shot("05f-map-travel-confirm", () =>
        {
            AttuneAWaystone();
            Map?.RequestTravelForCapture();
        });

        Shot("06-map-closed", () => Map?.SetOpen(false));

        // The journal, which grew a track control in 39.5B and has never been photographed either.
        Shot("07-journal", () =>
        {
            StartAQuest();
            Journal?.SetOpen(true);
        });

        // 41C. The tally errand, which is the only quest carrying an Interact objective, a Stealth
        // condition and a deadline — so this one frame is the only check that any of the three draw
        // at all. ⚠️ The tracker's countdown is the point: a Stealth objective is seeded ALREADY MET,
        // so it must render as satisfied here rather than as 0/1, and the clock must be counting down
        // rather than sitting at its authored value.
        Shot("08-journal-timed", () =>
        {
            StartTheTally();
            Journal?.SetOpen(true);
        });

        // 41B. The journal with a Defend objective live: its count is SECONDS, so this card reads
        // "0/60" where every other objective in the game counts things. A quest whose progress bar
        // measures a different unit from its neighbours is worth looking at once.
        Shot("09-journal-defend", () =>
        {
            StartTheHold();
            Journal?.SetOpen(true);
        });

        // ⚠️ 41B, AND THIS IS THE ONE THE SUB-PHASE EXISTS TO PHOTOGRAPH. Failure is the first way a
        // quest can end without succeeding, and the journal's FAILED section had never been drawn
        // because until now the state could not exist (the panel's own header said so). A shot of
        // the happy path proves nothing about the branch — 41A's lesson, applied to its own sequel.
        Shot("10-journal-failed", () =>
        {
            FailTheHold();
            Journal?.SetOpen(true);
        });

        // ⚠️ 41D, AND THE PAIR IS THE EVIDENCE — ONE FRAME OF A BRANCH PROVES NOTHING. The barrels
        // errand has four objectives: two behind flag.hollowreach.barrels_declared, two behind
        // flag.hollowreach.barrels_hushed. This card must show the CROSSWAY pair and nothing else,
        // with the second row padlocked because the quest is SequentialObjectives — the first frame
        // anywhere that an ordered quest draws a locked step.
        Shot("11-journal-declared", () =>
        {
            SetBranch(DeclaredFlag);
            StartTheBarrels();
            Journal?.SetOpen(true);
        });

        // ⚠️ THE SAME QUEST INSTANCE, THE OTHER FLAG — and that is deliberately not two quests. It
        // is the only check anywhere that a branch is RE-DERIVED from the flag rather than frozen
        // when the quest was accepted, which is the entire reason 41D added no save state: the flag
        // already persists, so the branch comes back with it. If the card still shows the Crossway
        // rows here, the tracker/journal are caching a fork.
        Shot("12-journal-hushed", () =>
        {
            SetBranch(HushedFlag);
            Journal?.SetOpen(true);
        });

        // The campaign quest UI. A set of quests shaped like the campaign's (chapters, a ledger umbrella, an
        // errand, finished errands) is built in memory, because no authored quest uses the new fields yet.
        // Chapter groups first: the Last Hearth card is open so the index shows three chapters, the Ledger
        // fold and the updated dots on the quests that have not been read.
        Shot("12b-journal-chapters", () =>
        {
            if (QuestShotFixtures.Log() is { } log)
            {
                QuestShotFixtures.StartCampaignSet(log);
            }

            Journal?.Select(QuestShotFixtures.LastHearth);
            Journal?.SetOpen(true);
        });

        // The detail card: region, level and main chips, the giver, the long prose, the stage log with a ticked
        // first step, the current step with its hint and place, an Optional chip, locked steps, and the full
        // rewards (xp, gold, an item, a faction gain).
        Shot("12c-journal-detail", () => Journal?.Select(QuestShotFixtures.AshWind));

        // Finished errands, newest first, folded after five.
        Shot("12d-journal-completed", () => Journal?.Select(QuestShotFixtures.OldErrandPrefix + QuestShotFixtures.OldErrands));

        Shot("12e-journal-errands", () => Journal?.Select(QuestShotFixtures.Errand));

        // "Show on map" from the open card: the journal hands over to the map, which opens centred on the
        // live objective's place with its details in the rail.
        Shot("12f-journal-show-on-map", () =>
        {
            Journal?.Select(QuestShotFixtures.AshWind);
            Journal?.ShowSelectedOnMapForCapture();
        });

        Shot("13-journal-closed", () =>
        {
            Map?.SetOpen(false);
            Journal?.SetOpen(false);
        });

        Shot("14-inventory-full", () =>
        {
            StageInventory();
            Character?.SetOpen(true);
            Character?.ShowGear();
        });

        // The stat block with its per-point lines under each primary (progression P2).
        Shot("14b-progression-stats", () => Character?.ShowProgression());

        // The material bag (ics:inv-ui): the Gear screen's second grid, through the tab a click
        // throws. StageInventory's materials land here now that the player's bag is on, so this is
        // also the frame that shows the pack grid no longer carrying them.
        Shot("14c-gear-materials", () => Character?.ShowMaterials());

        Shot("15-shop", () =>
        {
            Character?.SetOpen(false);
            if (Player() is { } player && ShopDatabase.All.Count > 0)
            {
                EventBus.Instance?.Publish(new ShopOpenedEvent(player, ShopDatabase.All[0]));
            }
        });

        Shot("15b-shop-scrolled", () => UiAuditShots.ScrollToEnd(Vendor));

        Shot("16-dialogue", () =>
        {
            Vendor?.SetOpen(false);
            if (Player() is { } player && DialogueFixture() is { } dialogue)
            {
                EventBus.Instance?.Publish(new DialogueStartedEvent(player, player, dialogue));
            }
        });

        // Consequence chips on every kind of choice, the quest line under the speaker (the Elder is the Talk
        // objective of the tracked Ash on the Wind), and the numbered choices. The first conversation is ended
        // properly first: an overlapping start is ignored by design, and would photograph the old node.
        Shot("16b-dialogue-tags", () =>
        {
            Dialogue?.EndConversation();
            if (QuestShotFixtures.Log() is { } log)
            {
                log.Track(QuestShotFixtures.AshWind);
            }

            if (Player() is { } player)
            {
                EventBus.Instance?.Publish(
                    new DialogueStartedEvent(player, player, QuestShotFixtures.BuildElderDialogue()));
            }
        });

        // A line writing itself out, with no options yet. The typewriter is off in a capture run, so it is
        // turned on for these two frames; the hook holds the line part-written rather than running the
        // clock, so the frame does not depend on how fast the harness renders.
        Shot("16c-dialogue-typing", () =>
        {
            Dialogue?.EndConversation();
            Dialogue?.TypewriterForCapture(true);
            if (Player() is { } player && DialogueFixture() is { } dialogue)
            {
                EventBus.Instance?.Publish(new DialogueStartedEvent(player, player, dialogue));
            }
        });

        // The same line finished the way an accept press finishes it, and its options arrived.
        Shot("16d-dialogue-complete", () => Dialogue?.FinishLineForCapture());

        Shot("17-dialogue-closed", () =>
        {
            Dialogue?.TypewriterForCapture(false);
            Dialogue?.EndConversation();
            Dialogue?.SetOpen(false);
        });

        // ⚠️ 42A, AND THE STATES ARE THE POINT — a Guilds tab where all five read "Unaffiliated" is
        // what the panel draws before anything happens, so it proves the tab exists and nothing
        // else (41A's trap: a harness shot is only evidence if it drives the thing you changed).
        // This frame is staged through the SAME story flags a dialogue effect writes, so every one
        // of the five states below is one a player can actually reach.
        Shot("18-guilds", () =>
        {
            StageGuildStates();
            Journal?.SetOpen(false);
            Character?.SetOpen(true);
            Character?.ShowGuilds();
        });

        // ⚠️ 42A'S PERSISTENCE EVIDENCE, AND IT IS A FRAME RATHER THAN AN ASSERTION BECAUSE THE
        // FAILURE IS VISIBLE: this saves the staged states, then promotes the Dawnwardens to rank 3
        // and joins the Emberbound, then loads the save back. A `Load` that MERGED over live state
        // would leave both mutations standing — the tab would read "Dawnblade — rank 3 of 3" and an
        // Emberbound membership that the save does not contain. It must read exactly like 14.
        Shot("19-guilds-reloaded", () =>
        {
            // A harness save is not a player save: a conversation or duel the driver left open must not refuse it.
            SaveManager.Instance?.ClearSaveBlocks();
            SaveManager.Instance?.SaveGame(GuildShotSlot);
            MutateGuildsAfterSaving();
            SaveManager.Instance?.LoadGame(GuildShotSlot);
            Character?.ShowGuilds();
        });

        // ⚠️ And the slot is deleted again. A shot list that leaves a real save behind makes ITSELF
        // the newest slot, so the next `--play` boots into the harness's staged world rather than
        // the maintainer's game — a side effect nobody would think to look for here.
        Shot("20-guilds-closed", () =>
        {
            Character?.SetOpen(false);
            SaveManager.Instance?.DeleteSlot(GuildShotSlot);
        });

        // The perk tree (progression upgrade P6). Four states, each staged through the real components:
        // an unspent tree, a build with points in it, a corruption-gated branch, and the respec confirmation.
        Shot("21-perks-empty", () =>
        {
            StagePerkPoints(6);
            Character?.SetOpen(true);
            Character?.ShowPerks(PerkBranch.Warrior);
        });

        // Learnable, locked-behind-a-tier and maxed nodes in one frame, lit and unlit connectors, a stocked gold purse.
        Shot("22-perks-mid-build", () =>
        {
            StagePerkPoints(14);
            StageGold(1000);
            LearnPerks(("perk.might", 5), ("perk.toughness", 3), ("perk.warding", 2), ("perk.iron_stance", 1), ("perk.second_wind", 1));
            Character?.ShowPerks(PerkBranch.Warrior);
        });

        // Keyboard navigation, driven the way a player drives it: focus the first node, then press Right and Down. The
        // detail pane follows focus, so landing on Second Wind (one right, one down from Might) is the evidence that the
        // grid has a sensible focus order. Validated below, not just photographed.
        Shot("22b-perks-navigated", () =>
        {
            FocusFirstPerkNode();
            Press(Key.Right);
            Press(Key.Down);
        });

        // Enter on the focused node buys a rank through Learn, the rebuild that follows must keep focus there.
        Shot("22c-perks-learned-by-key", () => Press(Key.Enter));

        // Untainted players see every Ashbound perk gated; at Touched the first opens and the rest stay shut.
        Shot("23-perks-corruption-gated", () =>
        {
            if (Player()?.GetComponent<CorruptionComponent>() is { } corruption)
            {
                corruption.Set(25);
            }

            Character?.ShowPerks(PerkBranch.Ashbound);
        });

        Shot("24-perks-respec-confirm", () =>
        {
            Player()?.GetComponent<CorruptionComponent>()?.Set(0);
            Character?.ShowPerks(PerkBranch.Warrior, confirmRespec: true);
        });

        Shot("25-perks-closed", () => Character?.SetOpen(false));

        // The bestiary's three stages on one category: a sealed page, a part-written one and a written
        // one with its wards. Tallies are staged through the service's own Load, since a kill cannot be.
        Shot("26-bestiary-unknown", () =>
        {
            StageBestiary();
            Bestiary?.SetOpen(true);
            Bestiary?.SelectForCapture(_bestiaryUnseen);
        });

        Shot("27-bestiary-sighted", () => Bestiary?.SelectForCapture(_bestiarySighted));

        Shot("28-bestiary-known", () => Bestiary?.SelectForCapture(_bestiaryKnown));

        // The spellbook under the same hub strip and legend as the other four.
        Shot("29-hub-spellbook", () =>
        {
            Bestiary?.SetOpen(false);
            Spellbook?.SetOpen(true);
        });

        // The eight favourite slots over the page, on a caster that knows every learnable spell, with
        // the last slot emptied so an empty socket and a Pin that has somewhere to go are both shown.
        // The page turns to the first school with an unpinned spell.
        Shot("29b-spellbook-pins", () =>
        {
            WheelShotFixtures.TeachEverything();
            WheelShotFixtures.Caster()?.SetFavourite(SpellFavouritesRules.SlotCount - 1, SpellFavouritesRules.None);
            Spellbook?.ShowUnpinnedForCapture();
        });

        // A card's Pin button under keyboard or pad focus.
        Shot("29c-spellbook-pin-focused", () => Spellbook?.FocusPinForCapture());

        Shot("30-knowledge-closed", () =>
        {
            Spellbook?.SetOpen(false);
            WheelShotFixtures.Restore();
        });
    }

    private string _bestiaryUnseen = string.Empty;
    private string _bestiarySighted = string.Empty;
    private string _bestiaryKnown = string.Empty;

    /// <summary>
    /// Writes three tallies into the bestiary: one creature hunted to its threshold (and one of those
    /// kills Ashen), one seen once, and one never met. All three are taken from the category with the
    /// most pages that can show every stage, so the three frames share an index.
    /// </summary>
    private void StageBestiary()
    {
        if (ServiceLocator.Instance is not { } locator || !locator.TryGet(out BestiaryService service))
        {
            return;
        }

        foreach (BestiaryCategory category in System.Enum.GetValues<BestiaryCategory>())
        {
            var pages = BestiaryDatabase.All.Where(e => e.Category == category).ToList();

            // A written page shows wards only for a creature with an archetype; a part-written one
            // needs a threshold above one kill.
            BestiaryEntryResource? known = pages.FirstOrDefault(e => EnemyArchetypeDatabase.Get(e.Id) is { AttributesPath.Length: > 0 });
            BestiaryEntryResource? sighted = pages.FirstOrDefault(e => e != known && e.KillsToKnow > 1);
            BestiaryEntryResource? unseen = pages.FirstOrDefault(e => e != known && e != sighted);
            if (known is null || sighted is null || unseen is null)
            {
                continue;
            }

            _bestiaryKnown = known.Id;
            _bestiarySighted = sighted.Id;
            _bestiaryUnseen = unseen.Id;
            int enough = Mathf.Max(1, known.KillsToKnow);
            service.Load(new Godot.Collections.Dictionary
            {
                ["kills"] = new Godot.Collections.Dictionary { [known.Id] = enough, [sighted.Id] = 1 },
                ["ashen"] = new Godot.Collections.Dictionary { [known.Id] = 1 },
            });
            return;
        }
    }

    /// <summary>Makes sure one waystone is attuned, so the map has a journey to ask about. A save that
    /// has attuned none gets the Embermarket's, in the region the player is standing in.</summary>
    private static void AttuneAWaystone()
    {
        if (ServiceLocator.Instance is not { } locator || !locator.TryGet(out FastTravelService travel) ||
            travel.Nodes.Any())
        {
            return;
        }

        string region = locator.TryGet(out RegionStreamer streamer) ? streamer.ActiveRegionId ?? string.Empty : string.Empty;
        travel.Discover(
            "travel.ember_crown.embermarket", Loc.T("travel.ember_crown.embermarket.name"), region,
            Player()?.GlobalPosition ?? Vector3.Zero);
    }

    private string? ValidatePerkShot(string name)
    {
        if (!Character!.IsOpen)
        {
            return "character panel did not open";
        }

        PerksComponent? perks = Player()?.GetComponent<PerksComponent>();
        if (perks is null)
        {
            return "player has no perks component";
        }

        PerkTreePanel.ViewState view = Character.PerkTreeState;
        PerkBranch? branch = view.Branch;
        bool respecPending = view.ConfirmingRespec;
        if (name.StartsWith("22b-") && view.FocusedId != "perk.second_wind")
            return $"keyboard focus is on {view.FocusedId}, expected perk.second_wind";
        if (name.StartsWith("22c-") && (view.FocusedId != "perk.second_wind" || perks.RankOf("perk.second_wind") != 2))
            return $"after ui_accept the focused perk is {view.FocusedId} at rank {perks.RankOf("perk.second_wind")}, expected perk.second_wind at 2";
        PerkBranch expected = name.StartsWith("23-") ? PerkBranch.Ashbound : PerkBranch.Warrior;
        if (branch != expected)
            return $"perk tree is on {branch}, expected {expected}";
        if (respecPending != name.StartsWith("24-"))
            return respecPending ? "a respec confirmation is showing" : "the respec confirmation is not showing";
        if ((name.StartsWith("22-") || name.StartsWith("24-")) && perks.PointsSpent == 0)
            return "no points are spent, so this is not a built tree";
        if (name.StartsWith("21-") && perks.PointsSpent != 0)
            return "points are already spent, so this is not an empty tree";
        if (name.StartsWith("23-") && !PerkDatabase.All.Any(p => p.Branch == PerkBranch.Ashbound && !perks.MeetsCorruption(p)))
            return "no Ashbound perk is corruption-gated";
        return null;
    }

    /// <summary>Focuses the first perk node of the open tree: the first button under the tree canvas.</summary>
    private void FocusFirstPerkNode()
    {
        if (FirstOf<PerkTreeCanvas>(Character!) is { } canvas && FirstOf<Button>(canvas) is { } node)
        {
            node.GrabFocus();
        }
    }

    private static T? FirstOf<T>(Node root) where T : Node
    {
        foreach (Node child in root.GetChildren())
        {
            if (child is T match)
            {
                return match;
            }

            if (FirstOf<T>(child) is { } inner)
            {
                return inner;
            }
        }

        return null;
    }

    /// <summary>Sends one key as a press and release into the viewport; the default <c>ui_*</c> actions map it to focus
    /// navigation and accept, the same path a d-pad or gamepad button takes.</summary>
    private void Press(Key key)
    {
        GetViewport().PushInput(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = true });
        GetViewport().PushInput(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = false });
    }

    /// <summary>Tops the unspent skill points up to at least <paramref name="atLeast"/>.</summary>
    private static void StagePerkPoints(int atLeast)
    {
        if (Player()?.GetComponent<ProgressionComponent>() is { } progression && progression.SkillPoints < atLeast)
        {
            progression.RefundSkillPoints(atLeast - progression.SkillPoints);
        }
    }

    private static void StageGold(int atLeast)
    {
        if (Player()?.GetComponent<InventoryComponent>() is { } pack && ItemDatabase.Get(GameIds.Currency.Gold) is { } gold
            && pack.CountOf(gold) < atLeast)
        {
            pack.AddItem(gold, atLeast - pack.CountOf(gold));
        }
    }

    /// <summary>Buys ranks through <see cref="PerksComponent.Learn"/>, in order, so gates and costs apply as in play.</summary>
    private static void LearnPerks(params (string Id, int Rank)[] wanted)
    {
        if (Player()?.GetComponent<PerksComponent>() is not { } perks)
        {
            return;
        }

        foreach ((string id, int rank) in wanted)
        {
            if (PerkDatabase.Get(id) is { } perk)
            {
                while (perks.RankOf(id) < rank && perks.Learn(perk))
                {
                }
            }
        }
    }

    private static PlayerCharacter? Player() =>
        ServiceLocator.Instance is { } locator && locator.TryGet(out PlayerCharacter player) ? player : null;

    /// <summary>Fills the pack so list and grid screens are judged at density: a few rolled Rare and Epic
    /// pieces first (they carry affix chips, the tallest rows), then plain stock up to 18 slots.
    /// Shared with <see cref="UiAuditShots"/>.</summary>
    internal static void StageInventory()
    {
        if (Player()?.GetComponent<InventoryComponent>() is not { } pack)
        {
            return;
        }

        int rolled = 0;
        foreach (ItemResource item in ItemDatabase.All.Values)
        {
            if (item is EquippableItemResource gear && rolled < 6)
            {
                pack.AddInstance(Loot.LootGenerator.RollAffixed(gear, rolled % 2 == 0 ? ItemRarity.Rare : ItemRarity.Epic), 1);
                rolled++;
            }
        }

        foreach (ItemResource item in ItemDatabase.All.Values)
        {
            pack.AddItem(item, item.MaxStack > 1 ? Mathf.Min(item.MaxStack, 12) : 1);
            if (pack.UsedSlots >= Mathf.Min(pack.Capacity, 18))
            {
                break;
            }
        }
    }

    private static DialogueResource? DialogueFixture()
    {
        DialogueResource? best = null;
        int score = -1;
        foreach (DialogueResource dialogue in DialogueDatabase.All)
        {
            DialogueNode? node = dialogue.StartNode();
            int candidate = node?.Text.Length ?? 0;
            candidate += (node?.ChoiceList().Count ?? 0) * 80;
            if (candidate > score)
            {
                score = candidate;
                best = dialogue;
            }
        }

        return best;
    }

    /// <summary>
    /// Puts each of the five guilds in a different state (42A), so one frame carries the whole
    /// vocabulary: a ranked member, a finished arc, a departure, a refusal and an untouched order.
    ///
    /// ⚠️ Ranks are set as the cumulative run 1..N, exactly as the dialogue effect and the `guild`
    /// console command do. Setting rank 2 alone would photograph a `RankGap` contradiction and call
    /// it a promotion.
    /// </summary>
    private const string GuildShotSlot = "guildshots";

    /// <summary>Promotes and joins AFTER the save, so the reload has something to undo. Nothing here
    /// is a state the shot list wants — it exists only to be discarded by the load.</summary>
    private static void MutateGuildsAfterSaving()
    {
        if (Flags() is not { } flags)
        {
            return;
        }

        flags.Set(GuildRules.RankFlag(GameIds.Factions.Dawnwardens, 3));
        flags.Set(GuildRules.OfferedFlag(GameIds.Factions.Emberbound));
        flags.Set(GuildRules.JoinedFlag(GameIds.Factions.Emberbound));
    }

    private static Dialogue.StoryFlagsComponent? Flags() =>
        ServiceLocator.Instance is { } locator && locator.TryGet(out PlayerCharacter player)
            ? player.GetComponent<Dialogue.StoryFlagsComponent>()
            : null;

    private static void StageGuildStates()
    {
        if (Flags() is not { } flags)
        {
            return;
        }

        void Join(string guild, int rank)
        {
            flags.Set(GuildRules.OfferedFlag(guild));
            flags.Set(GuildRules.JoinedFlag(guild));
            for (int i = 1; i <= rank; i++)
            {
                flags.Set(GuildRules.RankFlag(guild, i));
            }
        }

        Join(GameIds.Factions.Dawnwardens, 2);

        Join(GameIds.Factions.AshHunters, 3);
        flags.Set(GuildRules.FinaleFlag(GameIds.Factions.AshHunters));

        Join(GameIds.Factions.IronSyndicate, 1);
        flags.Set(GuildRules.LeftFlag(GameIds.Factions.IronSyndicate));

        flags.Set(GuildRules.OfferedFlag(GameIds.Factions.VeiledArchive));
        flags.Set(GuildRules.RefusedFlag(GameIds.Factions.VeiledArchive));

        // The Emberbound are deliberately left untouched — the concealed order the player has never
        // met is a real state, and it is the one the empty tab is made of.
    }

    /// <summary>Starts the sealed-tally errand (41C) and tracks it, so the HUD tracker draws its
    /// countdown and the journal draws its Interact and Stealth rows.</summary>
    private static void StartTheTally()
    {
        if (Log() is { } log && QuestDatabase.Get(TallyQuestId) is { } tally && log.StartQuest(tally))
        {
            log.Track(tally.Id);
        }
    }

    private const string TallyQuestId = "quest.emberdeep.tally";

    /// <summary>Starts the north-road hold (41B) — the only authored quest with a Defend objective,
    /// and the one this harness can reach: it has no prerequisite, so it starts from a fresh save.
    /// The escort quest deliberately cannot be started here, because it requires 41A's courier quest
    /// to be COMPLETED and nothing in a screenshot harness can walk to Hollowreach.</summary>
    private static void StartTheHold()
    {
        if (Log() is { } log && QuestDatabase.Get(HoldQuestId) is { } hold && log.StartQuest(hold))
        {
            log.Track(hold.Id);
        }
    }

    /// <summary>Drives the hold into <see cref="QuestStatus.Failed"/> — the state the player reaches
    /// by dying with it live, which is not something a harness can stage.</summary>
    private static void FailTheHold() => Log()?.Fail(HoldQuestId);

    private const string HoldQuestId = "quest.warband.hold_north";

    private const string BarrelsQuestId = "quest.hollowreach.barrels";

    private const string DeclaredFlag = "flag.hollowreach.barrels_declared";

    private const string HushedFlag = "flag.hollowreach.barrels_hushed";

    /// <summary>Starts the branching barrels errand (41D) and tracks it. Authored with no
    /// prerequisite for 41C's reason — a gate only a human can open is a gate no instrument sees
    /// behind, and this quest is the only caller of every 41D mechanic.</summary>
    private static void StartTheBarrels()
    {
        if (Log() is { } log && QuestDatabase.Get(BarrelsQuestId) is { } barrels && log.StartQuest(barrels))
        {
            log.Track(barrels.Id);
        }
    }

    /// <summary>
    /// Puts the save on exactly one branch (41D) — sets <paramref name="flag"/> and clears the other.
    ///
    /// ⚠️ Clearing the other is not tidiness. Both flags set means both paths live, which is a state
    /// the authored dialogue makes unreachable (each fork choice hides on the other's flag) and which
    /// this harness could otherwise walk straight into — photographing four objective rows and
    /// calling it a branch.
    /// </summary>
    private static void SetBranch(string flag)
    {
        if (ServiceLocator.Instance is not { } locator ||
            !locator.TryGet(out PlayerCharacter player) ||
            player.GetComponent<Dialogue.StoryFlagsComponent>() is not { } flags)
        {
            return;
        }

        flags.Set(flag);
        flags.Clear(flag == DeclaredFlag ? HushedFlag : DeclaredFlag);
    }

    private static QuestLogComponent? Log() =>
        ServiceLocator.Instance is { } locator && locator.TryGet(out PlayerCharacter player)
            ? player.GetComponent<QuestLogComponent>()
            : null;

    /// <summary>Reveals every region and location so the plot is dense enough to judge.</summary>
    private static void DiscoverEverything()
    {
        if (ServiceLocator.Instance is not { } locator || !locator.TryGet(out MapService map))
        {
            return;
        }

        foreach (RegionResource region in RegionDatabase.All)
        {
            map.DiscoverRegion(region.Id);
        }

        // ⚠️ DiscoverRegion reveals only the places that arrive with their region (RevealWithCell). A
        // stall or a bench is found by walking up to it, so the detail shots drew an empty market and
        // the pin-snap shot had no pin to snap to. The rest are added the way a save that has walked
        // everywhere carries them: through the service's own Save and Load, which is its public
        // contract. Flag-gated places stay hidden, as they would be in that save.
        Godot.Collections.Dictionary data = map.Save();
        Godot.Collections.Array known = data.TryGetValue("locations", out Variant saved) && saved.VariantType == Variant.Type.Array
            ? saved.AsGodotArray()
            : new Godot.Collections.Array();
        foreach (MapLocationResource location in MapLocationDatabase.All)
        {
            if (location.RequiredFlagId.Length == 0 && location.RevealFlagId.Length == 0 && !map.IsDiscovered(location.Id))
            {
                known.Add(new Godot.Collections.Dictionary { ["id"] = location.Id });
            }
        }

        data["locations"] = known;
        map.Load(data);
    }

    /// <summary>
    /// Starts a quest so the journal has a card to draw, preferring the courier quest (41A).
    ///
    /// ⚠️ <b>The preference is doing real work, not tidying.</b> <c>quest.hollowreach.word</c> is the
    /// only quest carrying a Reach and a Talk objective, so it is the only one whose journal card
    /// renders the two new types at all — a shot of a Kill objective proves nothing about them.
    ///
    /// ⚠️ <b>And this shot doubles as the one check that a Reach objective is PROXIMITY rather than
    /// DISCOVERY.</b> <see cref="DiscoverEverything"/> runs before this, revealing all 64 locations
    /// while the player stands in the town hub — roughly 90 m from Hollowreach. A discovery-driven
    /// Reach would therefore render <c>1/1</c> here, complete, without the player having walked
    /// anywhere. It must render <c>0/1</c>. That distinction is invisible to the build, the tests and
    /// the validator alike, which is exactly why it is pinned to a frame somebody looks at.
    /// </summary>
    private static void StartAQuest()
    {
        if (ServiceLocator.Instance is not { } locator ||
            !locator.TryGet(out PlayerCharacter player) ||
            player.GetComponent<QuestLogComponent>() is not { } log)
        {
            return;
        }

        if (QuestDatabase.Get("quest.hollowreach.word") is { } courier && log.StartQuest(courier))
        {
            log.Track(courier.Id);
            return;
        }

        foreach (QuestResource quest in QuestDatabase.All)
        {
            if (log.StartQuest(quest))
            {
                return;
            }
        }
    }
}
