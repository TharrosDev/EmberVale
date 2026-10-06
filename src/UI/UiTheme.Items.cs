using Embervale.Items;
using Godot;

namespace Embervale.UI;

/// <summary>
/// The item screens' share of the theme (2026-10 UI upgrade, lane "Items and character"): the cut
/// plate a detail card is, its rarity band and footer, the dark badge a slot's count sits on, and
/// the drawn up/down arrow every stat delta carries. Everything here is built from the tokens in
/// <c>UiTheme.cs</c>; no new colours.
/// </summary>
public static partial class UiTheme
{
    /// <summary>
    /// The lit top edge of an item's detail plate, in px. It is the card's second rarity channel:
    /// a hairline for the common tiers, a heavier rule from Epic up, on the same step
    /// <see cref="RarityBorderWidth"/> thickens the slot frame at.
    /// </summary>
    public static int RarityEdgeWidth(ItemRarity rarity) => RarityBorderWidth(rarity) + 1;

    /// <summary>A cut plate: the card ground with a single lit edge along the top and nothing round
    /// the other three sides. It has no content margins, so a band or footer inside it runs edge to
    /// edge; pad the body with <see cref="PlateBody"/>.</summary>
    public static PanelContainer Plate(Color litEdge, int edgeWidth = 2)
    {
        var plate = new PanelContainer();
        plate.AddThemeStyleboxOverride("panel", PlateStyle(litEdge, edgeWidth));
        return plate;
    }

    /// <summary>The stylebox of a <see cref="Plate"/>, for a control that is one by inheritance.</summary>
    public static StyleBoxFlat PlateStyle(Color litEdge, int edgeWidth = 2)
    {
        var box = new StyleBoxFlat { BgColor = CardBg, BorderColor = litEdge };
        box.SetBorderWidthAll(0);
        box.BorderWidthTop = HighContrast ? edgeWidth + 1 : edgeWidth;
        box.SetCornerRadiusAll(RadiusSm);
        box.SetContentMarginAll(0);
        return box;
    }

    /// <summary>The padded body of a <see cref="Plate"/>.</summary>
    public static MarginContainer PlateBody()
    {
        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left", SpaceMd);
        margin.AddThemeConstantOverride("margin_right", SpaceMd);
        margin.AddThemeConstantOverride("margin_top", SpaceSm);
        margin.AddThemeConstantOverride("margin_bottom", SpaceSm);
        return margin;
    }

    /// <summary>The ground of a <see cref="PlateBand"/>: the card ground warmed a little toward
    /// <paramref name="tint"/>. Pure, and only a little, because the name printed on it is set in
    /// that same tint and has to stay readable (<c>ItemCardRulesTests</c> holds it to AA).</summary>
    public static Color PlateBandColor(Color tint) => CardBg.Lerp(tint with { A = 1f }, 0.08f);

    /// <summary>The header band of a plate: the card ground warmed toward <paramref name="tint"/>
    /// with a hairline under it. Holds the name and the type line.</summary>
    public static PanelContainer PlateBand(Color tint)
    {
        var box = new StyleBoxFlat { BgColor = PlateBandColor(tint), BorderColor = Rule };
        box.SetBorderWidthAll(0);
        box.BorderWidthBottom = 1;
        box.ContentMarginLeft = SpaceMd;
        box.ContentMarginRight = SpaceMd;
        box.ContentMarginTop = SpaceSm;
        box.ContentMarginBottom = SpaceSm;

        var band = new PanelContainer();
        band.AddThemeStyleboxOverride("panel", box);
        return band;
    }

    /// <summary>The footer of a plate: cut back into the well, a hairline above it. Holds what the
    /// thing weighs and costs and what the buttons do to it.</summary>
    public static PanelContainer PlateFooter()
    {
        var box = new StyleBoxFlat { BgColor = WellBg, BorderColor = Rule };
        box.SetBorderWidthAll(0);
        box.BorderWidthTop = 1;
        box.ContentMarginLeft = SpaceMd;
        box.ContentMarginRight = SpaceMd;
        box.ContentMarginTop = SpaceXs;
        box.ContentMarginBottom = SpaceXs;

        var footer = new PanelContainer();
        footer.AddThemeStyleboxOverride("panel", box);
        return footer;
    }

    /// <summary>The dark badge a count or a mark sits on inside an item slot, so it reads over
    /// painted art as well as over an empty well.</summary>
    public static StyleBoxFlat BadgeStyle()
    {
        var box = new StyleBoxFlat { BgColor = Keyline };
        box.SetCornerRadiusAll(RadiusSm);
        box.ContentMarginLeft = Space2xs;
        box.ContentMarginRight = Space2xs;
        box.ContentMarginTop = 0;
        box.ContentMarginBottom = 0;
        return box;
    }

    /// <summary>
    /// The arrow a stat change carries: up and <see cref="Good"/> for a gain, down and
    /// <see cref="Bad"/> for a loss. Drawn rather than typed, so it does not depend on a font having
    /// the triangle and cannot come out as a missing-glyph box. The direction is the channel that
    /// survives a colour-vision mode. <paramref name="lowerIsBetter"/> flips the colour only.
    /// </summary>
    public static Control DeltaArrow(float delta, float size = 9f, bool lowerIsBetter = false)
    {
        bool up = delta > 0f;
        return new UiDeltaArrow(up, up != lowerIsBetter ? Good : Bad, size);
    }
}
