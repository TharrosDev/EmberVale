using System;
using System.Diagnostics;
using Embervale.Core.Events;
using Embervale.Entities;
using Embervale.Items;
using Embervale.Stats;
using Godot;

namespace Embervale.Debugging;

/// <summary>Native regressions for inventory ownership and direct component lookup.</summary>
public partial class RuntimeAuditProbeDriver : Node
{
    public bool RunChecks()
    {
        var actor = new Entity { Name = "RuntimeAuditActor" };
        var stats = new StatsComponent { HealthRegen = 0f, StaminaRegen = 0f, ManaRegen = 0f };
        var inventory = new InventoryComponent();
        var second = new InventoryComponent();
        actor.AddChild(stats);
        actor.AddChild(inventory);
        actor.AddChild(second);
        AddChild(actor);

        bool passed = ReferenceEquals(actor.GetComponent<InventoryComponent>(), inventory);
        actor.RemoveChild(inventory);
        passed &= ReferenceEquals(actor.GetComponent<InventoryComponent>(), second);
        actor.AddChild(inventory);
        passed &= ReferenceEquals(actor.GetComponent<InventoryComponent>(), second);

        var potion = new ConsumableItemResource
        {
            Id = "item.audit.potion", DisplayName = "Audit potion", HealAmount = 10f, MaxStack = 10,
        };
        ItemInstance held = ItemInstance.Plain(potion);
        ItemInstance stale = ItemInstance.Plain(potion);
        inventory.AddInstance(held, 1);
        stats.SetCurrent(StatType.Health, 50f);
        passed &= !inventory.Consume(stale);
        passed &= stats.GetCurrent(StatType.Health) == 50f && inventory.CountOf(potion.Id) == 1;

        bool reentered = false;
        bool reused = false;
        Action<EntityHealedEvent> onHeal = e =>
        {
            if (!ReferenceEquals(e.Entity, actor) || reentered)
            {
                return;
            }
            reentered = true;
            reused = inventory.Consume(held);
        };
        EventBus.Instance.Subscribe(onHeal);
        try
        {
            passed &= inventory.Consume(held);
            passed &= reentered && !reused && inventory.CountOf(potion.Id) == 0;
            passed &= stats.GetCurrent(StatType.Health) == 60f;
        }
        finally
        {
            EventBus.Instance.Unsubscribe(onHeal);
        }

        inventory.AddInstance(held, 1);
        stats.SetCurrent(StatType.Health, 0f);
        passed &= !inventory.Consume(held) && inventory.CountOf(potion.Id) == 1;

        // Compare the previous GetChildren snapshot path with the actual production lookup.
        const int iterations = 10000;
        for (int i = 0; i < 1000; i++)
        {
            _ = SnapshotLookup(actor);
            _ = actor.GetComponent<StatsComponent>();
        }
        long before = GC.GetAllocatedBytesForCurrentThread();
        long start = Stopwatch.GetTimestamp();
        for (int i = 0; i < iterations; i++)
        {
            passed &= ReferenceEquals(SnapshotLookup(actor), stats);
        }
        double snapshotMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        long snapshotBytes = GC.GetAllocatedBytesForCurrentThread() - before;
        before = GC.GetAllocatedBytesForCurrentThread();
        start = Stopwatch.GetTimestamp();
        for (int i = 0; i < iterations; i++)
        {
            passed &= ReferenceEquals(actor.GetComponent<StatsComponent>(), stats);
        }
        double directMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        long directBytes = GC.GetAllocatedBytesForCurrentThread() - before;
        GD.Print($"RuntimeAudit lookup ({iterations} calls): snapshot={snapshotBytes} B/{snapshotMs:F2} ms; " +
                 $"direct={directBytes} B/{directMs:F2} ms");
        actor.Free();
        potion.Dispose();
        return passed;
    }

    private static StatsComponent? SnapshotLookup(Node host)
    {
        foreach (Node child in host.GetChildren())
        {
            if (child is StatsComponent stats)
            {
                return stats;
            }
        }
        return null;
    }
}
