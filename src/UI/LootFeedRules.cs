using System.Collections.Generic;
using Embervale.Core.Events;
using Embervale.Entities;
using Embervale.Items;

namespace Embervale.UI;

/// <summary>One line of the pickup feed: an item, how many, and the rarity that colours it.</summary>
public readonly record struct LootFeedLine(string ItemId, string Name, int Rarity, int Quantity);

/// <summary>
/// Folds a burst of pickups into one line per item. Sweeping a corpse pile raises a pickup event
/// per stack, and holding the loot key raises several a second; one toast each would bury the feed
/// under "Iron Ore" five times. A line is held for <see cref="Window"/> seconds after its last
/// pickup, growing its count, and is released no later than <see cref="MaxHold"/> after its first,
/// so a steady trickle still reports instead of waiting forever. Pure: the feed supplies the clock.
/// </summary>
public sealed class LootFeedMerger
{
    /// <summary>Quiet time after the last pickup of an item before its line is released.</summary>
    public const double Window = 0.45;

    /// <summary>The longest a line is held from its first pickup, however busy the stream.</summary>
    public const double MaxHold = 1.5;

    private sealed class Pending
    {
        public required string Name { get; init; }
        public required int Rarity { get; init; }
        public required double First { get; init; }
        public double Last { get; set; }
        public int Quantity { get; set; }
    }

    // Insertion-ordered, so lines leave in the order the player picked the items up.
    private readonly List<string> _order = new();
    private readonly Dictionary<string, Pending> _pending = new();

    public bool HasPending => _order.Count > 0;

    public void Add(string itemId, string name, int rarity, int quantity, double now)
    {
        if (quantity <= 0 || string.IsNullOrEmpty(itemId))
        {
            return;
        }

        if (_pending.TryGetValue(itemId, out Pending? held))
        {
            held.Quantity += quantity;
            held.Last = now;
            return;
        }

        _pending[itemId] = new Pending { Name = name, Rarity = rarity, First = now, Last = now, Quantity = quantity };
        _order.Add(itemId);
    }

    /// <summary>The lines whose hold has run out at <paramref name="now"/>, oldest first.</summary>
    public List<LootFeedLine> Flush(double now)
    {
        var ready = new List<LootFeedLine>();
        for (int i = 0; i < _order.Count;)
        {
            string id = _order[i];
            Pending held = _pending[id];
            if (now - held.Last >= Window || now - held.First >= MaxHold)
            {
                ready.Add(new LootFeedLine(id, held.Name, held.Rarity, held.Quantity));
                _pending.Remove(id);
                _order.RemoveAt(i);
            }
            else
            {
                i++;
            }
        }

        return ready;
    }

    public void Clear()
    {
        _order.Clear();
        _pending.Clear();
    }
}
