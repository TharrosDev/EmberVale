using System;
using System.Collections.Generic;
using Embervale.Core.Diagnostics;
using Embervale.Core.Services;
using Embervale.Save;
using Embervale.World;
using Godot;

namespace Embervale.Economy;

/// <summary>
/// Which settlements' trade is disturbed right now, and for how much longer (Phase 38T). One node,
/// registered with both the <see cref="ServiceLocator"/> and the <see cref="SaveManager"/> and
/// unregistered from both — shaped on <see cref="ShopStockService"/>, <see cref="HaggleLedger"/> and
/// <see cref="WagerLedger"/>.
///
/// ⚠️ <b>THE ROLL IS DERIVED AND THE WINDOW IS STORED, AND BOTH HALVES ARE LOAD-BEARING.</b>
/// <see cref="SupplyShockRules.Roll"/> is a pure function of (day, cell), so a quickload cannot shop
/// around for a better market — but the player can <see cref="Deliver"/> goods into a shortage and end
/// it early, and no clock can derive that. 38Q2's board needed only the first mechanism because a
/// posting is filled once by nature; this needs both, which is exactly 38R2's carried distinction.
///
/// <b>Rolled lazily on query, never on a tick</b> — <see cref="ShopStockService"/>'s restock rule and
/// for its reason: a shock begins because enough days had passed by the time the player asked a price,
/// and nothing in the game can observe the difference. <c>WorldEventDirector</c> is the cautionary case
/// the gate names: it ticks real seconds and is not <c>ISaveable</c>, so its state evaporates on reload.
/// </summary>
[GlobalClass]
public partial class SupplyShockService : Node, ISaveable
{
    public string SaveId => "shocks";

    /// <summary>Days of catch-up one query will roll. A save resumed a season later should not walk a
    /// thousand days of dice to arrive at the same "nothing is happening today" — the shocks it would
    /// have found all expired before the player got there.</summary>
    private const int MaxCatchUpDays = 14;

    private readonly List<SupplyShock> _active = new();

    /// <summary>Units hauled into each live shortage, keyed as <c>cell|tag</c>. Only the player's own
    /// deliveries — nothing derivable is kept here, which is the whole shape of this arc's ledgers.</summary>
    private readonly Dictionary<string, int> _delivered = new();

    private int _rolledThrough = int.MinValue;

    /// <summary>Bumped on any change, matching <see cref="HaggleLedger.Revision"/> — the vendor window
    /// and the board rebuild off it.</summary>
    public int Revision { get; private set; }

    public override void _EnterTree()
    {
        ServiceScope.RegisterOwned(this, this);
        SaveManager.Instance?.Register(this);
    }

    public override void _ExitTree()
    {
        SaveManager.Instance?.Unregister(this);
    }

    /// <summary>Every shock running today, across the realm — what the caravan board lists.</summary>
    public IReadOnlyList<SupplyShock> ActiveOn(int day)
    {
        EnsureRolled(day);

        var live = new List<SupplyShock>();
        foreach (SupplyShock shock in _active)
        {
            if (shock.ActiveOn(day))
            {
                live.Add(shock);
            }
        }

        return live;
    }

    /// <summary>The shock at one cell today, or <c>null</c>. At most one runs at a place at a time —
    /// two overlapping notices would be a place with an opinion rather than an event.</summary>
    public SupplyShock? At(string cellId, int day)
    {
        EnsureRolled(day);

        foreach (SupplyShock shock in _active)
        {
            if (shock.CellId == cellId && shock.ActiveOn(day))
            {
                return shock;
            }
        }

        return null;
    }

    /// <summary>
    /// The surplus and demand lists a cell wears today — its authored pair with any shock applied. The
    /// one function every price in the game reaches this feature through
    /// (<see cref="ShopResource.LocalValue"/>).
    /// </summary>
    public (List<string> Surplus, List<string> Demand) TagsFor(RegionCellResource cell, int day)
    {
        EnsureRolled(day);

        return SupplyShockRules.Apply(
            ShopResource.Plain(cell.Surplus),
            ShopResource.Plain(cell.Demand),
            ShopResource.Plain(cell.ShockTags),
            _active,
            day);
    }

