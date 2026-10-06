using Embervale.UI;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// The text-scale curve behind <c>UiTheme.FontSize</c>: small type takes the whole setting, large
/// type takes less of it, and nothing ever renders under the 12 px floor. Also pins the readable-font
/// role switch. Pure arithmetic; no engine.
/// </summary>
public class UiTypeScaleTests
{
    private static readonly int[] Tokens =
    {
        UiTheme.CaptionFontSize, UiTheme.BodyFontSize, UiTheme.HeaderFontSize,
        UiTheme.TitleFontSize, UiTheme.DisplayFontSize, UiTheme.ShoutFontSize,
    };

    private static System.Collections.Generic.IEnumerable<float> Scales()
    {
        for (int step = 0; step <= 13; step++)
        {
            yield return 0.85f + (step * 0.05f);
        }
    }

    [Fact]
    public void DefaultScaleLeavesEveryTokenUnchanged()
    {
        foreach (int token in Tokens)
        {
            Assert.Equal(token, UiTheme.ScaledFontSize(token, 1f));
        }
    }

    [Fact]
    public void NothingFallsBelowTheLegibilityFloorAtAnyScale()
    {
        foreach (float scale in Scales())
        {
            for (int token = 1; token <= 64; token++)
            {
                Assert.True(
                    UiTheme.ScaledFontSize(token, scale) >= UiTheme.CaptionFontSize,
                    $"{token} px at {scale:0.00} fell below the floor");
            }
        }

        // Out-of-range settings clamp, they do not escape the floor.
        Assert.Equal(UiTheme.CaptionFontSize, UiTheme.ScaledFontSize(UiTheme.CaptionFontSize, 0.1f));
        Assert.Equal(UiTheme.ScaledFontSize(UiTheme.BodyFontSize, 1.5f), UiTheme.ScaledFontSize(UiTheme.BodyFontSize, 9f));
    }

    /// <summary>A bigger token is never drawn smaller than a smaller one: the curve bends the
    /// scale, it must not fold the hierarchy.</summary>
    [Fact]
    public void SizeIsMonotonicInTheToken()
    {
        foreach (float scale in Scales())
        {
            int previous = 0;
            for (int token = 1; token <= 64; token++)
            {
                int size = UiTheme.ScaledFontSize(token, scale);
                Assert.True(size >= previous, $"{token} px at {scale:0.00} = {size}, below {token - 1} px = {previous}");
                previous = size;
            }
        }
    }

    /// <summary>Turning the setting up never makes any text smaller.</summary>
    [Fact]
    public void SizeIsMonotonicInTheSetting()
    {
        for (int token = 1; token <= 64; token++)
        {
            int previous = 0;
            foreach (float scale in Scales())
            {
                int size = UiTheme.ScaledFontSize(token, scale);
                Assert.True(size >= previous, $"{token} px shrank from {previous} to {size} at {scale:0.00}");
                previous = size;
            }
        }
    }

    /// <summary>Caption and body may meet at the floor when the setting is turned down; from body
    /// up the steps of the scale must stay distinct.</summary>
    [Fact]
    public void TheTypeScaleStaysStrictlyOrderedAboveBody()
    {
        foreach (float scale in Scales())
        {
            for (int i = 2; i < Tokens.Length; i++)
            {
                Assert.True(
                    UiTheme.ScaledFontSize(Tokens[i], scale) > UiTheme.ScaledFontSize(Tokens[i - 1], scale),
                    $"{Tokens[i]} px and {Tokens[i - 1]} px collapsed at {scale:0.00}");
            }
        }
    }

    /// <summary>The share each anchor takes: all of it to body, 85% header, 70% title, half from display up.</summary>
    [Theory]
    [InlineData(12, 18)] // 12 * 1.5
    [InlineData(18, 26)] // 18 * 1.425 = 25.65
    [InlineData(24, 32)] // 24 * 1.35 = 32.4
    [InlineData(32, 40)] // 32 * 1.25
    [InlineData(40, 50)] // 40 * 1.25
    public void LargeTypeTakesLessOfTheScale(int token, int atMaximum)
    {
        Assert.Equal(atMaximum, UiTheme.ScaledFontSize(token, 1.5f));
    }

    [Fact]
    public void ReadableFontSendsEveryRoleToTheInterfaceFace()
    {
        foreach (UiTheme.FontRole role in System.Enum.GetValues<UiTheme.FontRole>())
        {
            Assert.Equal(role, UiTheme.ResolveRole(role, readable: false));
            Assert.Equal(UiTheme.FontRole.Interface, UiTheme.ResolveRole(role, readable: true));
        }
    }

    /// <summary>No settings service in this harness, so the live seams report today's behaviour.</summary>
    [Fact]
    public void WithoutSettingsTheThemeUsesTheAuthoredFacesAndSizes()
    {
        Assert.False(UiTheme.ReadableFont);
        Assert.Equal(UiTheme.TitleFontSize, UiTheme.FontSize(UiTheme.TitleFontSize));
    }
}
