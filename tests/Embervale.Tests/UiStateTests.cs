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
