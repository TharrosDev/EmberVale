using Embervale.Core.Events;
using Embervale.Core.Services;
using Embervale.Crafting;
using Embervale.Economy;
using Embervale.Housing;
using Embervale.Items;
using Embervale.Player;
using Embervale.UI;
using Godot;

namespace Embervale.Debugging;

/// <summary>
/// Rendered coverage of the trade screens: vendor, crafting, storage, appraisal and the contract
/// board. <c>godot --path . -- --tradeshots</c> continues the most recent save, like the other
/// session harnesses. Run WITHOUT <c>--headless</c>.
///
/// Each panel is opened through the event its interactable publishes, the way
/// <see cref="UiAuditShots"/> opens them, and each further state through the panel's own capture
/// hook. For the exact Steam Deck frame (853x533 logical) run the whole list with
/// <c>EMBERVALE_RES=1280x800</c> and <c>EMBERVALE_SHOT_UISCALE=1.5</c>; the last three shots set
/// that scale themselves on whatever window they are given.
/// </summary>
public sealed partial class TradeShots : ShotHarness
{
    protected override string Flag => "--tradeshots";

    protected override string OutputDir => "user://tradeshots";

    /// <summary>The view's content scale before the handheld shots changed it.</summary>
    private float _scaleBefore = 1f;

    /// <summary>Whether the staging found a ware or pack piece with something worn to compare against.</summary>
    private bool _compareStaged;

    private static PlayerCharacter? Player() =>
        ServiceLocator.Instance is { } locator && locator.TryGet(out PlayerCharacter player) ? player : null;

    private T? Find<T>() where T : Node => QuestShotFixtures.FindFirst<T>(GetTree().Root);

    protected override string? ValidateShotState(string name)
    {
        if (Player() is null)
        {
            return "player is not registered";
        }

        if (name.StartsWith("01") || name.StartsWith("02") || name.StartsWith("03") || name.StartsWith("10-"))
        {
            return ValidateVendor(name);
        }

        if (name.StartsWith("04") || name.StartsWith("05-") || name.StartsWith("06-") || name.StartsWith("11-"))
        {
            return ValidateCrafting(name);
        }

        if (name.StartsWith("07-") || name.StartsWith("12-"))
        {
            return Find<StoragePanel>() is not { IsOpen: true } storage ? "storage did not open"
                : storage.ShownRowCount == 0 ? "the stash drew no rows"
                : null;
        }

        if (name.StartsWith("08-"))
        {
            return Find<ContractBoardPanel>() is not { IsOpen: true } board ? "contract board did not open"
                : board.PostingCount == 0 ? "the board drew no postings"
                : null;
        }

        if (name.StartsWith("09-"))
        {
            return Find<AppraisalPanel>() is not { IsOpen: true } appraisal ? "appraisal did not open"
                : appraisal.ShownRowCount == 0 ? "the appraiser valued nothing"
                : null;
        }

        return null;
    }

    private string? ValidateVendor(string name)
    {
        if (Find<VendorPanel>() is not { IsOpen: true } vendor)
        {
            return "vendor did not open";
        }

        if (name.StartsWith("02"))
        {
            if (!_compareStaged)
            {
                return "nothing on the counter or in the pack had a worn piece to compare against";
            }

            if (QuestShotFixtures.FindFirst<ItemDetailCard>(vendor) is not { CanCompare: true } card)
            {
                return "the detail card has nothing worn to compare against";
            }

            if (card.ShowingSideBySide != name.StartsWith("02b-"))
            {
                return card.ShowingSideBySide ? "the side-by-side view is showing" : "the side-by-side view is not showing";
            }
        }

        if (name.StartsWith("03-") && vendor.JunkConfirmTotal <= 0)
        {
            return "the junk confirm is not naming a total";
        }

        return name.StartsWith("10-") ? ValidateHandheld() : null;
    }

