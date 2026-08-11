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

        Shot("08-journal-closed", () => Journal?.SetOpen(false));
    }

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

    /// <summary>Starts the first startable quest so the journal has a card to draw.</summary>
    private static void StartAQuest()
    {
        if (ServiceLocator.Instance is not { } locator ||
            !locator.TryGet(out PlayerCharacter player) ||
            player.GetComponent<QuestLogComponent>() is not { } log)
        {
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
