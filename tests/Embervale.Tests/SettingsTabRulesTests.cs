using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Embervale.Settings;
using Godot;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// The promises the settings screen's resets make, and the one number the difficulty setting is.
/// The field table is checked against <c>Settings</c> itself by reflection (the type is only
/// inspected, never constructed), so a field added to the class and forgotten on the screen fails
/// here rather than surviving every reset unnoticed.
/// </summary>
public class SettingsTabRulesTests
{
    private static readonly SettingsTab[] Tabs = Enum.GetValues<SettingsTab>();

    private static IEnumerable<string> ExportedFields() =>
        typeof(Settings.Settings).GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(p => p.GetCustomAttribute<ExportAttribute>() != null)
            .Select(p => p.Name);

    [Fact]
    public void TabOrdinals_AreTheStripOrder()
    {
        Assert.Equal(0, (int)SettingsTab.Graphics);
        Assert.Equal(1, (int)SettingsTab.Audio);
        Assert.Equal(2, (int)SettingsTab.Controls);
        Assert.Equal(3, (int)SettingsTab.Gameplay);
        Assert.Equal(4, (int)SettingsTab.Interface);
        Assert.Equal(5, (int)SettingsTab.Accessibility);
        Assert.Equal(SettingsTabRules.TabCount, Tabs.Length);
    }

    [Fact]
    public void EveryTableField_IsARealSetting()
    {
        HashSet<string> real = ExportedFields().ToHashSet();
        foreach (SettingsTab tab in Tabs)
        {
            Assert.All(SettingsTabRules.Fields(tab), field => Assert.Contains(field, real));
        }

        Assert.All(SettingsTabRules.BindingFields, field => Assert.Contains(field, real));
    }

    [Fact]
    public void EverySetting_BelongsToExactlyOneTab()
    {
        var owners = new Dictionary<string, int>();
        foreach (SettingsTab tab in Tabs)
        {
            foreach (string field in SettingsTabRules.Fields(tab))
            {
                owners[field] = owners.GetValueOrDefault(field) + 1;
            }
        }

        foreach (string field in ExportedFields())
        {
            Assert.True(owners.GetValueOrDefault(field) == 1,
                $"Settings.{field} is on {owners.GetValueOrDefault(field)} tab(s); a reset would miss it or apply it twice.");
        }
    }

    [Fact]
    public void EveryTab_HasFields()
    {
        Assert.All(Tabs, tab => Assert.NotEmpty(SettingsTabRules.Fields(tab)));
        Assert.Empty(SettingsTabRules.Fields((SettingsTab)99));
    }

    [Fact]
    public void OnlyTheControlsTab_HoldsTheBindings()
    {
        foreach (SettingsTab tab in Tabs)
        {
            bool holds = SettingsTabRules.Fields(tab).Intersect(SettingsTabRules.BindingFields).Any();
            Assert.Equal(tab == SettingsTab.Controls, holds);
        }

        Assert.Equal(2, SettingsTabRules.Fields(SettingsTab.Controls).Intersect(SettingsTabRules.BindingFields).Count());
    }

    [Fact]
    public void ATabReset_NeverReachesAnAccessibilitySetting()
    {
        HashSet<string> accessibility = SettingsTabRules.Fields(SettingsTab.Accessibility).ToHashSet();
        foreach (SettingsTab tab in Tabs.Where(t => t != SettingsTab.Accessibility))
        {
            Assert.DoesNotContain(SettingsTabRules.Fields(tab), field => accessibility.Contains(field));
        }

        // The ones a player may need in order to read the screen at all.
        foreach (string field in new[]
                 {
                     nameof(Settings.Settings.TextScale), nameof(Settings.Settings.UiScale),
                     nameof(Settings.Settings.HighContrast), nameof(Settings.Settings.ColorVision),
                     nameof(Settings.Settings.ReducedMotion), nameof(Settings.Settings.ReadableFont),
                     nameof(Settings.Settings.HoldsToPresses), nameof(Settings.Settings.SubtitlesEnabled),
                 })
        {
            Assert.Contains(field, accessibility);
        }
    }

    [Fact]
    public void ResetAll_KeepsBindingsAndAccessibility_AndTakesEverythingElse()
    {
        IReadOnlyList<string> reset = SettingsTabRules.ResetAllFields();
        Assert.Empty(reset.Intersect(SettingsTabRules.BindingFields));
        Assert.Empty(reset.Intersect(SettingsTabRules.Fields(SettingsTab.Accessibility)));
        Assert.Equal(reset.Count, reset.Distinct().Count());

        int expected = ExportedFields().Count()
                       - SettingsTabRules.BindingFields.Length
                       - SettingsTabRules.Fields(SettingsTab.Accessibility).Count;
        Assert.Equal(expected, reset.Count);
        Assert.Contains(nameof(Settings.Settings.MouseSensitivity), reset);
        Assert.Contains(nameof(Settings.Settings.MasterVolume), reset);
    }

