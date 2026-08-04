using Embervale.Core;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// Covers the blocking-menu owner set behind <see cref="UiState"/> (Phase 25.5E). The mouse-mode
/// application runs in-engine, but the load-bearing decision — "is ANY menu still open?" when
/// overlays overlap — is pure and pinned here. <see cref="UiState"/> is process-global static state,
/// so each test closes the owners it opens to leave it clean for the next.
/// </summary>
public class UiStateTests
{
    [Fact]
    public void Open_MakesMenuOpen()
    {
        var a = new object();
        UiState.Open(a);
        Assert.True(UiState.MenuOpen);
        UiState.Close(a);
        Assert.False(UiState.MenuOpen);
    }

    [Fact]
    public void ClosingInnerOverlay_KeepsMenuOpen_WhileOuterRemains()
    {
        // The 25.5E bug: a single bool flipped to false here, recapturing the mouse behind the
        // still-open outer menu. The owner set must stay open until BOTH close.
        var inventory = new object();
        var devConsole = new object();
        UiState.Open(inventory);
        UiState.Open(devConsole);

        UiState.Close(devConsole);
        Assert.True(UiState.MenuOpen);  // inventory still up

        UiState.Close(inventory);
        Assert.False(UiState.MenuOpen);
    }

    [Fact]
    public void Close_WithoutOpen_IsNoOp()
    {
        UiState.Close(new object());
        Assert.False(UiState.MenuOpen);
        Assert.Equal(0, UiState.OpenCount);
    }

    // --- World pause (the menu/cinematic split) -----------------------------

    [Fact]
    public void AMenuPausesTheWorldByDefault()
    {
        // Before this, a blocking menu suspended the player's movement, guard, dodge and casts and
        // suspended nothing else — so reading the inventory mid-fight left a frozen, un-blocking
        // player being hit by enemies that never stopped, with DoTs still ticking.
        var inventory = new object();
        UiState.Open(inventory);

        Assert.True(UiState.WorldPaused);

        UiState.Close(inventory);
        Assert.False(UiState.WorldPaused);
    }

    [Fact]
    public void ACinematicLockTakesTheControlsWithoutTheClock()
    {
        // The boss intro and the opening narration hold the player still to watch something that is
        // in the world — pausing there would freeze the very thing being watched.
        var bossIntro = new object();
        UiState.Open(bossIntro, pausesWorld: false);

        Assert.True(UiState.MenuOpen, "the player must still be held still");
        Assert.False(UiState.WorldPaused, "the entrance has to keep playing");

        UiState.Close(bossIntro);
    }

    [Fact]
    public void AMenuOverACinematic_PausesUntilThatMenuCloses()
    {
        var bossIntro = new object();
        var inventory = new object();
        UiState.Open(bossIntro, pausesWorld: false);
        UiState.Open(inventory);
        Assert.True(UiState.WorldPaused);

        UiState.Close(inventory);
        Assert.False(UiState.WorldPaused);
        Assert.True(UiState.MenuOpen);   // the cinematic lock outlives it

        UiState.Close(bossIntro);
    }

    [Fact]
    public void NestedMenus_ResumeOnlyWhenTheLastCloses()
    {
        var inventory = new object();
        var crafting = new object();
        UiState.Open(inventory);
        UiState.Open(crafting);

        UiState.Close(crafting);
        Assert.True(UiState.WorldPaused);

        UiState.Close(inventory);
        Assert.False(UiState.WorldPaused);
    }

    [Fact]
    public void OpenAndClose_RaiseChanged()
    {
        // GameManager refreshes the scene tree's paused flag off this event; a missed raise either
        // strands the pause or never applies it.
        var inventory = new object();
        int raised = 0;
        void Handler() => raised++;

        UiState.Changed += Handler;
        try
        {
            UiState.Open(inventory);
            UiState.Close(inventory);
        }
        finally
        {
            UiState.Changed -= Handler;
        }

        Assert.Equal(2, raised);
    }

    [Fact]
    public void Open_IsIdempotent_PerOwner()
    {
        var a = new object();
        UiState.Open(a);
        UiState.Open(a);            // same owner twice
        Assert.Equal(1, UiState.OpenCount);
        UiState.Close(a);          // one close clears it
        Assert.False(UiState.MenuOpen);
    }
}
