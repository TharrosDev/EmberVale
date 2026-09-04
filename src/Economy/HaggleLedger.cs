using System.Collections.Generic;
using Embervale.Core.Diagnostics;
using Embervale.Core.Services;
using Embervale.Save;
using Godot;

namespace Embervale.Economy;

/// <summary>
/// Which merchants the player has already tried to talk down, and on which day (Phase 38S) — the
/// "merchant memory" half of the sub-phase. Shaped on <see cref="WagerLedger"/> and
/// <see cref="ContractLedger"/>: one node, registered with both the <see cref="ServiceLocator"/> and
/// the <see cref="SaveManager"/>, unregistered from both.
///
/// ⚠️ <b>THIS NODE IS THE ENTIRE BOUND, AND THE ARITHMETIC IS NOT.</b>
/// <see cref="HaggleRules.Succeeds"/> is a pure function of the day and the shop, so a reload replays
/// the same refusal — but nothing in that stops the player asking again five seconds later until the
/// standing hit is all they have left to lose. One attempt per merchant per day lives only here.
///
/// <b>The outcome is deliberately NOT stored, only the attempt.</b> A stored result is a second thing
/// that can disagree with the clock (38Q2's finding); re-deriving it costs one function call and
/// cannot drift. One row per shop: a different day answers "not yet", so nothing ticks, nothing
/// expires, and the dictionary stays the size of the number of haggling merchants in the realm.
/// </summary>
[GlobalClass]
public partial class HaggleLedger : Node, ISaveable
{
    public string SaveId => "haggles";

    /// <summary>Shop id → the last day the player tried their luck at that counter.</summary>
    private readonly Dictionary<string, int> _tried = new();

    /// <summary>Bumped on any change, matching <see cref="WagerLedger.Revision"/> and
    /// <see cref="ContractLedger.Revision"/>.</summary>
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

    /// <summary>Whether the player has already tried this merchant today. A stored row from another day
    /// answers false rather than being cleaned up, which is what makes the daily reset free.</summary>
    public bool TriedToday(string shopId, int day) =>
        _tried.TryGetValue(shopId, out int stored) && stored == day;

    /// <summary>Records the attempt. Returns false if one was already made today — the caller must not
    /// charge standing twice for one conversation, so the guard lives with the record rather than at the
    /// press, where a second call site could forget it.</summary>
    public bool TryTake(string shopId, int day)
    {
        if (string.IsNullOrEmpty(shopId) || TriedToday(shopId, day))
        {
            return false;
        }

        _tried[shopId] = day;
        Revision++;
        Log.Info($"Haggle at '{shopId}' attempted on day {day}.");

        return true;
    }

    public Godot.Collections.Dictionary Save()
    {
        var rows = new Godot.Collections.Dictionary();
        foreach (KeyValuePair<string, int> entry in _tried)
        {
            rows[entry.Key] = entry.Value;
        }

        return new Godot.Collections.Dictionary { { "tried", rows } };
    }

    public void Load(Godot.Collections.Dictionary data)
    {
        // Replaced, never merged (§7). A quickload onto a save taken BEFORE the attempt must give that
        // attempt back — otherwise a conversation had in a timeline being abandoned stays had in the one
        // being restored, and the player has lost a day's negotiation to a reload. Same rule as
        // WagerLedger.Load, same failure, and the empty-save case is the one that catches a merge.
        _tried.Clear();
        Revision++;

        if (!data.TryGetValue("tried", out Variant triedVariant) ||
            triedVariant.VariantType != Variant.Type.Dictionary)
        {
            return;
        }

        Godot.Collections.Dictionary rows = triedVariant.AsGodotDictionary();
        foreach (Variant key in rows.Keys)
        {
            string id = key.AsString();
            if (!string.IsNullOrEmpty(id) && rows[key].VariantType == Variant.Type.Int)
            {
                _tried[id] = rows[key].AsInt32();
            }
        }
    }
}
