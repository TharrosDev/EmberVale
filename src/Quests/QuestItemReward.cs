using Godot;

namespace Embervale.Quests;

/// <summary>
/// A guaranteed item grant on quest completion: a quantity of an item resolved by id
/// through the <see cref="Embervale.Items.ItemDatabase"/>. Authored as a sub-resource
/// in a quest <c>.tres</c>.
/// </summary>
[GlobalClass]
public partial class QuestItemReward : Resource
{
    [Export] public string ItemId { get; set; } = string.Empty;
    [Export] public int Quantity { get; set; } = 1;
}
