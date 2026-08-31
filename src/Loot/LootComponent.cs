using System.Collections.Generic;
using Embervale.Core.Diagnostics;
using Embervale.Core.Events;
using Embervale.Entities;
using Embervale.Items;
using Godot;

namespace Embervale.Loot;

/// <summary>
/// Drops procedurally generated loot when its owner dies. On the owner's
/// <see cref="EntityDiedEvent"/> it asks the <see cref="LootGenerator"/> to roll its
/// <see cref="Table"/> and spawns a world pickup for each resulting
/// <see cref="LootDrop"/>, scattered around the death position. This replaces
/// hard-coded death drops — give an actor a <see cref="LootTable"/> and it loots.
/// </summary>
[GlobalClass]
public partial class LootComponent : EntityComponent
{
    [Export] public LootTable? Table { get; set; }

    /// <summary>Optional path to load the table from when one isn't assigned
    /// directly (lets the factory wire it by resource path).</summary>
    [Export] public string TablePath { get; set; } = string.Empty;

    /// <summary>Per-actor luck added to the table's quality (e.g. elites drop better).</summary>
    [Export] public float QualityBonus { get; set; }

    private bool _dropped;

    protected override void OnInitialize()
    {
        if (Table == null && !string.IsNullOrEmpty(TablePath))
        {
            Table = GD.Load<LootTable>(TablePath);
            if (Table == null)
            {
                Log.Warn($"LootComponent on '{Entity?.DisplayName}' could not load loot table '{TablePath}'; it will drop nothing.");
            }
        }

        EventBus.Instance?.Subscribe<EntityDiedEvent>(OnEntityDied);
    }

    protected override void OnTeardown()
    {
        EventBus.Instance?.Unsubscribe<EntityDiedEvent>(OnEntityDied);
    }

    private void OnEntityDied(EntityDiedEvent e)
    {
        if (_dropped || Entity == null || !ReferenceEquals(e.Entity, Entity))
        {
            return;
        }

        _dropped = true;
        DropLoot();
    }

    private void DropLoot()
    {
        if (Table == null || Entity == null)
        {
            return;
        }

        Node? parent = ((Node)Entity.Body).GetParent();
        if (parent == null)
        {
            return;
        }

        List<LootDrop> drops = LootGenerator.Generate(Table, QualityBonus);
        if (drops.Count == 0)
        {
            return;
        }

        Vector3 origin = Entity.Body.GlobalPosition;
        int index = 0;
        foreach (LootDrop drop in drops)
        {
            Vector3 spot = ScatterAround(origin, index++);
            Entity pickup = ItemPickupFactory.Create(drop.Instance, drop.Quantity, spot);
            // Deferred: death is raised mid-damage; don't mutate the tree inline.
            parent.CallDeferred(Node.MethodName.AddChild, pickup);
        }

        Log.Info($"{Entity.DisplayName} dropped {drops.Count} item(s).");
    }

    /// <summary>Spirals drop positions outward from <paramref name="origin"/> so multiple items
    /// don't stack on one spot (shared with container looting and quest/event reward overflow).
    ///
    /// ⚠️ <b>The Y comes from the ground, not from zero.</b> It used to be a literal <c>0f</c>, which
    /// was every drop's correct height for exactly as long as the world was flat. On real terrain a
    /// kill on a terrace buried its loot and a kill in a basin left it floating out of reach —
    /// silently, because a pickup does not complain about where it is.</summary>
    internal static Vector3 ScatterAround(Vector3 origin, int index)
    {
        float angle = index * 2.39996f; // golden angle for an even spread
        float radius = 0.4f + (index * 0.25f);
        float x = origin.X + (Mathf.Cos(angle) * radius);
        float z = origin.Z + (Mathf.Sin(angle) * radius);
        return new Vector3(x, Embervale.World.WorldGround.HeightAt(x, z), z);
    }
}
