using System.Collections.Generic;
using Godot;

namespace Embervale.UI;

/// <summary>
/// Embervale's small functional glyph family. The SVGs share a 24 px grid, rounded 1.8 px strokes,
/// and a bone-pale source colour; callers tint semantic meaning through <see cref="CanvasItem.Modulate"/>.
/// Keeping the mapping here prevents screens from falling back to Unicode stand-ins.
/// </summary>
public static class UiIcon
{
    public enum Kind
    {
        Health,
        Stamina,
        Mana,
        Currency,
        Quest,
        Waypoint,
        Settlement,
        Service,
        Travel,
        Lock,
        Inventory,
        Weapon,
        Spell,
        Warning,
        Sun,
        Moon,
        Consumable,
        Armor,
        Material,
        Misc,
    }

    private static readonly Dictionary<Kind, Texture2D?> Cache = new();

    public static TextureRect Create(Kind kind, float size = 20f, Color? tint = null)
    {
        var icon = new TextureRect
        {
            CustomMinimumSize = new Vector2(size, size),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Texture = Texture(kind),
            Modulate = tint ?? UiTheme.Text,
        };
        return icon;
    }

    public static Texture2D? Texture(Kind kind)
    {
        if (Cache.TryGetValue(kind, out Texture2D? cached))
        {
            return cached;
        }

        string file = kind switch
        {
            Kind.Health => "health",
            Kind.Stamina => "stamina",
            Kind.Mana => "mana",
            Kind.Currency => "currency",
            Kind.Quest => "quest",
            Kind.Waypoint => "waypoint",
            Kind.Settlement => "settlement",
            Kind.Service => "service",
            Kind.Travel => "travel",
            Kind.Lock => "lock",
            Kind.Inventory => "inventory",
            Kind.Weapon => "weapon",
            Kind.Spell => "spell",
            Kind.Warning => "warning",
            Kind.Sun => "sun",
            Kind.Moon => "moon",
            Kind.Consumable => "consumable",
            Kind.Armor => "armor",
            Kind.Material => "material",
            Kind.Misc => "misc",
            _ => "warning",
        };

        Texture2D? loaded = GD.Load<Texture2D>($"res://assets/ui/icons/{file}.svg");
        Cache[kind] = loaded;
        return loaded;
    }
}
