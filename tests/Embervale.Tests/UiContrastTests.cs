using Embervale.Combat;
using Embervale.Items;
using Embervale.UI;
using Godot;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// The 30.5K legibility audit, kept executable: every token pair the UI renders text with
/// must hold WCAG AA (≥4.5:1), and the meaningful non-text pairs (bar fills on the trough)
/// ≥3:1. A palette retune that regresses readability fails here instead of shipping.
/// </summary>
public class UiContrastTests
{
    // The button-face surfaces from UiTheme.ApplyInteractiveStyle/ButtonStyle.
    private static readonly Color ButtonNormal = new(0.16f, 0.15f, 0.13f, 0.95f);
    private static readonly Color ButtonHover = new(0.23f, 0.21f, 0.18f, 0.98f);

    public static readonly TheoryData<string, Color, Color> TextPairs = new()
    {
        { "Text on PanelBg", UiTheme.Text, UiTheme.PanelBg },
        { "Dim on PanelBg", UiTheme.Dim, UiTheme.PanelBg },
        { "Accent on PanelBg", UiTheme.Accent, UiTheme.PanelBg },
        { "AccentHot on PanelBg", UiTheme.AccentHot, UiTheme.PanelBg },
        { "Good on PanelBg", UiTheme.Good, UiTheme.PanelBg },
        { "Bad on PanelBg", UiTheme.Bad, UiTheme.PanelBg },
        { "CorruptionText on PanelBg", UiTheme.CorruptionText, UiTheme.PanelBg },
        { "Text on Trough (keycaps)", UiTheme.Text, UiTheme.Trough },
        { "Dim on Trough", UiTheme.Dim, UiTheme.Trough },
        { "Text on buttons", UiTheme.Text, ButtonNormal },
        { "Dim on buttons (inactive tabs)", UiTheme.Dim, ButtonNormal },
        { "Accent on hovered buttons", UiTheme.Accent, ButtonHover },

        // --- Phase 37.5A: the two new depths -----------------------------------
        // CardBg is the lightest surface in the UI and therefore the hardest ground for every
        // text token — an item row, a spell card and a save slot all sit on it. WellBg is the
        // darkest, and is the ground a Chip's coloured label sits on.
        { "Text on CardBg", UiTheme.Text, UiTheme.CardBg },
        { "Dim on CardBg", UiTheme.Dim, UiTheme.CardBg },
        { "Accent on CardBg", UiTheme.Accent, UiTheme.CardBg },
        { "AccentHot on CardBg", UiTheme.AccentHot, UiTheme.CardBg },
        { "Good on CardBg", UiTheme.Good, UiTheme.CardBg },
        { "Bad on CardBg", UiTheme.Bad, UiTheme.CardBg },
        { "CorruptionText on CardBg", UiTheme.CorruptionText, UiTheme.CardBg },
        { "Text on WellBg", UiTheme.Text, UiTheme.WellBg },
        { "Dim on WellBg", UiTheme.Dim, UiTheme.WellBg },

        // --- The rarity ramp ----------------------------------------------------
        // Item names render in these on both a panel and a card, so both grounds are pinned.
        { "Common on CardBg", ItemRarities.Color(ItemRarity.Common), UiTheme.CardBg },
        { "Uncommon on CardBg", ItemRarities.Color(ItemRarity.Uncommon), UiTheme.CardBg },
        { "Rare on CardBg", ItemRarities.Color(ItemRarity.Rare), UiTheme.CardBg },
        { "Epic on CardBg", ItemRarities.Color(ItemRarity.Epic), UiTheme.CardBg },
        { "Legendary on CardBg", ItemRarities.Color(ItemRarity.Legendary), UiTheme.CardBg },
        { "Common on PanelBg", ItemRarities.Color(ItemRarity.Common), UiTheme.PanelBg },
        { "Legendary on PanelBg", ItemRarities.Color(ItemRarity.Legendary), UiTheme.PanelBg },

        // --- The magic school ramp ----------------------------------------------
        // Every school renders as a spell-card title and as a Chip label, so card and well both.
        { "Physical on CardBg", UiTheme.SchoolColor(DamageType.Physical), UiTheme.CardBg },
        { "Fire on CardBg", UiTheme.SchoolColor(DamageType.Fire), UiTheme.CardBg },
        { "Frost on CardBg", UiTheme.SchoolColor(DamageType.Frost), UiTheme.CardBg },
        { "Lightning on CardBg", UiTheme.SchoolColor(DamageType.Lightning), UiTheme.CardBg },
        { "Arcane on CardBg", UiTheme.SchoolColor(DamageType.Arcane), UiTheme.CardBg },
        { "Nature on CardBg", UiTheme.SchoolColor(DamageType.Nature), UiTheme.CardBg },
        { "Necrotic on CardBg", UiTheme.SchoolColor(DamageType.Necrotic), UiTheme.CardBg },
        { "Necrotic on WellBg (chip)", UiTheme.SchoolColor(DamageType.Necrotic), UiTheme.WellBg },
        { "Fire on WellBg (chip)", UiTheme.SchoolColor(DamageType.Fire), UiTheme.WellBg },

        // --- Quest state and disposition -----------------------------------------
        { "QuestSide on PanelBg", UiTheme.QuestSide, UiTheme.PanelBg },
        { "QuestSide on CardBg", UiTheme.QuestSide, UiTheme.CardBg },
        { "Neutral on PanelBg", UiTheme.Neutral, UiTheme.PanelBg },
        { "Neutral on CardBg", UiTheme.Neutral, UiTheme.CardBg },

        // --- The spellbook's cold ground -------------------------------------------
        // The one screen that does not use PanelBg, so its text tokens need their own pins.
        { "Text on ArcaneGround", UiTheme.Text, UiTheme.ArcaneGround },
        { "Dim on ArcaneGround", UiTheme.Dim, UiTheme.ArcaneGround },
        { "ArcaneSilver on ArcaneGround", UiTheme.ArcaneSilver, UiTheme.ArcaneGround },
        { "GlyphLight on ArcaneGround", UiTheme.GlyphLight, UiTheme.ArcaneGround },
        { "Accent on ArcaneGround (mastery)", UiTheme.Accent, UiTheme.ArcaneGround },

        // Deliberately absent: UiTheme.Disabled. WCAG exempts disabled controls, and a disabled
        // row that reads as strongly as a live one gets clicked. It is held perceivable (~2.4:1)
        // by the DisabledStaysPerceivable test below instead, which is the floor that actually
        // matters for it.
    };