    [Theory]
    [InlineData(SettingsTab.Graphics, 1, SettingsTab.Audio)]
    [InlineData(SettingsTab.Accessibility, 1, SettingsTab.Graphics)]
    [InlineData(SettingsTab.Graphics, -1, SettingsTab.Accessibility)]
    [InlineData(SettingsTab.Gameplay, -1, SettingsTab.Controls)]
    [InlineData(SettingsTab.Controls, 0, SettingsTab.Controls)]
    [InlineData(SettingsTab.Audio, 13, SettingsTab.Controls)]
    public void Step_WrapsAtBothEnds(SettingsTab from, int delta, SettingsTab expected)
    {
        Assert.Equal(expected, SettingsTabRules.Step(from, delta));
    }

    [Theory]
    [InlineData(853f, true)]
    [InlineData(999f, true)]
    [InlineData(1000f, false)]
    [InlineData(1280f, false)]
    [InlineData(3440f, false)]
    public void Narrow_FoldsThePaneBelowAThousandLogicalPixels(float width, bool narrow)
    {
        Assert.Equal(narrow, SettingsTabRules.Narrow(width));
    }

    [Fact]
    public void SameValue_GivesASnappedSliderValueSomeSlack()
    {
        Assert.True(SettingsTabRules.SameValue(0.8f, (float)(16 * 0.05)));
        Assert.True(SettingsTabRules.SameValue(1f, 1.0001f));
        Assert.False(SettingsTabRules.SameValue(1f, 1.05f));
    }

    [Fact]
    public void SameValue_ComparesListsByContent()
    {
        Assert.True(SettingsTabRules.SameValue(Array.Empty<string>(), new string[0]));
        Assert.True(SettingsTabRules.SameValue(new[] { 0, 1, 2 }, new[] { 0, 1, 2 }));
        Assert.False(SettingsTabRules.SameValue(new[] { 0, 1, 2 }, new[] { 0, 1 }));
        Assert.False(SettingsTabRules.SameValue(new[] { "jump=key:F" }, Array.Empty<string>()));
    }

    [Fact]
    public void SameValue_ComparesEverythingElseByEquality()
    {
        Assert.True(SettingsTabRules.SameValue(true, true));
        Assert.False(SettingsTabRules.SameValue(true, false));
        Assert.True(SettingsTabRules.SameValue(-1, -1));
        Assert.False(SettingsTabRules.SameValue(-1, 0));
        Assert.True(SettingsTabRules.SameValue(UI.ColorVisionMode.None, UI.ColorVisionMode.None));
        Assert.True(SettingsTabRules.SameValue(null, null));
        Assert.False(SettingsTabRules.SameValue(null, 1));
    }

    // --- Difficulty -------------------------------------------------------------------------

    [Fact]
    public void Normal_IsExactlyOne()
    {
        // Bit for bit: the gates and every balance number were measured on Normal.
        Assert.Equal(1f, DifficultyRules.IncomingPlayerDamage(DifficultyRules.Normal));
        Assert.Equal(137.25f, 137.25f * DifficultyRules.IncomingPlayerDamage(DifficultyRules.Normal));
    }

    [Fact]
    public void StoryIsGentler_AndHardIsHarsher()
    {
        Assert.Equal(0.6f, DifficultyRules.IncomingPlayerDamage(DifficultyRules.Story));
        Assert.Equal(1.35f, DifficultyRules.IncomingPlayerDamage(DifficultyRules.Hard));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(3)]
    [InlineData(int.MaxValue)]
    public void ANumberThatIsNotADifficulty_IsNormal(int difficulty)
    {
        Assert.Equal(1f, DifficultyRules.IncomingPlayerDamage(difficulty));
    }

    [Fact]
    public void DifficultyNumbers_AreTheOnesTheSettingSaves()
    {
        Assert.Equal(0, DifficultyRules.Story);
        Assert.Equal(1, DifficultyRules.Normal);
        Assert.Equal(2, DifficultyRules.Hard);
    }

    [Theory]
    [InlineData(DifficultyRules.Story)]
    [InlineData(DifficultyRules.Normal)]
    [InlineData(DifficultyRules.Hard)]
    public void OnlyThePlayerIsScaled(int difficulty)
    {
        Assert.Equal(1f, DifficultyRules.Incoming(difficulty, defenderIsPlayer: false));
        Assert.Equal(DifficultyRules.IncomingPlayerDamage(difficulty), DifficultyRules.Incoming(difficulty, defenderIsPlayer: true));
    }
}
