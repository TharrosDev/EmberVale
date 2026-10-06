using System.Collections.Generic;
using Embervale.Core;
using Embervale.Core.Diagnostics;
using Embervale.Entities;
using Embervale.Interaction;
using Embervale.Localization;
using Embervale.Loot;
using Embervale.Save;
using Godot;

namespace Embervale.Items;

/// <summary>
/// Makes a container entity lootable (30L): interacting **pops the contents out as floor
/// pickups** scattered around the chest (the same spiral enemy drops use), so looting reads
/// physically — grab what you want with E, like any other ground loot.
///
/// The first-ever open also rolls the chest's <see cref="TablePath"/> through the
/// <see cref="LootGenerator"/>, for the realm it stands in and the character opening it (level,
/// loot-quality perks, bad-luck protection), then swaps the visual to the open-lid model. The roll
/// is seeded from the chest's persistent id and the save's salt, so reloading in front of a closed
/// chest does not reroll it. The looted flag and a non-default table persist via
/// <see cref="ISaveable"/>, so a plundered chest stays open and empty across save/load and a
/// boss's reward chest still knows which table it owes.
///
/// <b>The chest owns what it spilled until it is collected.</b> A floor pickup is transient and
/// saves nothing, while <c>looted</c> and a once-per-save claim in the loot ledger both do. A save
/// taken with the loot still on the ground used to load as an open, empty chest with the boss's
/// legendary gone for good. So every pickup this chest spawned is tracked, whatever is still lying
/// there is written under <c>spilled</c>, and a load hands it back on the next press.
/// </summary>
[GlobalClass]
public partial class ContainerLootComponent : InteractableComponent, ISaveable
{
    /// <summary>The table an ordinary cache rolls: level-appropriate gear and supplies for the
    /// realm it stands in.</summary>
    public const string DefaultTablePath = "res://data/loot/ChestLoot.tres";

    private const string OpenModelPath = ModelAssets.CacheChestOpen;
    private const string ClosedModelPath = ModelAssets.CacheChest;
    private const string LootedKey = "looted";
    private const string TableKey = "table";
    private const string SpilledKey = "spilled";

    /// <summary>The loot table rolled on the first open. Empty rolls nothing (the chest then only
    /// holds what its inventory was given).</summary>
    [Export] public string TablePath { get; set; } = DefaultTablePath;

    private bool _looted;
    private string _authoredTablePath = DefaultTablePath;
    private string _authoredName = string.Empty;

    /// <summary>The floor pickups this chest popped out that may still be lying there.</summary>
    private readonly List<ItemPickupComponent> _spilled = new();

    /// <summary>Spilled loot restored from a save, owed back on the next press.</summary>
    private readonly List<(ItemInstance Instance, int Quantity)> _pending = new();

    public string SaveId => SaveKey("container_loot");

    public override string Prompt =>
        Loc.TF("interact.loot", Entity?.DisplayName ?? Loc.T("interact.container"));

    /// <summary>True for a chest that rolls anything but the ordinary cache table: a reward chest.</summary>
    public bool IsRewardChest => TablePath.Length > 0 && TablePath != DefaultTablePath;

    protected override void OnInitialize()
    {
        _authoredTablePath = TablePath;
        _authoredName = Entity?.DisplayName ?? string.Empty;
        RegisterSaveable();
        ApplyName();
    }

    protected override void OnTeardown()
    {
        SaveManager.Instance?.Unregister(this);
    }

    /// <summary>Points a freshly spawned chest at the table it rolls when opened (a boss's reward
    /// chest). The path is saved, so the chest still owes that table after a load.</summary>
    public void Arm(string tablePath)
    {
        TablePath = tablePath;
        ApplyName();
    }