    /// <summary>
    /// Goods have reached a shocked settlement (Phase 38T). Ends a shortage early once
    /// <see cref="SupplyShockRules.ReliefUnits"/> of the shocked kind have been sold into the cell.
    ///
    /// ⚠️ <b>A shortage only.</b> A glut cannot be relieved by hauling more of the same thing in, and a
    /// fair is a festival rather than a problem — a player who could "fix" either by selling into it
    /// would be paid twice for the same cart. Returns true when this delivery ended the shortage, so the
    /// caller can say so.
    /// </summary>
    public bool Deliver(string cellId, IReadOnlyList<string> itemTags, int units)
    {
        if (units <= 0 || string.IsNullOrEmpty(cellId) || itemTags.Count == 0)
        {
            return false;
        }

        for (int i = 0; i < _active.Count; i++)
        {
            SupplyShock shock = _active[i];
            if (shock.CellId != cellId)
            {
                continue;
            }

            string key = Key(shock);
            int hauled = _delivered.GetValueOrDefault(key, 0);

            if (!SupplyShockRules.Relieves(shock, itemTags, hauled, units, out bool breaks))
            {
                continue;
            }

            hauled += units;
            Revision++;

            if (!breaks)
            {
                _delivered[key] = hauled;
                return false;
            }

            _active.RemoveAt(i);
            _delivered.Remove(key);
            Log.Info($"Supply shock relieved at '{cellId}' ({shock.Tag}) — {hauled} units hauled in.");
            Core.Events.EventBus.Instance?.Publish(new SupplyShockRelievedEvent(cellId, shock.Tag));

            return true;
        }

        return false;
    }

    /// <summary>How far along the relief of a shortage is, for the notice on the board. Zero for
    /// anything that cannot be relieved.</summary>
    public int DeliveredTo(SupplyShock shock) =>
        shock.Kind == ShockKind.Shortage ? _delivered.GetValueOrDefault(Key(shock), 0) : 0;

    /// <summary>Starts a shock outright (dev console). Replaces whatever was running at that cell, so a
    /// second call is a change of scene rather than a stack.</summary>
    public void Force(string cellId, string tag, ShockKind kind, int day, int days)
    {
        EnsureRolled(day);
        Clear(cellId);
        _active.Add(new SupplyShock(cellId, tag, kind, day, Math.Max(1, days)));
        Revision++;
        Log.Info($"Supply shock forced at '{cellId}': {kind} of '{tag}' for {days} day(s).");
    }

    /// <summary>Ends whatever is running at a cell (dev console). True if there was something.</summary>
    public bool Clear(string cellId)
    {
        bool removed = false;
        for (int i = _active.Count - 1; i >= 0; i--)
        {
            if (_active[i].CellId == cellId)
            {
                _delivered.Remove(Key(_active[i]));
                _active.RemoveAt(i);
                removed = true;
            }
        }

        if (removed)
        {
            Revision++;
        }

        return removed;
    }

    /// <summary>
    /// Rolls every day that has passed since the last query and drops what has expired.
    ///
    /// ⚠️ <b>A rewound clock resets the cursor rather than freezing it</b> — the same case
    /// <see cref="ShopStock.IsRestockDue"/> and <see cref="ContractRules.Cycle"/> handle, and the dev
    /// console can rewind the day at will. Re-rolling a day already rolled is harmless because the roll
    /// is pure: it produces the shock that was there.
    /// </summary>
    private void EnsureRolled(int day)
    {
        if (_rolledThrough == day)
        {
            return;
        }

        if (day < _rolledThrough)
        {
            _rolledThrough = day - 1;
        }

        int from = _rolledThrough == int.MinValue ? day : Math.Max(_rolledThrough + 1, day - MaxCatchUpDays);
        for (int d = from; d <= day; d++)
        {
            RollDay(d);
        }

        _rolledThrough = day;

        // Expired windows are dropped rather than kept as history: nothing in the game asks what the
        // weather was, and a list that only grows would be saved forever.
        for (int i = _active.Count - 1; i >= 0; i--)
        {
            if (!_active[i].ActiveOn(day))
            {
                _delivered.Remove(Key(_active[i]));
                _active.RemoveAt(i);
                Revision++;
            }
        }
    }

