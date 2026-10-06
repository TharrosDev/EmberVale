using Embervale.Core.Events;
using Embervale.Core.Services;
using Embervale.Crafting;
using Embervale.Economy;
using Embervale.Enemies;
using Embervale.Housing;
using Embervale.Items;
using Embervale.Player;
using Embervale.Progression;
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
///
/// Shots 12 to 18 are the items and character pass: new pips, the detail card comparing against what
/// is worn (both views), the pack narrowed to an equipment slot, the perk tree's four node states,
/// and the Gear tab on a handheld-sized view. For the exact Steam Deck frame (853x533 logical) run the
/// whole list with <c>EMBERVALE_RES=1280x800</c> and <c>EMBERVALE_SHOT_UISCALE=1.5</c>.
/// </summary>
public sealed partial class UiAuditShots : ShotHarness
{
    protected override string Flag => "--uishots";

    protected override string OutputDir => "user://uishots";

    private static PlayerCharacter? Player() =>
        ServiceLocator.Instance is { } locator && locator.TryGet(out PlayerCharacter player) ? player : null;

    private T? Find<T>() where T : Node => QuestShotFixtures.FindFirst<T>(GetTree().Root);

    /// <summary>The slot <see cref="StageComparison"/> put a piece on, for the shots that follow it.</summary>
    private EquipmentSlot _wornSlot = EquipmentSlot.None;