    public override bool Interact(IEntity instigator)
    {
        if (Entity?.Body is not Node3D body || body.GetParent() is not { } parent)
        {
            return false;
        }

        Vector3 origin = body.GlobalPosition;
        int index = 0;
        ItemRarity best = ItemRarity.Common;

        // Pop the container's contents out onto the floor. Snapshot the stacks — removing
        // while iterating would invalidate the list.
        //
        // Rolled items leave by reference, stackables by id — the same split StoragePanel.Transfer
        // makes, and for the same reason: RemoveItem matches on template id across every stack, so
        // two differently-affixed copies of one template are interchangeable to it. Draining the
        // whole container happened to come out even (every instance was popped, every stack was
        // removed, just not pairwise), but "correct because the counts cancel" is not a property to
        // leave load-bearing under a component that may one day pop only part of itself.
        if (Entity.GetComponent<InventoryComponent>() is { } source)
        {
            foreach (ItemStack stack in new List<ItemStack>(source.Stacks))
            {
                bool removed = stack.Instance.IsStackable
                    ? source.RemoveItem(stack.Instance.TemplateId, stack.Quantity)
                    : source.RemoveOneInstance(stack.Instance) != null;

                if (removed)
                {
                    SpawnPickup(parent, stack.Instance, stack.Quantity, origin, index++, ref best);
                }
            }
        }

        // Loot that was on the floor when the loaded save was written comes back out.
        foreach ((ItemInstance owed, int quantity) in _pending)
        {
            SpawnPickup(parent, owed, quantity, origin, index++, ref best);
        }

        _pending.Clear();

        // First-ever open: the table's roll, and the lid comes off.
        if (!_looted)
        {
            _looted = true;
            RollTable(parent, origin, instigator, ref index, ref best);
            SwapToOpenVisual();
        }

        if (index > 0)
        {
            LootComponent.AnnounceDrop(best, origin);
        }

        // Something came out, or the lid came off for the first time. An already-empty, already-open
        // container is a press against a prop and advances nothing.
        return index > 0;
    }

    private void RollTable(Node parent, Vector3 origin, IEntity instigator, ref int index, ref ItemRarity best)
    {
        if (TablePath.Length == 0)
        {
            return;
        }

        LootTable? table = ResidentResources.Load<LootTable>(TablePath);
        if (table == null)
        {
            Log.Warn($"Container '{Entity?.DisplayName}' could not load loot table '{TablePath}'; it yields only its contents.");
            return;
        }

        LootContext context = LootComponent.ContextFor(instigator);

        // Seeded from who this chest is, not from the clock: the same chest in the same save
        // always holds the same loot, however many times the player reloads in front of it.
        var rng = new RandomNumberGenerator
        {
            Seed = LootSeeds.For(Entity?.PersistentId, context.Ledger?.Salt ?? 0L),
        };

        foreach (LootDrop drop in LootGenerator.Generate(table, rng, context))
        {
            SpawnPickup(parent, drop.Instance, drop.Quantity, origin, index++, ref best);
            if (LootPresentation.IsAnnounced(drop.Instance.Rarity))
            {
                Log.Info($"The {Entity?.DisplayName ?? "container"} yields {drop.Instance.DisplayName}.");
            }
        }
    }

    private void SpawnPickup(Node parent, ItemInstance instance, int quantity, Vector3 origin, int index,
        ref ItemRarity best)
    {
        Entity pickup = ItemPickupFactory.Create(instance, quantity, LootComponent.ScatterAround(origin, index));
        if (pickup.GetNodeOrNull<ItemPickupComponent>("Pickup") is { } carried)
        {
            _spilled.RemoveAll(static p => !IsStillOnFloor(p));
            _spilled.Add(carried);
        }

        // Deferred: Interact runs from the player's physics tick; don't mutate the tree inline.
        parent.CallDeferred(Node.MethodName.AddChild, pickup);
        if (instance.Rarity > best)
        {
            best = instance.Rarity;
        }
    }

    /// <summary>True while a spilled pickup still holds something nobody has collected. A collected
    /// pickup is at zero before its node is freed, and one freed with its cell is no longer valid.</summary>
    private static bool IsStillOnFloor(ItemPickupComponent pickup) =>
        GodotObject.IsInstanceValid(pickup) && pickup.Quantity > 0 && pickup.Instance != null;

    /// <summary>Names the chest for what it is. The name is derived from the table rather than
    /// saved, so it is right again after a load rebuilds the chest from its template.</summary>
    private void ApplyName()
    {
        if (Entity is Entity entity)
        {
            entity.DisplayName = IsRewardChest ? Loc.T("loot.chest.reward") : _authoredName;
        }
    }