    private string? ValidateCrafting(string name)
    {
        if (Find<CraftingPanel>() is not { IsOpen: true } crafting)
        {
            return "crafting did not open";
        }

        if ((name.StartsWith("04") || name.StartsWith("11-")) && !crafting.ShowingThreeColumns)
        {
            return $"the Craft page drew {crafting.ShownRecipeCount} recipe(s) and no ingredient or result column";
        }

        if (name.StartsWith("05-") && crafting.Mode != 2)
        {
            return $"the open page is {crafting.Mode}, not Salvage";
        }

        if (name.StartsWith("06-") && crafting.Mode != 1)
        {
            return $"the open page is {crafting.Mode}, not Reforge";
        }

        return name.StartsWith("11-") ? ValidateHandheld() : null;
    }

    // The scale the shot set, not a width: an ultrawide window at 1.5 is still wider than a handheld.
    private string? ValidateHandheld() =>
        Mathf.IsEqualApprox(GetTree().Root.ContentScaleFactor, 1.5f)
            ? null
            : $"the UI scale is {GetTree().Root.ContentScaleFactor:0.##}, not the handheld's 1.5";

    protected override void BuildShotList()
    {
        // A counter at density: the staged pack (rolled pieces, stock, materials) against the first shop.
        Shot("01-vendor", () =>
        {
            PanelShots.StageInventory();
            StageJunk();
            OpenShop();
        });

        // A ware (or a pack piece) inspected with something worn in its slot: the card's deltas.
        Shot("02-vendor-compare", () =>
        {
            StageWorn();
            _compareStaged = Find<VendorPanel>()?.CompareForCapture(sideBySide: false) ?? false;
        });

        // The same card flipped to worn and candidate side by side.
        Shot("02b-vendor-compare-side-by-side", () => Find<VendorPanel>()?.CompareForCapture(sideBySide: true));

        // "Sell all junk" pressed once: the confirm that names the total, with Cancel beside it.
        Shot("03-vendor-junk-confirm", () =>
        {
            ItemSlot.CompareOpen = false;
            Find<VendorPanel>()?.ArmJunkSaleForCapture();
        });

        // Recipes, the chosen recipe's ingredients with their marks, and the result card.
        Shot("04-crafting", () =>
        {
            Find<VendorPanel>()?.SetOpen(false);
            if (Player() is { } player)
            {
                EventBus.Instance?.Publish(new CraftingStationOpenedEvent(player, CraftingStationType.Forge, "Forge"));
            }
        });

        // The foot of the recipe list, where the recipes still to be learned sit under their rule.
        Shot("04b-crafting-scrolled", () => UiAuditShots.ScrollToEnd(Find<CraftingPanel>()));

        Shot("05-crafting-salvage", () => Find<CraftingPanel>()?.ShowModeForCapture(2));

        Shot("06-crafting-reforge", () => Find<CraftingPanel>()?.ShowModeForCapture(1));

        Shot("07-storage", () =>
        {
            Find<CraftingPanel>()?.SetOpen(false);
            OpenStorage();
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

        // The three full-screen pages at a handheld's logical size: UI scale 1.5 on this window
        // (853 px wide at 1280). Lists must scroll and the frame must not.
        Shot("10-vendor-handheld", () =>
        {
            Find<AppraisalPanel>()?.SetOpen(false);
            _scaleBefore = GetTree().Root.ContentScaleFactor;
            GetTree().Root.ContentScaleFactor = 1.5f;
            OpenShop();
        });

        Shot("11-crafting-handheld", () =>
        {
            Find<VendorPanel>()?.SetOpen(false);
            if (Player() is { } player)
            {
                EventBus.Instance?.Publish(new CraftingStationOpenedEvent(player, CraftingStationType.Forge, "Forge"));
            }
        });

        Shot("12-storage-handheld", () =>
        {
            Find<CraftingPanel>()?.SetOpen(false);
            OpenStorage();
        });

        Shot("13-closed", () =>
        {
            Find<StoragePanel>()?.SetOpen(false);
            GetTree().Root.ContentScaleFactor = _scaleBefore;
        });
    }

    private static void OpenShop()
    {
        if (Player() is { } player && ShopDatabase.All.Count > 0)
        {
            EventBus.Instance?.Publish(new ShopOpenedEvent(player, ShopDatabase.All[0]));
        }
    }

    /// <summary>The player's own pack as the chest, as <see cref="UiAuditShots"/> does: both lists full.</summary>
    private static void OpenStorage()
    {
        if (Player() is { } player && player.GetComponent<InventoryComponent>() is { } pack)
        {
            EventBus.Instance?.Publish(new StorageOpenedEvent(player, pack, "Chest"));
        }
    }

    /// <summary>
    /// Gives the counter a junk line that sells. ⚠️ The first shop is a herbalist, and the staged pack
    /// holds nothing she deals in: marking its first plain stacks left "None of your junk will sell
    /// here" on screen and the confirm shot with no total to name. So a few of the shop's own cheap
    /// wares go into the pack first (a merchant always deals in what she stocks) and are marked,
    /// through <see cref="InventoryComponent.SetJunk"/>; two stacks she will not take are marked as
    /// well, so the "more will not sell here" line is photographed too.
    /// </summary>
    private static void StageJunk()
    {
        if (Player()?.GetComponent<InventoryComponent>() is not { } pack || ShopDatabase.All.Count == 0)
        {
            return;
        }

        ShopResource shop = ShopDatabase.All[0];
        System.Collections.Generic.List<string> accepted = shop.AcceptedTagList();
        int staged = 0;
        foreach (ShopStockEntry entry in shop.StockList())
        {
            if (ItemDatabase.Get(entry.ItemId) is not { } item || item is EquippableItemResource || !item.IsStackable
                || !ShopPricing.Sellable(item.Type, isCurrency: false) || !TradeTags.Accepts(item.TagList(), accepted)
                || pack.AddItem(item, 3) == 0)
            {
                continue;
            }

            foreach (ItemStack stack in new System.Collections.Generic.List<ItemStack>(pack.AllStacks))
            {
                if (stack.Instance.TemplateId == item.Id && !stack.Instance.Locked)
                {
                    pack.SetJunk(stack.Instance, true);
                }
            }

            if (++staged >= 2)
            {
                break;
            }
        }

        int refused = 0;
        foreach (ItemStack stack in new System.Collections.Generic.List<ItemStack>(pack.Stacks))
        {
            ItemInstance instance = stack.Instance;
            if (!instance.IsEquippable && !instance.Locked && !instance.Junk
                && !TradeTags.Accepts(instance.Template.TagList(), accepted)
                && pack.SetJunk(instance, true) && ++refused >= 2)
            {
                break;
            }
        }
    }

    /// <summary>Wears the first pack piece that will go on, through <see cref="EquipmentComponent.Equip"/>,
    /// so another piece for that slot is a real comparison.</summary>
    private static void StageWorn()
    {
        if (Player() is not { } player || player.GetComponent<InventoryComponent>() is not { } pack
            || player.GetComponent<EquipmentComponent>() is not { } worn)
        {
            return;
        }

        var seen = new System.Collections.Generic.HashSet<EquipmentSlot>();
        foreach (ItemStack stack in new System.Collections.Generic.List<ItemStack>(pack.Stacks))
        {
            if (stack.Instance.Equippable is not { } gear || gear.Slot == EquipmentSlot.None)
            {
                continue;
            }

            // The second piece seen for a slot is the one to wear: the first stays in the pack to be compared.
            if (worn.GetEquipped(gear.Slot) == null && !seen.Add(gear.Slot)
                && worn.CanEquip(stack.Instance) == EquipRefusal.None && worn.Equip(stack.Instance))
            {
                return;
            }
        }
    }
}
