using System.Collections.Generic;
using Embervale.Core.Diagnostics;
using Embervale.Core.Services;
using Embervale.Save;
using Godot;

namespace Embervale.Economy;

/// <summary>
/// Which supply contracts have already been filled, and in which rotation (Phase 38Q2). Shaped on
/// <see cref="ConsignmentLedger"/>: one node, registered with both the <see cref="ServiceLocator"/>
/// and the <see cref="SaveManager"/>, unregistered from both.
///
/// ⚠️ <b>IT RECORDS WHAT WAS FILLED AND NEVER WHAT IS OFFERED, AND THAT ASYMMETRY IS THE DESIGN.</b>
/// The board itself is derived from the day by <see cref="ContractRules.SlotContract"/>, so there is
/// nothing about the offer to persist and a quickload cannot reroll it. Only the player's own act of
/// delivering is state, and it is one integer per contract.
///
/// <b>This is also the whole of what stops a money printer.</b> A posting deliberately pays more than
/// a shop would, so buy-cheap-deliver-dear is a real trade — bounded, and bounded only here, by a
/// posting being fillable once per rotation. If this node is ever bypassed, that bound is gone.
///
/// <b>Nothing ticks.</b> A rotation turns because the day moved on by the time the player walked up to
/// the board — the same lazy-on-read rule <see cref="ShopStockService"/>'s restock and
/// <see cref="ConsignmentLedger"/>'s maturation follow.
///
/// ponytail: filled entries are pruned to the current and previous cycle on write, so the dictionary
/// stays the size of the pool rather than growing with playtime. Keep a full history if a later phase
/// wants to show how much haulage the player has done.
/// </summary>
[GlobalClass]
public partial class ContractLedger : Node, ISaveable
{
    public string SaveId => "contracts";

    /// <summary>Contract id → the cycle it was filled in.</summary>
    private readonly Dictionary<string, int> _filled = new();

    /// <summary>Bumped on any change so the board window knows to rebuild — the same signal
    /// <see cref="ConsignmentLedger.Revision"/> and <c>ShopStockService.Revision</c> give.</summary>
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

    /// <summary>Whether this posting has already been filled in this rotation. Read by both the board's
    /// row state and the delivery itself, so what the window shows and what the press does cannot
    /// drift — the rule <c>PropertyDeedComponent</c> set for a deed.</summary>
    public bool Filled(string contractId, int cycle) =>
        _filled.TryGetValue(contractId, out int filledCycle) && filledCycle == cycle;

    /// <summary>The cycle a posting was filled in, or <c>int.MinValue</c> if never — for the board's
    /// greyed row, which names the rotation rather than only refusing.</summary>
    public int FilledCycle(string contractId) =>
        _filled.TryGetValue(contractId, out int cycle) ? cycle : int.MinValue;

    public void MarkFilled(string contractId, int cycle)
    {
        if (string.IsNullOrEmpty(contractId))
        {
            return;
        }

        _filled[contractId] = cycle;
        Prune(cycle);
        Revision++;
        Log.Info($"Contract '{contractId}' filled in rotation {cycle}.");
    }

    /// <summary>Drops records from rotations that can no longer be current. A filled entry only ever
    /// answers "is this the cycle we are in", so anything older is dead weight — and the previous cycle
    /// is kept so a clock nudged backwards over a boundary does not un-fill a delivery already paid
    /// for.</summary>
    private void Prune(int cycle)
    {
        var stale = new List<string>();
        foreach (KeyValuePair<string, int> entry in _filled)
        {
            if (entry.Value < cycle - 1)
            {
                stale.Add(entry.Key);
            }
        }

        foreach (string id in stale)
        {
            _filled.Remove(id);
        }
    }

    public Godot.Collections.Dictionary Save()
    {
        var filled = new Godot.Collections.Dictionary();
        foreach (KeyValuePair<string, int> entry in _filled)
        {
            filled[entry.Key] = entry.Value;
        }

        return new Godot.Collections.Dictionary { { "filled", filled } };
    }

    public void Load(Godot.Collections.Dictionary data)
    {
        // Replaced, never merged (§7). A quickload onto a save taken before a delivery must UN-fill
        // that posting — otherwise a contract filled in a timeline being abandoned stays filled in the
        // one being restored, and the player has lost a rotation's reward to a reload.
        _filled.Clear();
        Revision++;

        if (!data.TryGetValue("filled", out Variant filledVariant) ||
            filledVariant.VariantType != Variant.Type.Dictionary)
        {
            return;
        }

        Godot.Collections.Dictionary rows = filledVariant.AsGodotDictionary();
        foreach (Variant key in rows.Keys)
        {
            string id = key.AsString();
            if (!string.IsNullOrEmpty(id))
            {
                _filled[id] = rows[key].AsInt32();
            }
        }
    }
}
