using System.Collections.Generic;
using Embervale.Items;
using Godot;

namespace Embervale.UI;

/// <summary>
/// The picture an item draws in a slot: its own authored <c>Icon</c> when it has one, otherwise its
/// archetype's cell on the painted item atlas (<see cref="ItemIconRules"/>), otherwise nothing, and
/// the slot keeps drawing the category glyph it always has.
///
/// The atlas is <c>atlas.png</c> plus <c>atlas.json</c> under <c>assets/ui/icons/items/</c>, written
/// by <c>tools/pack_ui_atlas.gd</c>. It is loaded on first use and its absence is not an error: a
/// checkout without the art, or a key the atlas has no cell for, falls through quietly. One
/// <see cref="AtlasTexture"/> is made per key and shared by every slot that shows it.
/// </summary>
public static class ItemIcons
{
    public const string AtlasPath = "res://assets/ui/icons/items/atlas.png";
    public const string AtlasIndexPath = "res://assets/ui/icons/items/atlas.json";

    private static readonly Dictionary<string, AtlasTexture?> Regions = new();
    private static Dictionary<string, (int Column, int Row)> _cells = new();
    private static Texture2D? _atlas;
    private static int _cell;
    private static bool _loaded;

    /// <summary>The icon for <paramref name="template"/>, or null to draw the category glyph.</summary>
    public static Texture2D? For(ItemResource? template)
    {
        if (template == null)
        {
            return null;
        }

        if (template.Icon is { } icon)
        {
            return icon;
        }

        EquipmentSlot slot = template is EquippableItemResource equippable ? equippable.Slot : EquipmentSlot.None;
        return ForKey(ItemIconRules.Key(template.Id, template.Type, slot));
    }

    /// <summary>The atlas cell for an archetype key, or null when the atlas has none.</summary>
    public static Texture2D? ForKey(string key)
    {
        EnsureLoaded();
        if (_atlas == null)
        {
            return null;
        }

        if (Regions.TryGetValue(key, out AtlasTexture? cached))
        {
            return cached;
        }

        AtlasTexture? region = null;
        if (_cells.TryGetValue(key, out (int Column, int Row) at))
        {
            region = new AtlasTexture
            {
                Atlas = _atlas,
                Region = new Rect2(at.Column * _cell, at.Row * _cell, _cell, _cell),
            };
        }

        Regions[key] = region;
        return region;
    }

    private static void EnsureLoaded()
    {
        if (_loaded)
        {
            return;
        }

        _loaded = true;
        if (!ResourceLoader.Exists(AtlasPath) || !FileAccess.FileExists(AtlasIndexPath))
        {
            return;
        }

        (int cell, Dictionary<string, (int Column, int Row)> cells) =
            ItemIconRules.ParseAtlas(FileAccess.GetFileAsString(AtlasIndexPath));
        if (cell <= 0 || cells.Count == 0)
        {
            return;
        }

        _atlas = GD.Load<Texture2D>(AtlasPath);
        _cell = cell;
        _cells = cells;
    }
}