    public static readonly TheoryData<string, Color, Color> FillPairs = new()
    {
        { "Health fill on Trough", UiTheme.Health, UiTheme.Trough },
        { "Stamina fill on Trough", UiTheme.Stamina, UiTheme.Trough },
        { "Mana fill on Trough", UiTheme.Mana, UiTheme.Trough },
        { "Accent fill on Trough (XP)", UiTheme.Accent, UiTheme.Trough },

        // Deliberately absent: the deep Corruption fill (~2.5:1) — the gauge is a redundant
        // graphic (its tier/value always renders as CorruptionText beside it), and the art
        // bible's violet is not to be brightened (UI_STYLE §2). Corruption *text* is audited.
    };

    [Theory]
    [MemberData(nameof(TextPairs))]
    public void TextPairs_HoldWcagAa(string pair, Color fg, Color bg)
    {
        double ratio = UiContrast.Ratio(fg, bg);
        Assert.True(ratio >= 4.5, $"{pair} = {ratio:0.00}:1, below the 4.5:1 AA floor");
    }

    [Theory]
    [MemberData(nameof(FillPairs))]
    public void FillPairs_HoldUiComponentFloor(string pair, Color fg, Color bg)
    {
        double ratio = UiContrast.Ratio(fg, bg);
        Assert.True(ratio >= 3.0, $"{pair} = {ratio:0.00}:1, below the 3:1 non-text floor");
    }

    [Fact]
    public void Ratio_IsOrderIndependentAndBounded()
    {
        Assert.Equal(UiContrast.Ratio(Colors.White, Colors.Black), UiContrast.Ratio(Colors.Black, Colors.White), 5);
        Assert.Equal(21.0, UiContrast.Ratio(Colors.White, Colors.Black), 1);
        Assert.Equal(1.0, UiContrast.Ratio(Colors.White, Colors.White), 5);
    }