    private void RollDay(int day)
    {
        foreach (RegionResource region in RegionDatabase.All)
        {
            foreach (RegionCellResource cell in region.Cells)
            {
                if (cell.ShockTags.Count == 0 || Running(cell.Id, day))
                {
                    continue;
                }

                SupplyShock? rolled = SupplyShockRules.Roll(
                    day,
                    cell.Id,
                    ShopResource.Plain(cell.ShockTags),
                    ShopResource.Plain(cell.Surplus),
                    ShopResource.Plain(cell.Demand));

                if (rolled is { } shock)
                {
                    _active.Add(shock);
                    Revision++;
                    Log.Info($"Supply shock at '{cell.Id}': {shock.Kind} of '{shock.Tag}' for {shock.Days} day(s) from day {day}.");
                }
            }
        }
    }

    private bool Running(string cellId, int day)
    {
        foreach (SupplyShock shock in _active)
        {
            if (shock.CellId == cellId && shock.ActiveOn(day))
            {
                return true;
            }
        }

        return false;
    }

    private static string Key(SupplyShock shock) => $"{shock.CellId}|{shock.Tag}";

    public Godot.Collections.Dictionary Save()
    {
        var rows = new Godot.Collections.Array();
        foreach (SupplyShock shock in _active)
        {
            rows.Add(new Godot.Collections.Dictionary
            {
                ["cell"] = shock.CellId,
                ["tag"] = shock.Tag,
                ["kind"] = (int)shock.Kind,
                ["start"] = shock.StartDay,
                ["days"] = shock.Days,
                ["hauled"] = _delivered.GetValueOrDefault(Key(shock), 0),
            });
        }

        return new Godot.Collections.Dictionary
        {
            ["shocks"] = rows,
            ["rolled"] = _rolledThrough,
        };
    }

    public void Load(Godot.Collections.Dictionary data)
    {
        // Replaced, never merged (§7). The empty-save case is the one that catches a merge, and here it
        // is sharper than in the other ledgers: a quickload onto a save taken before a shortage began
        // must put the cheap prices back, and the roll cursor with them — otherwise the shock survives
        // into a timeline it never happened in and, worse, `_rolledThrough` sitting in the future means
        // the day it began is never rolled again.
        _active.Clear();
        _delivered.Clear();
        _rolledThrough = int.MinValue;
        Revision++;

        if (data.TryGetValue("rolled", out Variant rolled) && rolled.VariantType == Variant.Type.Int)
        {
            _rolledThrough = rolled.AsInt32();
        }

        if (!data.TryGetValue("shocks", out Variant shocks) || shocks.VariantType != Variant.Type.Array)
        {
            return;
        }

        foreach (Variant element in shocks.AsGodotArray())
        {
            if (element.VariantType != Variant.Type.Dictionary)
            {
                continue;
            }

            Godot.Collections.Dictionary row = element.AsGodotDictionary();
            string cellId = row.TryGetValue("cell", out Variant cell) ? cell.AsString() : string.Empty;
            if (string.IsNullOrEmpty(cellId))
            {
                continue;
            }

            var shock = new SupplyShock(
                cellId,
                row.TryGetValue("tag", out Variant tag) ? tag.AsString() : string.Empty,
                row.TryGetValue("kind", out Variant kind) ? (ShockKind)kind.AsInt32() : ShockKind.Shortage,
                row.TryGetValue("start", out Variant start) ? start.AsInt32() : 0,
                row.TryGetValue("days", out Variant days) ? Math.Max(1, days.AsInt32()) : 1);

            _active.Add(shock);

            int hauled = row.TryGetValue("hauled", out Variant delivered) ? delivered.AsInt32() : 0;
            if (hauled > 0)
            {
                _delivered[Key(shock)] = hauled;
            }
        }
    }
}
