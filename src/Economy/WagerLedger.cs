using System.Collections.Generic;
using Embervale.Core.Diagnostics;
using Embervale.Core.Services;
using Embervale.Save;
using Godot;

namespace Embervale.Economy;

/// <summary>
/// How many throws the player has taken at each gambling house, and on which day (Phase 38R2). Shaped
/// on <see cref="ContractLedger"/>: one node, registered with both the <see cref="ServiceLocator"/> and
/// the <see cref="SaveManager"/>, unregistered from both.
///
/// ⚠️ <b>THIS NODE IS THE ENTIRE BOUND ON THE GAME, AND THE ARITHMETIC IS NOT.</b>
/// <see cref="WagerRules.Won"/> is a pure function of the day, the throw number and the house, so a
/// reload replays the same result rather than rerolling it — but nothing in that stops a player
/// throwing a hundred times on one day. The day's allowance does, and it lives only here. If this is
/// ever bypassed, an authored house with a 90% payout is still a losing proposition and an unbounded
/// number of attempts is still a tap.
///
/// <b>One row per house: the day, and the throws taken on it.</b> A different day answers zero, so
/// nothing ticks, nothing expires and the dictionary stays the size of the number of houses in the
/// realm — the same lazy-on-read rule <c>ShopStockService</c>'s restock and
/// <see cref="ContractLedger"/>'s rotation follow.
/// </summary>
[GlobalClass]
public partial class WagerLedger : Node, ISaveable
{
    public string SaveId => "wagers";

    /// <summary>Service id → the last day played, and how many throws were taken that day.</summary>
    private readonly Dictionary<string, (int Day, int Plays)> _plays = new();

    /// <summary>Bumped on any change, matching <see cref="ContractLedger.Revision"/> and
    /// <c>ConsignmentLedger.Revision</c>. Nothing reads it yet — a wager has no window — but the shape
    /// is the one every other ledger here has and a UI phase will want it.</summary>
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

    /// <summary>Throws already taken at this house today. A stored row from another day answers zero
    /// rather than being cleaned up, which is what makes the daily reset free.</summary>
    public int PlaysToday(string houseId, int day) =>
        _plays.TryGetValue(houseId, out (int Day, int Plays) row) && row.Day == day ? row.Plays : 0;

    /// <summary>Records a throw and returns its 0-based index within the day — the number
    /// <see cref="WagerRules.Won"/> needs, so the caller cannot count it differently to the ledger.</summary>
    public int TakePlay(string houseId, int day)
    {
        if (string.IsNullOrEmpty(houseId))
        {
            return 0;
        }

        int index = PlaysToday(houseId, day);
        _plays[houseId] = (day, index + 1);
        Revision++;
        Log.Info($"Wager '{houseId}': throw {index + 1} taken on day {day}.");

        return index;
    }

    public Godot.Collections.Dictionary Save()
    {
        var rows = new Godot.Collections.Dictionary();
        foreach (KeyValuePair<string, (int Day, int Plays)> entry in _plays)
        {
            // Two ints in an array rather than a nested dictionary: the row has no names worth
            // persisting and this reads the same in a save file as it does here.
            rows[entry.Key] = new Godot.Collections.Array { entry.Value.Day, entry.Value.Plays };
        }

        return new Godot.Collections.Dictionary { { "plays", rows } };
    }

    public void Load(Godot.Collections.Dictionary data)
    {
        // Replaced, never merged (§7). A quickload onto a save taken before a throw must give that
        // throw BACK — otherwise an allowance spent in a timeline being abandoned stays spent in the
        // one being restored, and the player has lost a day's play to a reload. This is the same rule
        // ContractLedger.Load carries, and the same failure with the sign flipped.
        _plays.Clear();
        Revision++;

        if (!data.TryGetValue("plays", out Variant playsVariant) ||
            playsVariant.VariantType != Variant.Type.Dictionary)
        {
            return;
        }

        Godot.Collections.Dictionary rows = playsVariant.AsGodotDictionary();
        foreach (Variant key in rows.Keys)
        {
            string id = key.AsString();
            if (string.IsNullOrEmpty(id) || rows[key].VariantType != Variant.Type.Array)
            {
                continue;
            }

            Godot.Collections.Array row = rows[key].AsGodotArray();
            if (row.Count >= 2)
            {
                _plays[id] = (row[0].AsInt32(), row[1].AsInt32());
            }
        }
    }
}
