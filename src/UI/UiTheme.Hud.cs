using Godot;

namespace Embervale.UI;

/// <summary>
/// The HUD's share of the theme: the gameplay HUD is light chrome over a live world, so it is built
/// from three things and no boxes. Text and bars sit <b>bare</b> on the world and carry their own
/// <see cref="Keyline"/>; a widget that is mostly reading (the tracker, a prompt) takes a
/// <b>plate</b>, a translucent ash ground with one lit edge; a slot is a small keylined well.
/// High contrast turns every one of them opaque.
/// </summary>
public static partial class UiTheme
{
    /// <summary>Outline, in px, on HUD text drawn over the world. Without it a bone-pale number
    /// crossing a bright sky is not there.</summary>
    public const int HudInkSize = 2;

    /// <summary>The ground of a HUD plate: the panel ash, thin enough to see the world through.</summary>
    public static Color HudPlateBg => PanelBg with { A = HighContrast ? 1f : 0.66f };

    /// <summary>The ground of a HUD slot (a hotbar cell).</summary>
    public static Color HudSlotBg => WellBg with { A = HighContrast ? 1f : 0.74f };

    /// <summary>The lighter edge just inside a keyline, which keeps a bar's outline readable when the
    /// scene behind it is as dark as the keyline is.</summary>
    public static Color HudInnerEdge => IronLit with { A = HighContrast ? 0.95f : 0.55f };

    /// <summary>The length of bar a hit has just removed, before it closes.</summary>
    public static Color HudChunk => Text with { A = 0.80f };

    /// <summary>The dark wedge of a cooldown still to run.</summary>
    public static Color HudWipe => ScrimBg with { A = 0.72f };

    /// <summary>Gives HUD text its keyline. Returns the control so it can wrap a builder call.</summary>
    public static T HudInk<T>(T text)
        where T : Control
    {
        text.AddThemeConstantOverride("outline_size", HighContrast ? HudInkSize + 1 : HudInkSize);
        text.AddThemeColorOverride("font_outline_color", Keyline);
        return text;
    }

    /// <summary>A HUD group with no ground at all: its children sit on the world. Under high contrast
    /// it becomes an opaque plate, because that setting exists for exactly this surface.</summary>
    public static PanelContainer HudBare()
    {
        var group = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        group.AddThemeStyleboxOverride("panel", HighContrast ? HudPlateStyle(null) : new StyleBoxEmpty());
        return group;
    }

    /// <summary>A HUD plate: a translucent cut of ash with a single lit edge on the left, in
    /// <paramref name="edge"/> when the plate has something to say with it (a quest's priority, a
    /// warning) and <see cref="RuleLit"/> otherwise. The stylebox is the padding.</summary>
    public static PanelContainer HudPlate(Color? edge = null)
    {
        var plate = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        plate.AddThemeStyleboxOverride("panel", HudPlateStyle(edge));
        return plate;
    }

    public static StyleBoxFlat HudPlateStyle(Color? edge)
    {
        var box = new StyleBoxFlat { BgColor = HudPlateBg, BorderColor = edge ?? RuleLit };
        box.SetBorderWidthAll(0);
        box.BorderWidthLeft = HighContrast ? 4 : 2;
        box.SetCornerRadiusAll(RadiusSm);
        box.ContentMarginTop = CompactPadY;
        box.ContentMarginBottom = CompactPadY;
        box.ContentMarginLeft = CompactPadX;
        box.ContentMarginRight = CompactPadX;
        return box;
    }

    /// <summary>A HUD slot's face: a keylined well. <paramref name="edge"/> replaces the keyline for
    /// a state that has to be seen (hover, focus).</summary>
    public static StyleBoxFlat HudSlotStyle(Color? edge = null, float groundAlpha = 1f)
    {
        Color ground = HudSlotBg;
        var box = new StyleBoxFlat
        {
            BgColor = ground with { A = ground.A * groundAlpha },
            BorderColor = edge ?? Keyline,
        };
        box.SetBorderWidthAll(edge is null ? 1 : 2);
        box.SetCornerRadiusAll(RadiusSm);
        box.SetContentMarginAll(0);
        return box;
    }

    /// <summary>
    /// Draws the HUD keyline around <paramref name="rect"/> on <paramref name="item"/>: a dark line
    /// just outside it and a lighter one just inside, so the shape holds its edge over a bright scene
    /// and a dark one alike. Call it last, from the item's own draw.
    /// </summary>
    public static void DrawKeyline(CanvasItem item, Rect2 rect)
    {
        // Unfilled rects are stroked on their edge, so each is nudged half a pixel onto whole pixels.
        item.DrawRect(rect.Grow(0.5f), Keyline, false, 1f);
        if (rect.Size.X > 2f && rect.Size.Y > 2f)
        {
            item.DrawRect(rect.Grow(-0.5f), HudInnerEdge, false, 1f);
        }
    }
}
