using Embervale.Core.Events;
using Embervale.Core.Services;
using Embervale.Crafting;
using Embervale.Economy;
using Embervale.Enemies;
using Embervale.Housing;
using Embervale.Items;
using Embervale.Player;
using Embervale.UI;
using Godot;

namespace Embervale.Debugging;

/// <summary>
/// The rest of the screen-space UI, for spacing audits — <c>godot --path . -- --uishots</c>.
///
/// <see cref="PanelShots"/> and <see cref="HudShots"/> cover the map, journal, character sheet, shop,
/// dialogue and HUD. This one adds the panels they never opened (pause, spellbook, bestiary, crafting,
/// storage, contract board, appraisal, save slots, the character Progression tab) so a layout pass can be
/// compared before and after on every screen. Each state is driven through the panel's own entry point
/// or event, never by poking widgets. Set <c>EMBERVALE_RES</c> to shoot at another window size.
/// </summary>
public sealed partial class UiAuditShots : ShotHarness
{
    protected override string Flag => "--uishots";

    protected override string OutputDir => "user://uishots";

    private static PlayerCharacter? Player() =>
        ServiceLocator.Instance is { } locator && locator.TryGet(out PlayerCharacter player) ? player : null;

    private T? Find<T>() where T : Node => QuestShotFixtures.FindFirst<T>(GetTree().Root);

    protected override string? ValidateShotState(string name)
    {
        if (Player() is null)
        {
            return "player is not registered";
        }

        return name switch
        {
            "03-spellbook" when Find<SpellbookPanel>() is not { IsOpen: true } => "spellbook did not open",
            "04-bestiary" when Find<BestiaryPanel>() is not { IsOpen: true } => "bestiary did not open",
            "05-character-progression" when Find<InventoryPanel>() is not { IsOpen: true } => "character sheet did not open",
            "06-crafting" when Find<CraftingPanel>() is not { IsOpen: true } => "crafting did not open",
            "07-storage" when Find<StoragePanel>() is not { IsOpen: true } => "storage did not open",
            "08-contracts" when Find<ContractBoardPanel>() is not { IsOpen: true } => "contract board did not open",
            "09-appraisal" when Find<AppraisalPanel>() is not { IsOpen: true } => "appraisal did not open",
            _ => null,
        };
    }

    protected override void BuildShotList()
    {
        Shot("01-pause", () => Find<PauseMenu>()?.OpenForCapture());

        Shot("02-pause-closed", () => Find<PauseMenu>()?.CloseForCapture());

        Shot("03-spellbook", () =>
        {
            Find<SpellbookPanel>()?.SetOpen(true);
            Find<SpellbookPanel>()?.ShowSchool((int)Combat.DamageType.Fire);
        });

        // The last school: the ring list is the tallest thing on the page, so this frame shows whether it fits.
        Shot("03b-spellbook-necrotic", () => Find<SpellbookPanel>()?.ShowSchool((int)Combat.DamageType.Necrotic));

        Shot("04-bestiary", () =>
        {
            Find<SpellbookPanel>()?.SetOpen(false);
            RevealBestiary();
            Find<BestiaryPanel>()?.SetOpen(true);
        });

        Shot("05-character-progression", () =>
        {
            Find<BestiaryPanel>()?.SetOpen(false);
            Find<InventoryPanel>()?.SetOpen(true);
            if (Find<InventoryPanel>() is { } sheet)
            {
                QuestShotFixtures.FindFirst<UiTabs>(sheet)?.Select(1);
            }
        });

        Shot("06-crafting", () =>
        {
            Find<InventoryPanel>()?.SetOpen(false);
            if (Player() is { } player)
            {
                EventBus.Instance?.Publish(new CraftingStationOpenedEvent(player, CraftingStationType.Forge, "Forge"));
            }
        });

        Shot("07-storage", () =>
        {
            Find<CraftingPanel>()?.SetOpen(false);
            if (Player() is { } player && player.GetComponent<InventoryComponent>() is { } pack)
            {
                EventBus.Instance?.Publish(new StorageOpenedEvent(player, pack, "Chest"));
            }
        });

        Shot("08-contracts", () =>
        {
            Find<StoragePanel>()?.SetOpen(false);
            if (Player() is { } player)
            {
                EventBus.Instance?.Publish(new ContractBoardOpenedEvent(player, "Caravan board", 4, 3));
            }
        });

        Shot("09-appraisal", () =>
        {
            Find<ContractBoardPanel>()?.SetOpen(false);
            if (Player() is { } player)
            {
                EventBus.Instance?.Publish(new AppraisalOpenedEvent(player, "Appraiser"));
            }
        });

        Shot("10-save-slots", () =>
        {
            Find<AppraisalPanel>()?.SetOpen(false);
            var slots = new SaveSlotPanel();
            slots.Configure(SaveSlotPanel.Intent.Load, _ => { }, () => { });
            slots.Name = "AuditSaveSlots";
            GetTree().Root.AddChild(slots);
        });

        Shot("11-save-slots-closed", () => GetTree().Root.GetNodeOrNull("AuditSaveSlots")?.QueueFree());
    }

    /// <summary>Gives the bestiary a few discovered pages so its list is not the empty state.</summary>
    private static void RevealBestiary()
    {
        if (ServiceLocator.Instance is not { } locator || !locator.TryGet(out BestiaryService bestiary))
        {
            return;
        }

        var kills = new Godot.Collections.Dictionary();
        int shown = 0;
        foreach (BestiaryEntryResource entry in BestiaryDatabase.All)
        {
            kills[entry.Id] = entry.KillsToKnow + (shown % 3);
            if (++shown >= 14)
            {
                break;
            }
        }

        bestiary.Load(new Godot.Collections.Dictionary { ["kills"] = kills, ["ashen"] = new Godot.Collections.Dictionary() });
    }
}
