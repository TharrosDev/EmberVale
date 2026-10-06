using Embervale.Items;
using Embervale.Localization;
using Embervale.Settings;
using Godot;

namespace Embervale.UI;

/// <summary>
/// The small live pictures the description pane of <see cref="SettingsPanel"/> shows under an
/// option's words: a text-size sample, a subtitle line, the colour-vision swatches and a diagram of
/// the HUD. Each is drawn from the settings as they are, and a row's <c>Live</c> callback redraws
/// it while its slider is moving. They are shown only beside the list, not folded under it.
/// </summary>
public partial class SettingsPanel
{
    /// <summary>A preview's ground: a well cut into the sheet, padded like a card.</summary>
    private static PanelContainer PreviewWell(Control content)
    {
        PanelContainer well = UiTheme.Well();
        MarginContainer pad = UiTheme.Padding(UiTheme.SpaceSm);
        pad.AddChild(content);
        well.AddChild(pad);
        return well;
    }

    /// <summary>A line of body text and a caption at the text size the slider is on, which is the
    /// size the whole interface takes when the slider is let go.</summary>
    private Control TextSample(RowInfo info)
    {
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", UiTheme.LineGap);

        Label body = UiTheme.Body(Loc.T("settings.preview.text"));
        body.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        Label caption = UiTheme.Caption(Loc.T("settings.preview.caption"));
        caption.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        box.AddChild(body);
        box.AddChild(caption);

        info.Live = () =>
        {
            if (!IsInstanceValid(body) || !IsInstanceValid(caption))
            {
                return;
            }

            float scale = _settings.Current.TextScale;
            body.AddThemeFontSizeOverride("font_size", UiTheme.ScaledFontSize(UiTheme.BodyFontSize, scale));
            caption.AddThemeFontSizeOverride("font_size", UiTheme.ScaledFontSize(UiTheme.CaptionFontSize, scale));
        };
        return PreviewWell(box);
    }

    /// <summary>The type token a subtitle size is set in.</summary>
    private static int SubtitleToken(int size) => SettingsMath.ClampSubtitleSize(size) switch
    {
        0 => UiTheme.BodyFontSize,
        2 => UiTheme.TitleFontSize,
        _ => UiTheme.HeaderFontSize,
    };

