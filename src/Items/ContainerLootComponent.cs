using System.Collections.Generic;
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
/// The first-ever open also rolls **guaranteed legendary gear** (maintainer direction,
/// 2026-07-02): <see cref="MinLegendaryRolls"/>–<see cref="MaxLegendaryRolls"/> legendary
/// equippables generated at open time rather than seeded, so chests reward properly even on
/// saves created before the container had contents — and swaps the chest's visual to the
/// open-lid model. The looted flag persists via <see cref="ISaveable"/>, so a plundered chest
/// stays open and empty across save/load.
/// </summary>
[GlobalClass]
public partial class ContainerLootComponent : InteractableComponent, ISaveable
{
    private const string OpenModelPath = "res://assets/models/props/prp_cache_chest_open.glb";

    /// <summary>Guaranteed legendary equippables rolled on the first open (inclusive range).</summary>
    [Export] public int MinLegendaryRolls { get; set; } = 2;
    [Export] public int MaxLegendaryRolls { get; set; } = 3;

    private bool _looted;

    public string SaveId => SaveKey("container_loot");

    public override string Prompt =>
        Loc.TF("interact.loot", Entity?.DisplayName ?? Loc.T("interact.container"));

    protected override void OnInitialize()
    {
        RegisterSaveable();
    }

    protected override void OnTeardown()
    {
        SaveManager.Instance?.Unregister(this);
    }

    public override void Interact(IEntity instigator)
    {
        if (Entity?.Body is not Node3D body || body.GetParent() is not { } parent)
        {
            return;
        }

        Vector3 origin = body.GlobalPosition;
        int index = 0;

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
                    SpawnPickup(parent, stack.Instance, stack.Quantity, origin, index++);
                }
            }
        }

        // First-ever open: the guaranteed legendary haul, and the lid comes off.
        if (!_looted)
        {
            _looted = true;
            RollLegendaries(parent, origin, ref index);
            SwapToOpenVisual();
        }
    }

    private void RollLegendaries(Node parent, Vector3 origin, ref int index)
    {
        var candidates = new List<EquippableItemResource>();
        foreach (ItemResource item in ItemDatabase.All.Values)
        {
            if (item is EquippableItemResource equippable)
            {
                candidates.Add(equippable);
            }
        }

        if (candidates.Count == 0)
        {
            return;
        }

        var rng = new RandomNumberGenerator();
        int rolls = rng.RandiRange(MinLegendaryRolls, MaxLegendaryRolls);
        for (int i = 0; i < rolls; i++)
        {
            EquippableItemResource template = candidates[rng.RandiRange(0, candidates.Count - 1)];
            ItemInstance legendary = LootGenerator.RollAffixed(template, ItemRarity.Legendary, 0.9f);
            SpawnPickup(parent, legendary, 1, origin, index++);
            Log.Info($"The {Entity?.DisplayName ?? "container"} yields a legendary: {legendary.DisplayName}.");
        }
    }

    private static void SpawnPickup(Node parent, ItemInstance instance, int quantity, Vector3 origin, int index)
    {
        Entity pickup = ItemPickupFactory.Create(instance, quantity, LootComponent.ScatterAround(origin, index));
        // Deferred: Interact runs from the player's physics tick; don't mutate the tree inline.
        parent.CallDeferred(Node.MethodName.AddChild, pickup);
    }

    /// <summary>Replaces the chest's closed "Mesh" visual with the open-lid model.</summary>
    private void SwapToOpenVisual()
    {
        if (Entity?.Body is not Node3D body ||
            body.GetNodeOrNull<Node3D>("Mesh") is not { } closed ||
            GD.Load<PackedScene>(OpenModelPath)?.Instantiate() is not Node3D open)
        {
            return;
        }

        open.Name = "Mesh";
        closed.Name = "MeshClosed"; // free the node name before the replacement enters
        closed.QueueFree();
        body.AddChild(open);
    }

    // --- ISaveable ----------------------------------------------------------

    public Godot.Collections.Dictionary Save() => new() { ["looted"] = _looted };

    public void Load(Godot.Collections.Dictionary data)
    {
        _looted = data.TryGetValue("looted", out Variant looted) && looted.AsBool();
        if (_looted)
        {
            // Deferred: Load runs mid-restore, before it's safe to churn the visual subtree.
            Callable.From(SwapToOpenVisual).CallDeferred();
        }
    }
}
