using Embervale.Core.Services;
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