    /// <summary>The view's content scale before the handheld shot changed it.</summary>
    private float _scaleBefore = 1f;

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
            _ when name.StartsWith("12-") || name.StartsWith("13") || name.StartsWith("14-") || name.StartsWith("15-")
                || name.StartsWith("16-") || name.StartsWith("17-") => ValidateItemsShot(name),
            _ => null,
        };
    }

    private string? ValidateItemsShot(string name)
    {
        if (Find<InventoryPanel>() is not { IsOpen: true } sheet)
        {
            return "character sheet did not open";
        }

        if (name.StartsWith("12-") && sheet.NewItemCount == 0)
        {
            return "nothing in the pack wears the new pip";
        }

        if (name.StartsWith("13"))
        {
            if (_wornSlot == EquipmentSlot.None)
            {
                return "no slot had one piece to wear and another to compare with it";
            }

            if (QuestShotFixtures.FindFirst<ItemDetailCard>(sheet) is not { CanCompare: true })
            {
                return "the detail card has nothing worn to compare against";
            }

            if (sheet.ShowingSideBySide != name.StartsWith("13b-"))
            {
                return sheet.ShowingSideBySide ? "the side-by-side view is showing" : "the side-by-side view is not showing";
            }
        }

        if (name.StartsWith("14-") && (_wornSlot == EquipmentSlot.None || sheet.EquipmentFocus != _wornSlot || sheet.ShownCellCount == 0))
        {
            return $"the pack is narrowed to {sheet.EquipmentFocus} with {sheet.ShownCellCount} cell(s), expected {_wornSlot} with at least one";
        }

        if (name.StartsWith("15-") && sheet.PerkTreeState.VisualsShown < 4)
        {
            return $"the perk tree drew {sheet.PerkTreeState.VisualsShown} of the four node states";
        }

        // The scale the shot set, not a width: an ultrawide window at 1.5 is still wider than a handheld.
        if (name.StartsWith("16-") && !Mathf.IsEqualApprox(GetTree().Root.ContentScaleFactor, 1.5f))
        {
            return $"the UI scale is {GetTree().Root.ContentScaleFactor:0.##}, not the handheld's 1.5";
        }

        return null;
    }

    protected override void BuildShotList()
    {
        Shot("01-pause", () =>
        {
            PanelShots.StageInventory();
            Find<PauseMenu>()?.OpenForCapture();
        });

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

        // The salvage page: rows with a yield chip line and a verb, which the craft page does not exercise.
        Shot("06b-crafting-salvage", () =>
        {
            if (Find<CraftingPanel>() is { } crafting)
            {
                QuestShotFixtures.FindFirst<UiTabs>(crafting)?.Select(1);
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

        // The foot of both lists, where the staged pack keeps its rolled pieces: rows carrying affix chips.
        Shot("07b-storage-scrolled", () => ScrollToEnd(Find<StoragePanel>()));

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

        // Loot that arrived while the pack was shut wears the new pip. The sheet was opened and closed in
        // shot 05, which made everything staged before it known, so this adds three more pieces first.
        Shot("12-inventory-new-pips", () =>
        {
            StageFreshLoot();
            Find<InventoryPanel>()?.SetOpen(true);
            Find<InventoryPanel>()?.ShowGear();
        });

        // The detail card measuring a pack piece against the one worn in its slot: the hero number's
        // delta, the arrowed stat rows and the "If equipped" block under the card.
        Shot("13-inventory-compare", () =>
        {
            StageComparison();
            Find<InventoryPanel>()?.CompareForCapture(sideBySide: false);
        });

        // The same card flipped to worn and candidate side by side, the state the compare input leaves.
        Shot("13b-inventory-compare-side-by-side", () => Find<InventoryPanel>()?.CompareForCapture(sideBySide: true));

        // An equipment cell focused: the pack lists only what fits that slot.
        Shot("14-inventory-slot-filter", () =>
        {
            ItemSlot.CompareOpen = false;
            Find<InventoryPanel>()?.FocusEquipmentSlotForCapture(_wornSlot);
        });

        // Locked, available, owned and maxed nodes in one frame, each with its own frame and mark.
        Shot("15-perk-states", () =>
        {
            StagePerkStates();
            Find<InventoryPanel>()?.ShowPerks(PerkBranch.Warrior);
        });

        // The Gear tab at a handheld's logical size: UI scale 1.5 on this window (853 px wide at 1280).
        Shot("16-inventory-handheld", () =>
        {
            _scaleBefore = GetTree().Root.ContentScaleFactor;
            GetTree().Root.ContentScaleFactor = 1.5f;
            Find<InventoryPanel>()?.CompareForCapture(sideBySide: false);
        });

        Shot("17-perks-handheld", () => Find<InventoryPanel>()?.ShowPerks(PerkBranch.Warrior));

        Shot("18-inventory-closed", () =>
        {
            GetTree().Root.ContentScaleFactor = _scaleBefore;
            Find<InventoryPanel>()?.SetOpen(false);
        });
    }

    /// <summary>Adds three rolled pieces to the pack, as a chest opened since the pack was last looked at would.</summary>
    private static void StageFreshLoot()
    {
        if (Player()?.GetComponent<InventoryComponent>() is not { } pack)
        {
            return;
        }

        int added = 0;
        foreach (ItemResource item in ItemDatabase.All.Values)
        {
            if (item is EquippableItemResource gear && pack.AddInstance(Loot.LootGenerator.RollAffixed(gear, ItemRarity.Rare), 1) > 0
                && ++added >= 3)
            {
                break;
            }
        }
    }

    /// <summary>Wears one piece from the first slot the pack holds two candidates for, through
    /// <see cref="EquipmentComponent.Equip"/>, so the other is a real comparison. Records the slot.</summary>
    private void StageComparison()
    {
        if (_wornSlot != EquipmentSlot.None || Player() is not { } player
            || player.GetComponent<InventoryComponent>() is not { } pack
            || player.GetComponent<EquipmentComponent>() is not { } worn)
        {
            return;
        }

        var bySlot = new System.Collections.Generic.Dictionary<EquipmentSlot, System.Collections.Generic.List<ItemInstance>>();
        foreach (ItemStack stack in pack.Stacks)
        {
            if (stack.Instance.Equippable is { } gear && gear.Slot != EquipmentSlot.None)
            {
                if (!bySlot.TryGetValue(gear.Slot, out System.Collections.Generic.List<ItemInstance>? pieces))
                {
                    bySlot[gear.Slot] = pieces = new System.Collections.Generic.List<ItemInstance>();
                }

                pieces.Add(stack.Instance);
            }
        }

        foreach ((EquipmentSlot slot, System.Collections.Generic.List<ItemInstance> pieces) in bySlot)
        {
            // Something already worn there (the starting kit) is comparison enough with one piece in the pack.
            if (worn.GetEquipped(slot) != null)
            {
                _wornSlot = slot;
                return;
            }

            if (pieces.Count < 2)
            {
                continue;
            }

            foreach (ItemInstance piece in pieces)
            {
                if (worn.CanEquip(piece) == EquipRefusal.None && worn.Equip(piece))
                {
                    _wornSlot = slot;
                    return;
                }
            }
        }
    }

    /// <summary>Puts the Warrior branch in a state that draws all four node visuals: a maxed perk, an owned
    /// one with ranks to go, open ones and tier-gated ones. Bought through <see cref="PerksComponent.Learn"/>.</summary>
    private static void StagePerkStates()
    {
        if (Player() is not { } player || player.GetComponent<PerksComponent>() is not { } perks)
        {
            return;
        }

        if (player.GetComponent<ProgressionComponent>() is { } progression && progression.SkillPoints < 12)
        {
            progression.RefundSkillPoints(12 - progression.SkillPoints);
        }

        foreach ((string id, int rank) in new[] { ("perk.might", 5), ("perk.toughness", 2) })
        {
            if (PerkDatabase.Get(id) is { } perk)
            {
                while (perks.RankOf(id) < rank && perks.Learn(perk))
                {
                }
            }
        }
    }

    /// <summary>Scrolls every list under <paramref name="root"/> to its end, so a capture can show the rows a
    /// first view hides.</summary>
    internal static void ScrollToEnd(Node? root)
    {
        if (root is null)
        {
            return;
        }

        foreach (Node child in root.GetChildren())
        {
            if (child is ScrollContainer scroll)
            {
                scroll.ScrollVertical = 100000;
            }

            ScrollToEnd(child);
        }
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
