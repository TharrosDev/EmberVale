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
    public const int HudInkSize = 3;

    /// <summary>Width, in px, of the soft dark halo behind the keyline. The keyline alone holds a
    /// letter's edge; over a bright sky or pale ground it is the halo that gives the letter something
    /// darker than the scene to sit on.</summary>
    public const int HudHaloSize = 6;

    // What RefreshHud finds the HUD's pieces by. Plain strings: a static StringName here would be
    // built by the type initialiser, which the pure tests run without an engine.
    private const string HudInkMeta = "hud_ink";
    private const string HudBareMeta = "hud_bare";
    private const string HudPlateMeta = "hud_plate";
    private const string HudShadeMeta = "hud_shade";

    /// <summary>The ground of a HUD plate: the panel ash, thin enough to see the world through.</summary>
    public static Color HudPlateBg => PanelBg with { A = HighContrast ? 1f : 0.82f };

    /// <summary>The ground of a HUD shade: thinner than a plate, for one line of text.</summary>
    public static Color HudShadeBg => PanelBg with { A = HighContrast ? 1f : 0.58f };

    /// <summary>The halo behind HUD text (see <see cref="HudHaloSize"/>).</summary>
    public static Color HudHalo => Keyline with { A = 0.38f };

    /// <summary>The ground of a HUD slot (a hotbar cell).</summary>
    public static Color HudSlotBg => WellBg with { A = HighContrast ? 1f : 0.62f };

    /// <summary>How much of <see cref="HudSlotBg"/> an empty slot keeps: a hint of a well, so five
    /// unassigned cells are not the heaviest thing on the HUD.</summary>
    public const float HudSlotQuiet = 0.3f;

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

        // The halo is the label's shadow, drawn with no offset and an outline of its own.
        text.AddThemeColorOverride("font_shadow_color", HudHalo);
        text.AddThemeConstantOverride("shadow_offset_x", 0);
        text.AddThemeConstantOverride("shadow_offset_y", 1);
        text.AddThemeConstantOverride("shadow_outline_size", HudHaloSize);
        text.SetMeta(HudInkMeta, true);
        return text;
    }

    /// <summary>A HUD shade: a thin ash ground with no lit edge, for a line of text that has to read
    /// over any sky (the clock, a boss's name) without becoming a plate. The stylebox is the padding.</summary>
    public static PanelContainer HudShade()
    {
        var shade = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        shade.AddThemeStyleboxOverride("panel", HudShadeStyle());
        shade.SetMeta(HudShadeMeta, true);
        return shade;
    }

    private static StyleBoxFlat HudShadeStyle()
    {
        var box = new StyleBoxFlat { BgColor = HudShadeBg };
        box.SetBorderWidthAll(0);
        box.SetCornerRadiusAll(RadiusSm);
        box.ContentMarginTop = SpaceXs;
        box.ContentMarginBottom = SpaceXs;
        box.ContentMarginLeft = SpaceSm;
        box.ContentMarginRight = SpaceSm;
        return box;
    }

    /// <summary>A HUD group with no ground at all: its children sit on the world. Under high contrast
    /// it becomes an opaque plate, because that setting exists for exactly this surface.</summary>
    public static PanelContainer HudBare()
    {
        var group = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        group.AddThemeStyleboxOverride("panel", HudBareStyle());
        group.SetMeta(HudBareMeta, true);
        return group;
    }

    /// <summary>A HUD plate: a translucent cut of ash with a single lit edge on the left, in
    /// <paramref name="edge"/> when the plate has something to say with it (a quest's priority, a
    /// warning) and <see cref="RuleLit"/> otherwise. The stylebox is the padding.</summary>
    public static PanelContainer HudPlate(Color? edge = null)
    {
        var plate = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        plate.AddThemeStyleboxOverride("panel", HudPlateStyle(edge));
        plate.SetMeta(HudPlateMeta, true);
        return plate;
    }

    private static StyleBox HudBareStyle() => HighContrast ? HudPlateStyle(null) : new StyleBoxEmpty();

    private static int HudPlateEdge => HighContrast ? 4 : 2;

    /// <summary>
    /// Re-applies the high-contrast half of everything here to a HUD that is already built: bare
    /// groups gain or lose their ground, plates their opacity and edge, text its heavier ink, and
    /// whatever draws a keyline repaints. The HUD is built once a session and the setting can change
    /// in the middle of one, and with no grounds under the vitals any more this is the only thing
    /// that makes the setting take. A plate keeps its stylebox, and so the edge colour its owner set.
    /// </summary>
    public static void RefreshHud(Node root)
    {
        if (root is Control control)
        {
            if (control.HasMeta(HudInkMeta))
            {
                control.AddThemeConstantOverride("outline_size", HighContrast ? HudInkSize + 1 : HudInkSize);
            }

            if (control.HasMeta(HudBareMeta))
            {
                control.AddThemeStyleboxOverride("panel", HudBareStyle());
            }
            else if (control.HasMeta(HudPlateMeta) && control.GetThemeStylebox("panel") is StyleBoxFlat plate)
            {
                plate.BgColor = HudPlateBg;
                plate.BorderWidthLeft = HudPlateEdge;
            }
            else if (control.HasMeta(HudShadeMeta) && control.GetThemeStylebox("panel") is StyleBoxFlat shade)
            {
                shade.BgColor = HudShadeBg;
            }

            control.QueueRedraw();
        }

        foreach (Node child in root.GetChildren())
        {
            RefreshHud(child);
        }
    }

    public static StyleBoxFlat HudPlateStyle(Color? edge)
    {
        var box = new StyleBoxFlat { BgColor = HudPlateBg, BorderColor = edge ?? RuleLit };
        box.SetBorderWidthAll(0);
        box.BorderWidthLeft = HudPlateEdge;
        box.SetCornerRadiusAll(RadiusSm);
        box.ContentMarginTop = CompactPadY;
        box.ContentMarginBottom = CompactPadY;
        box.ContentMarginLeft = CompactPadX;
        box.ContentMarginRight = CompactPadX;
        return box;
    }

    /// <summary>A HUD slot's face: a keylined well. <paramref name="edge"/> replaces the keyline for
    /// a state that has to be seen (hover, focus). A <paramref name="groundAlpha"/> below one is an
    /// empty slot: its ground thins and its keyline thins with it, except under high contrast.</summary>
    public static StyleBoxFlat HudSlotStyle(Color? edge = null, float groundAlpha = 1f)
    {
        Color ground = HudSlotBg;
        float quiet = HighContrast ? 1f : groundAlpha;
        var box = new StyleBoxFlat
        {
            BgColor = ground with { A = ground.A * quiet },
            BorderColor = edge ?? Keyline with { A = Keyline.A * Mathf.Lerp(0.6f, 1f, quiet) },
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