    /// <summary>
    /// The disabled token is exempt from AA on purpose, but "exempt" must not drift into
    /// "invisible" — a greyed-out requirement the player cannot read is a requirement they cannot
    /// act on. This pins the band it has to stay inside: clearly dimmer than <c>Dim</c>, clearly
    /// still there.
    /// </summary>
    [Fact]
    public void DisabledStaysPerceivableWithoutReachingAa()
    {
        double disabled = UiContrast.Ratio(UiTheme.Disabled, UiTheme.PanelBg);
        Assert.True(disabled >= 2.0, $"Disabled = {disabled:0.00}:1 on PanelBg — too faint to read at all");
        Assert.True(disabled < 4.5, $"Disabled = {disabled:0.00}:1 on PanelBg — reads as strongly as a live control");
        Assert.True(
            disabled < UiContrast.Ratio(UiTheme.Dim, UiTheme.PanelBg),
            "Disabled must sit below Dim, or secondary text and unavailable text look the same");
    }
}

/// <summary>
/// Phase 37.5A: pins the two guarantees the rarity ramp makes beyond "the colours look nice",
/// both of which exist so rarity survives a colourblind player, a greyscale screenshot and the
/// 37.5G colourblind modes that will remap hue out from under it.
///
/// This is a separate class from the contrast audit because it is testing a different property:
/// not "can this be read against its background" but "can these five be told apart from each
/// other". A ramp can pass every AA pin and still be five indistinguishable pastels.
/// </summary>
public class RarityRampTests
{
    private static readonly ItemRarity[] Ascending =
    {
        ItemRarity.Common, ItemRarity.Uncommon, ItemRarity.Rare, ItemRarity.Epic, ItemRarity.Legendary,
    };

    /// <summary>Rarer is brighter, strictly. This is what makes the ramp legible with hue removed.</summary>
    [Fact]
    public void LuminanceClimbsStrictlyWithRarity()
    {
        for (int i = 1; i < Ascending.Length; i++)
        {
            double lower = UiContrast.Luminance(ItemRarities.Color(Ascending[i - 1]));
            double higher = UiContrast.Luminance(ItemRarities.Color(Ascending[i]));
            Assert.True(
                higher > lower,
                $"{Ascending[i]} ({higher:0.000}) must be brighter than {Ascending[i - 1]} ({lower:0.000})");
        }
    }

    /// <summary>Monotonic is not enough — a step too small to see is the same as no step. 1.15:1
    /// is roughly where adjacent tiers stop being separable side by side at UI sizes.</summary>
    [Fact]
    public void AdjacentTiersStaySeparable()
    {
        for (int i = 1; i < Ascending.Length; i++)
        {
            double ratio = UiContrast.Ratio(ItemRarities.Color(Ascending[i - 1]), ItemRarities.Color(Ascending[i]));
            Assert.True(
                ratio >= 1.15,
                $"{Ascending[i - 1]} vs {Ascending[i]} = {ratio:0.000}:1 — too close to tell apart");
        }
    }

    /// <summary>Legendary out-burns the UI's own ember accent, or the rarest drop in the game
    /// reads as no louder than a section header.</summary>
    [Fact]
    public void LegendaryOutburnsTheEmberAccent()
    {
        Assert.True(
            UiContrast.Luminance(ItemRarities.Color(ItemRarity.Legendary)) > UiContrast.Luminance(UiTheme.Accent),
            "Legendary must be brighter than UiTheme.Accent");
    }

    /// <summary>The non-colour channel. If this collapses to one width for every tier, the ramp is
    /// back to relying on hue alone and the colourblind modes have nothing to fall back on.</summary>
    [Fact]
    public void FrameThicknessGivesRarityASecondChannel()
    {
        Assert.Equal(1, UiTheme.RarityBorderWidth(ItemRarity.Common));
        Assert.Equal(1, UiTheme.RarityBorderWidth(ItemRarity.Rare));
        Assert.Equal(2, UiTheme.RarityBorderWidth(ItemRarity.Epic));
        Assert.Equal(2, UiTheme.RarityBorderWidth(ItemRarity.Legendary));
    }
}
