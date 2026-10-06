using System.Collections.Generic;
using Godot;

namespace Embervale.Items;

/// <summary>
/// A named group of equippables that pay out bonuses for wearing several at once. Authored as one
/// <c>.tres</c> per set under <c>data/item_sets/</c> and indexed by <see cref="ItemSetDatabase"/>.
/// Membership is declared twice on purpose: <see cref="PieceIds"/> here and
/// <see cref="ItemResource.SetId"/> on each piece, so either side can be read without the other and
/// the validator can report the two disagreeing.
/// </summary>
[GlobalClass]
public partial class ItemSetResource : Resource
{
    /// <summary>Stable id, e.g. "set.emberguard". The database key and what a piece's SetId names.</summary>
    [Export] public string Id { get; set; } = "set.unknown";

    /// <summary>Locale key of the set's name, conventionally <c>&lt;id&gt;.name</c>.</summary>
    [Export] public string DisplayNameKey { get; set; } = string.Empty;

    /// <summary>The <c>item.*</c> ids that count toward the set.</summary>
    [Export] public Godot.Collections.Array<string> PieceIds { get; set; } = new();

    /// <summary>Every bonus row, in any order; see <see cref="ItemSetBonusResource"/>.</summary>
    [Export] public Godot.Collections.Array<ItemSetBonusResource> Bonuses { get; set; } = new();

    /// <summary>The rows switched on by wearing <paramref name="equippedPieces"/> pieces.</summary>
    public IEnumerable<ItemSetBonusResource> ActiveBonuses(int equippedPieces)
    {
        foreach (ItemSetBonusResource bonus in Bonuses)
        {
            if (bonus != null && equippedPieces >= bonus.PiecesRequired)
            {
                yield return bonus;
            }
        }
    }

    public bool HasPiece(string itemId) => PieceIds.Contains(itemId);
}
