using Godot;

namespace Embervale.Economy;

/// <summary>
/// One supply contract the caravan board can post (Phase 38Q2), authored under <c>data/contracts/</c>
/// and indexed by <see cref="ContractDatabase"/>. Deliver <see cref="Quantity"/> of
/// <see cref="ItemId"/>, take <see cref="RewardGold"/> and some standing.
///
/// ⚠️ <b>A contract is NOT a quest and must never become one.</b> There is no objective, no journal
/// entry and no <c>QuestResource</c> anywhere near it: <c>QuestLogPanel</c> deliberately omits a
/// Contracts heading on the rule that "the journal shows the states the data actually has", and a
/// board that wrote to the log would put haulage jobs beside the story. The board's own window is the
/// whole of its UI, which is also why this resource carries no description, stages or turn-in node.
///
/// ⚠️ <b>The reward is deliberately allowed to beat what a shop pays, and <c>--validate</c> insists it
/// does.</b> A contract that paid less than the best buyer would be strictly worse than selling the
/// goods — correct, saved, validated and pointless. What stops the obvious loop (buy cheap, deliver
/// dear, repeat) is not the price but the <b>bound</b>: a posting can be filled once per rotation, and
/// <see cref="ContractLedger"/> is what remembers that. The bound is the feature; the generosity is
/// the point.
/// </summary>
[GlobalClass]
public partial class ContractResource : Resource
{
    /// <summary>Stable id, e.g. <c>contract.crossway.iron_ore</c>.</summary>
    [Export] public string Id { get; set; } = "contract.unknown";

    /// <summary>Player-facing headline. A <c>Loc</c> key — it reaches the board, and CLAUDE.md §6
    /// admits no literals there.</summary>
    [Export] public string NameKey { get; set; } = string.Empty;

    [ExportGroup("Wanted")]

    /// <summary>What the caravans want. ⚠️ Never an <c>ItemType.Quest</c> item — handing one over would
    /// strand a Collect objective with no way to recover it, the same refusal
    /// <see cref="ShopPricing.Sellable"/> makes at every counter. <c>--validate</c> enforces it.</summary>
    [Export] public string ItemId { get; set; } = string.Empty;

    /// <summary>How many. All or nothing: the board's button is disabled below this, because a partial
    /// delivery would need per-contract progress in saved state, which is the accepted-and-lapsing
    /// design 38Q2 deliberately did not take.</summary>
    [Export] public int Quantity { get; set; } = 1;

    [ExportGroup("Reward")]

    /// <summary>Gold paid on delivery, for the whole lot rather than per unit.</summary>
    [Export] public int RewardGold { get; set; }

    /// <summary>Whose standing moves (a <c>faction.*</c> id). Empty means gold only.</summary>
    [Export] public string FactionId { get; set; } = string.Empty;

    /// <summary>Standing earned. ⚠️ Authored without a <see cref="FactionId"/> it lands nowhere —
    /// 38M's "the cost would be charged to nobody" rule, from the reward side, and
    /// <c>--validate</c> catches it.</summary>
    [Export] public int ReputationDelta { get; set; }
}
