using Embervale.Core;
using Embervale.Enemies;
using Embervale.Player;
using Embervale.UI;
using Embervale.World;
using Godot;

namespace Embervale.Bootstrap;

/// <summary>
/// Builds and holds the session's UI, and is the one place that knows which panels exist.
///
/// <para>Most panels are event-driven — one instance answers every merchant, container, appraiser
/// or board in the world — so once they are in the tree nothing calls back into them and they need
/// no field. A panel earns a field here only when something reads it: the HUD and the five panels
/// the capture harnesses drive, plus the two the player's components are injected into.</para>
///
/// <para><b>UI observes gameplay and sends intents; it is never the authority.</b> Everything here
/// is handed references by the session's build order — none of it reaches into the service registry
/// to find gameplay for itself.</para>
/// </summary>
public sealed partial class UICompositionRoot : Node
{
    public GameHud Hud { get; private set; } = null!;

    public InventoryPanel Inventory { get; private set; } = null!;

    public SpellbookPanel Spellbook { get; private set; } = null!;

    public HotbarPanel Hotbar { get; private set; } = null!;

    public QuestLogPanel QuestLog { get; private set; } = null!;

    public DialoguePanel Dialogue { get; private set; } = null!;

    public VendorPanel Vendor { get; private set; } = null!;

    public MapScreen Map { get; private set; } = null!;

    /// <summary>The always-on overlays: the HUD itself, toasts, the combat feedback flash, the
    /// pause menu and the loading screen.</summary>
    public void BuildShell()
    {
        Hud = new GameHud();
        AddChild(Hud);

        AddChild(new Notifications());
        AddChild(new CombatFeedbackOverlay());
        AddChild(new PauseMenu());
        AddChild(new LoadingScreen());
    }

    /// <summary>The modal and event-driven panels.</summary>
    public void BuildPanels()
    {
        Inventory = new InventoryPanel();
        AddChild(Inventory);

        Spellbook = new SpellbookPanel();
        AddChild(Spellbook);

        Hotbar = new HotbarPanel { Dock = Hud.BottomDock };
        AddChild(Hotbar);

        QuestLog = new QuestLogPanel();
        AddChild(QuestLog);

        Dialogue = new DialoguePanel();
        AddChild(Dialogue);

        Vendor = new VendorPanel();
        AddChild(Vendor);

        // No field: each of these is one instance answering an event from anywhere in the world,
        // and nothing ever calls back into it. A field that is assigned and never read is state
        // describing nothing.
        AddChild(new CraftingPanel());
        AddChild(new StoragePanel());
        AddChild(new AppraisalPanel());
        AddChild(new ContractBoardPanel());
    }

    public void BuildMapScreen(MapService mapService, FastTravelService fastTravel)
    {
        Map = new MapScreen();
        AddChild(Map);
        Map.SetMapService(mapService);
        Map.SetFastTravel(fastTravel);
    }

    public void BuildBestiaryPanel(BestiaryService bestiary)
    {
        var panel = new BestiaryPanel();
        AddChild(panel);
        panel.SetBestiary(bestiary);
    }

    public void SetClock(WorldClock clock) => Hud.SetClock(clock);

    public void SetWeather(WeatherDirector weather) => Hud.SetWeather(weather);

    public void SetWorldEvents(WorldEventDirector events) => Hud.SetWorldEvents(events);

    /// <summary>Wires the player's components into the panels that read them. Called once, by the
    /// player host, immediately after the actor is built.</summary>
    public void BindPlayer(PlayerCharacter player)
    {
        Hud.SetPlayer(player);

        Inventory.SetInventory(player.GetComponent<Items.InventoryComponent>());
        Inventory.SetEquipment(player.GetComponent<Items.EquipmentComponent>());
        Inventory.SetHotbar(player.GetComponent<Items.HotbarComponent>());
        Inventory.SetProgression(player.GetComponent<Progression.ProgressionComponent>());
        Inventory.SetPerks(player.GetComponent<Progression.PerksComponent>());
        Inventory.SetReputation(player.GetComponent<Factions.ReputationComponent>());
        Inventory.SetCorruption(player.GetComponent<Corruption.CorruptionComponent>());
        Inventory.SetStats(player.GetComponent<Stats.StatsComponent>());
        Inventory.SetStoryFlags(player.GetComponent<Dialogue.StoryFlagsComponent>());

        Hotbar.SetHotbar(player.GetComponent<Items.HotbarComponent>());
        Hotbar.SetInventory(player.GetComponent<Items.InventoryComponent>());

        Spellbook.SetSpellcasting(player.GetComponent<Magic.SpellcastingComponent>());
        Spellbook.SetProgression(player.GetComponent<Progression.ProgressionComponent>());

        QuestLog.SetQuestLog(player.GetComponent<Quests.QuestLogComponent>());
    }
}