    /// <summary>Replaces the chest's closed "Mesh" visual with the open-lid model.</summary>
    private void SwapToOpenVisual() => SwapVisual(OpenModelPath);

    /// <summary>Puts the closed lid back — the load path, for a save taken before this chest was
    /// opened. Without it the mesh stayed open while <c>_looted</c> correctly went false, so the
    /// chest advertised itself as already plundered while still holding its contents.</summary>
    private void SwapToClosedVisual() => SwapVisual(ClosedModelPath);

    private void SwapVisual(string modelPath)
    {
        if (Entity?.Body is not Node3D body ||
            body.GetNodeOrNull<Node3D>("Mesh") is not { } current ||
            GD.Load<PackedScene>(modelPath)?.Instantiate() is not Node3D replacement)
        {
            return;
        }

        replacement.Name = "Mesh";
        current.Name = "MeshOld"; // free the node name before the replacement enters
        current.QueueFree();
        body.AddChild(replacement);
    }

    // --- ISaveable ----------------------------------------------------------

    public Godot.Collections.Dictionary Save()
    {
        var data = new Godot.Collections.Dictionary { [LootedKey] = _looted };

        // Written only when it differs from what the template builds, so an ordinary cache's entry
        // is exactly what it was before chests had tables.
        if (TablePath != _authoredTablePath)
        {
            data[TableKey] = TablePath;
        }

        // Additive, and written only while something is owed: loot still on the floor, plus loot a
        // load restored that has not been popped again. Same entry shape as an inventory stack.
        var spilled = new Godot.Collections.Array();
        foreach (ItemPickupComponent pickup in _spilled)
        {
            if (IsStillOnFloor(pickup))
            {
                spilled.Add(SpilledEntry(pickup.Instance!, pickup.Quantity));
            }
        }

        foreach ((ItemInstance owed, int quantity) in _pending)
        {
            spilled.Add(SpilledEntry(owed, quantity));
        }

        if (spilled.Count > 0)
        {
            data[SpilledKey] = spilled;
        }

        return data;
    }

    private static Godot.Collections.Dictionary SpilledEntry(ItemInstance instance, int quantity) => new()
    {
        ["instance"] = instance.Save(),
        ["qty"] = quantity,
    };

    public void Load(Godot.Collections.Dictionary data)
    {
        // Replace, never merge: on a quickload the pickups from the abandoned timeline are still
        // lying there, and leaving them would hand the same loot out twice. Absent key = nothing owed.
        foreach (ItemPickupComponent pickup in _spilled)
        {
            if (GodotObject.IsInstanceValid(pickup) && pickup.GetParent() is { } floorItem)
            {
                pickup.Quantity = 0;
                floorItem.QueueFree();
            }
        }

        _spilled.Clear();
        _pending.Clear();
        foreach (Variant entry in SaveRead.List(data, SpilledKey))
        {
            if (SaveRead.AsSection(entry) is not { } dict)
            {
                continue;
            }

            int qty = SaveRead.Int(dict, "qty", 1);
            if (qty > 0 && ItemInstance.FromSave(SaveRead.Section(dict, "instance")) is { } instance)
            {
                _pending.Add((instance, qty));
            }
        }

        bool wasLooted = _looted;
        _looted = data.TryGetValue(LootedKey, out Variant looted) && looted.AsBool();

        // Absent means "the table this chest was built with", never "whatever it was armed with in
        // the timeline being abandoned".
        TablePath = data.TryGetValue(TableKey, out Variant table) ? table.AsString() : _authoredTablePath;
        ApplyName();

        // Deferred either way: Load runs mid-restore, before it's safe to churn the visual subtree.
        // Both directions are handled — the second one used to be missing, so opening a chest and
        // then loading a save from before that left an open, empty-looking chest still full of loot.
        if (_looted && !wasLooted)
        {
            Callable.From(SwapToOpenVisual).CallDeferred();
        }
        else if (!_looted && wasLooted)
        {
            Callable.From(SwapToClosedVisual).CallDeferred();
        }
    }
}
