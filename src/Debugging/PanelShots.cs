using Embervale.Core;
using Embervale.Core.Services;
using Embervale.Core.Events;
using Embervale.Dialogue;
using Embervale.Economy;
using Embervale.Factions;
using Embervale.Items;
using Embervale.Save;
using Embervale.Player;
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
/// drives the <i>editor</i>, where these panels do not exist — <see cref="Bootstrap.GameBootstrap"/>
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

        Shot("13-journal-closed", () => Journal?.SetOpen(false));

        Shot("14-inventory-full", () =>
        {
            StageInventory();
            Character?.SetOpen(true);
            Character?.ShowGear();
        });

        Shot("15-shop", () =>
        {
            Character?.SetOpen(false);
            if (Player() is { } player && ShopDatabase.All.Count > 0)
            {
                EventBus.Instance?.Publish(new ShopOpenedEvent(player, ShopDatabase.All[0]));
            }
        });

        Shot("16-dialogue", () =>
        {
            Vendor?.SetOpen(false);
            if (Player() is { } player && DialogueFixture() is { } dialogue)
            {
                EventBus.Instance?.Publish(new DialogueStartedEvent(player, player, dialogue));
            }
        });

        Shot("17-dialogue-closed", () => Dialogue?.SetOpen(false));

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
    }

    private static PlayerCharacter? Player() =>
        ServiceLocator.Instance is { } locator && locator.TryGet(out PlayerCharacter player) ? player : null;

    private static void StageInventory()
    {
        if (Player()?.GetComponent<InventoryComponent>() is not { } pack)
        {
            return;
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
