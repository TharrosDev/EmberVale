using Godot;

namespace Embervale.Items;

/// <summary>
/// Resource-driven definition of an item *template*. Designers author one
/// <c>.tres</c> per item under <c>data/items/</c>; the <see cref="ItemDatabase"/>
/// indexes them by <see cref="Id"/>. Runtime quantities live in an
/// <see cref="ItemStack"/>; per-instance rolled affixes arrive with loot
/// generation (Phase 7) as a separate item-instance layer over this template.
/// </summary>
[GlobalClass]
public partial class ItemResource : Resource
{
    /// <summary>Stable unique id, e.g. "item.potion.health". The save/database key.</summary>
    [Export] public string Id { get; set; } = "item.unknown";

    [Export] public string DisplayName { get; set; } = "Unknown Item";

    [Export(PropertyHint.MultilineText)]
    public string Description { get; set; } = string.Empty;

    [Export] public ItemType Type { get; set; } = ItemType.Misc;
    [Export] public ItemRarity Rarity { get; set; } = ItemRarity.Common;

    /// <summary>Max units in one stack. 1 = non-stackable (weapons, armor).</summary>
    [Export] public int MaxStack { get; set; } = 99;

    [Export] public float Weight { get; set; } = 0.1f;

    /// <summary>Base merchant value in gold.</summary>
    [Export] public int Value { get; set; } = 1;

    /// <summary>Optional inventory icon; UI falls back to text/rarity colour when null.</summary>
    [Export] public Texture2D? Icon { get; set; }

    /// <summary>
    /// The model this item shows in the world: hung on the wielder's socket by
    /// <c>EquipmentComponent</c> when equipped, dropped on the ground by <c>ItemPickupFactory</c>,
    /// and stood on a plinth by <c>TrophyStandComponent</c>.
    ///
    /// ⚠️ <b>It lives on the base template on purpose, and it used to live on
    /// <see cref="EquippableItemResource"/>.</b> One item has one appearance, and putting the path on
    /// the equippable subclass meant the two places that show an item the player is NOT wearing — the
    /// ground pickup and the trophy stand — could not reach it and drew a rarity-tinted primitive
    /// instead. A potion on the floor was a glowing cube.
    ///
    /// <b>Empty is a supported state, and it means something different per consumer.</b> Equipped, it
    /// leaves whatever is already in the hand, so an unauthored weapon degrades to the actor's default
    /// rather than to an empty fist; on the ground and on a plinth it falls back to the rarity-tinted
    /// shape, which still reads across a room.
    /// </summary>
    [Export] public string WorldModelPath { get; set; } = "";

    /// <summary>
    /// What trades deal in this (Phase 38F) — words from <see cref="Economy.TradeTags"/>, e.g.
    /// <c>metal</c> + <c>weapon</c> for a sword. A merchant's <c>AcceptedTags</c> and <c>Specialties</c>
    /// are matched against these, so this one field decides who will buy an item and who pays over the
    /// odds for it.
    ///
    /// ⚠️ <b>Not <see cref="ItemType"/>.</b> That enum's ordinals are persisted in every save, so
    /// appending to it is irreversible; tags are strings, carry no ordinal, and an item can wear several
    /// (a leather cap is <c>armor</c> and <c>leather</c>) which a single enum member could never say.
    ///
    /// <b>Empty means every merchant takes it.</b> A new item is never silently unsellable while its
    /// trade is still being decided — see <see cref="Economy.TradeTags.Accepts"/> for why both empties
    /// fail open.
    /// </summary>
    [Export] public Godot.Collections.Array<string> TradeTags { get; set; } = new();

    public bool IsStackable => MaxStack > 1;

    /// <summary>The tags as a plain list, for the Godot-free <see cref="Economy.TradeTags"/> helpers
    /// (the test project cannot construct a <c>Godot.Collections.Array</c>).</summary>
    public System.Collections.Generic.List<string> TagList()
    {
        var list = new System.Collections.Generic.List<string>();
        foreach (string tag in TradeTags)
        {
            if (!string.IsNullOrEmpty(tag))
            {
                list.Add(tag);
            }
        }

        return list;
    }
}
