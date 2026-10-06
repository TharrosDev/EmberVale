using System.Linq;
using Embervale.Save;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// Where a player's save goes is the rule a lost save is made of: an F5 that lands in the autosave
/// ring, a manual save with no slot, an F9 that loads another character. Each is pinned here.
/// </summary>
public class SaveSlotPolicyTests
{
    private static SaveSlotInfo Save(string slot, double stamp, string name = "Kael", SaveHealth health = SaveHealth.Ok) =>
        new() { Slot = slot, TimestampUnix = stamp, CharacterName = name, Health = health };

    [Fact]
    public void PlayerManualSlots_AreTheThreeFixedSlots()
    {
        Assert.Equal(new[] { "slot1", "slot2", "slot3" }, SaveSlotPolicy.PlayerManualSlots);
    }

    [Fact]
    public void PlayerNeverWritesTheAutosaveRing()
    {
        Assert.All(SaveSlots.Auto, slot => Assert.False(SaveSlotPolicy.IsPlayerWritable(slot)));
        Assert.All(SaveSlotPolicy.PlayerManualSlots, slot => Assert.True(SaveSlotPolicy.IsPlayerWritable(slot)));
        Assert.True(SaveSlotPolicy.IsPlayerWritable(SaveSlots.Quick));
        Assert.False(SaveSlotPolicy.IsPlayerWritable(""));
        Assert.False(SaveSlotPolicy.IsPlayerWritable(null));
    }

    [Fact]
    public void QuickSave_AlwaysTargetsTheQuickSlot()
    {
        Assert.Equal(SaveSlots.Quick, SaveSlotPolicy.QuickSaveTarget);
    }

    [Fact]
    public void ManualTarget_KeepsAManualSession()
    {
        Assert.Equal("slot2", SaveSlotPolicy.ManualSaveTarget("slot2", "Kael", new[] { Save("slot1", 900) }));
    }

    [Fact]
    public void ManualTarget_AfterAnAutosaveLoad_IsThatCharactersNewestManualSlot()
    {
        SaveSlotInfo[] saves =
        {
            Save("slot1", 100),
            Save("slot2", 300, "Mira"),
            Save("slot3", 200),
            Save("auto1", 999),
        };

        Assert.Equal("slot3", SaveSlotPolicy.ManualSaveTarget("auto1", "Kael", saves));
        Assert.Equal("slot2", SaveSlotPolicy.ManualSaveTarget(SaveSlots.Quick, "Mira", saves));
    }

    [Fact]
    public void ManualTarget_IsNullWhenTheCharacterHasNeverSavedByHand()
    {
        SaveSlotInfo[] saves = { Save("slot1", 100, "Mira"), Save("auto2", 500), Save(SaveSlots.Quick, 600) };

        Assert.Null(SaveSlotPolicy.ManualSaveTarget("auto2", "Kael", saves));
        Assert.Null(SaveSlotPolicy.ManualSaveTarget("", "Kael", saves));
    }

    [Fact]
    public void ManualTarget_IgnoresUnloadableAndNonRosterSlots()
    {
        SaveSlotInfo[] saves = { Save("slot1", 100, health: SaveHealth.Corrupt), Save("slot5", 400) };

        Assert.Null(SaveSlotPolicy.ManualSaveTarget("auto1", "Kael", saves));
    }

    [Fact]
    public void QuickLoad_TakesTheNewerOfQuickAndActive()
    {
        Assert.Equal(SaveSlots.Quick,
            SaveSlotPolicy.QuickLoadTarget("slot1", "Kael", new[] { Save("slot1", 100), Save(SaveSlots.Quick, 200) }));
        Assert.Equal("slot1",
            SaveSlotPolicy.QuickLoadTarget("slot1", "Kael", new[] { Save("slot1", 300), Save(SaveSlots.Quick, 200) }));
        Assert.Equal("slot1",
            SaveSlotPolicy.QuickLoadTarget("slot1", "Kael", new[] { Save(SaveSlots.Quick, 200), Save("slot1", 200) }));
    }

    [Fact]
    public void QuickLoad_IgnoresAnotherCharactersQuickSaveAndUnrelatedSlots()
    {
        SaveSlotInfo[] saves = { Save(SaveSlots.Quick, 900, "Mira"), Save("slot2", 800), Save("slot1", 100) };

        Assert.Equal("slot1", SaveSlotPolicy.QuickLoadTarget("slot1", "Kael", saves));
    }

    [Fact]
    public void QuickLoad_IsNullWithNothingLoadable()
    {
        Assert.Null(SaveSlotPolicy.QuickLoadTarget("slot1", "Kael", new SaveSlotInfo[0]));
        Assert.Null(SaveSlotPolicy.QuickLoadTarget("slot1", "Kael",
            new[] { Save("slot1", 100, health: SaveHealth.Corrupt), Save(SaveSlots.Quick, 50, health: SaveHealth.Newer) }));
    }

    [Fact]
    public void UnsavedConfirm_StartsAtTheThreshold()
    {
        Assert.False(SaveSlotPolicy.NeedsUnsavedConfirm(0d));
        Assert.False(SaveSlotPolicy.NeedsUnsavedConfirm(SaveSlotPolicy.UnsavedWarningSeconds - 0.1d));
        Assert.True(SaveSlotPolicy.NeedsUnsavedConfirm(SaveSlotPolicy.UnsavedWarningSeconds));
    }

    [Fact]
    public void NewestFirst_DropsEmptySlotsAndSortsStably()
    {
        SaveSlotInfo[] saves =
        {
            Save("slot1", 100),
            Save("slot2", 0, health: SaveHealth.Missing),
            Save("auto1", 300),
            Save("slot3", 0, health: SaveHealth.Corrupt),
            Save(SaveSlots.Quick, 300),
        };

        Assert.Equal(new[] { "auto1", SaveSlots.Quick, "slot1", "slot3" },
            SaveSlotPolicy.NewestFirst(saves).Select(info => info.Slot));
    }

    [Theory]
    [InlineData(-5d, 0)]
    [InlineData(59.9d, 0)]
    [InlineData(60d, 1)]
    [InlineData(3725d, 62)]
    [InlineData(double.NaN, 0)]
    public void WholeMinutes_RoundsDownAndNeverGoesNegative(double seconds, int expected)
    {
        Assert.Equal(expected, SaveSlotPolicy.WholeMinutes(seconds));
    }
}