    /// <summary>One subtitle line on its plate, over a mid-grey stand-in for the world so the
    /// plate's opacity can be judged.</summary>
    private Control SubtitleSample(RowInfo info)
    {
        var world = new PanelContainer();
        var ground = new StyleBoxFlat { BgColor = UiTheme.Ash };
        ground.SetCornerRadiusAll(UiTheme.RadiusSm);
        ground.SetContentMarginAll(UiTheme.SpaceMd);
        world.AddThemeStyleboxOverride("panel", ground);

        var plate = new PanelContainer();
        var plateStyle = new StyleBoxFlat();
        plateStyle.SetCornerRadiusAll(UiTheme.RadiusSm);
        plateStyle.SetContentMarginAll(UiTheme.SpaceXs);
        plateStyle.ContentMarginLeft = UiTheme.SpaceSm;
        plateStyle.ContentMarginRight = UiTheme.SpaceSm;
        plate.AddThemeStyleboxOverride("panel", plateStyle);
        world.AddChild(plate);

        var line = new Label
        {
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        line.AddThemeColorOverride("font_color", UiTheme.Text);
        line.AddThemeColorOverride("font_outline_color", UiTheme.Keyline);
        line.AddThemeConstantOverride("outline_size", UiTheme.Space2xs);
        plate.AddChild(line);

        info.Live = () =>
        {
            if (!IsInstanceValid(line))
            {
                return;
            }

            var s = _settings.Current;
            plateStyle.BgColor = UiTheme.ScrimBg with { A = Mathf.Clamp(s.SubtitleBackground, 0f, 1f) };
            UiTheme.ApplyType(line, UiTheme.FontRole.Interface, SubtitleToken(s.SubtitleSize));
            line.Text = !s.SubtitlesEnabled
                ? Loc.T("settings.preview.subtitle_off")
                : s.SubtitleSpeakerNames
                    ? Loc.T("settings.preview.subtitle_named")
                    : Loc.T("settings.preview.subtitle");
        };
        info.Live();
        return world;
    }

    /// <summary>The colours that carry meaning, each with its word, as the colour-vision and
    /// contrast settings now draw them.</summary>
    private Control VisionSwatches()
    {
        HFlowContainer row = UiTheme.FlowRow();
        row.AddChild(UiTheme.Chip(Loc.T("settings.preview.rarity.common"), UiTheme.RarityColor(ItemRarity.Common)));
        row.AddChild(UiTheme.Chip(Loc.T("settings.preview.rarity.uncommon"), UiTheme.RarityColor(ItemRarity.Uncommon)));
        row.AddChild(UiTheme.Chip(Loc.T("settings.preview.rarity.rare"), UiTheme.RarityColor(ItemRarity.Rare)));
        row.AddChild(UiTheme.Chip(Loc.T("settings.preview.rarity.epic"), UiTheme.RarityColor(ItemRarity.Epic)));
        row.AddChild(UiTheme.Chip(Loc.T("settings.preview.rarity.legendary"), UiTheme.RarityColor(ItemRarity.Legendary)));
        row.AddChild(UiTheme.Chip(Loc.T("settings.preview.good"), UiTheme.Good));
        row.AddChild(UiTheme.Chip(Loc.T("settings.preview.bad"), UiTheme.Bad));
        return PreviewWell(row);
    }

    /// <summary>
    /// The screen in small, with a block where each fixed part of the HUD sits: vitals bottom
    /// left, hotbar bottom centre, minimap bottom right, compass top centre, tracker top right.
    /// The blocks grow with the HUD scale, fade with its opacity and step in with the safe zone,
    /// whose edge is drawn as a second outline.
    /// </summary>
    private Control HudDiagram(RowInfo info)
    {
        var canvas = new Control
        {
            CustomMinimumSize = new Vector2(UiTheme.SettingsPaneWidth, UiTheme.SettingsPaneWidth * 9f / 16f),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        canvas.Draw += () =>
        {
            var s = _settings.Current;
            float scale = SettingsMath.ClampHudScale(s.HudScale);
            float safe = SettingsMath.ClampHudSafeZone(s.HudSafeZone);
            Color ink = UiTheme.Text with { A = SettingsMath.ClampHudOpacity(s.HudOpacity) };

            Vector2 size = canvas.Size;
            canvas.DrawRect(new Rect2(Vector2.Zero, size), UiTheme.WellBg);
            canvas.DrawRect(new Rect2(Vector2.Zero, size), UiTheme.Rule, false, 1f);

            Vector2 inset = size * safe;
            if (safe > 0f)
            {
                canvas.DrawRect(new Rect2(inset, size - (inset * 2f)), UiTheme.RuleLit, false, 1f);
            }

            // The HUD's own margin inside the safe zone, as a share of the picture.
            const float Margin = 0.03f;
            inset += size * Margin;
            Vector2 room = size - (inset * 2f);

            void Block(float anchorX, float anchorY, float width, float height)
            {
                var block = new Vector2(width * size.X, height * size.Y) * scale;
                var at = new Vector2(inset.X + (anchorX * (room.X - block.X)), inset.Y + (anchorY * (room.Y - block.Y)));
                canvas.DrawRect(new Rect2(at, block), ink);
            }

            Block(0f, 1f, 0.22f, 0.11f);
            Block(0.5f, 1f, 0.26f, 0.07f);
            Block(1f, 1f, 0.12f, 0.21f);
            Block(0.5f, 0f, 0.30f, 0.035f);
            Block(1f, 0f, 0.20f, 0.14f);
        };
        info.Live = () =>
        {
            if (IsInstanceValid(canvas))
            {
                canvas.QueueRedraw();
            }
        };
        return canvas;
    }
}
