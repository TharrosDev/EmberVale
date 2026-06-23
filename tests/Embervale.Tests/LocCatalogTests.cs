using Embervale.Localization;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// Covers the pure localization-CSV parser (Phase 24G). The catalogue is the source of every
/// player-facing string, so quoting (embedded commas/quotes), comment/blank-line skipping, multiple
/// locale columns, and empty-cell fallback are pinned here; the engine-side <c>TranslationServer</c>
/// registration runs in-engine.
/// </summary>
public class LocCatalogTests
{
    [Fact]
    public void Parse_ReadsKeysAndValuesForALocale()
    {
        var result = LocCatalog.Parse("keys,en\nmenu.new_game,New Game\nmenu.quit,Quit\n");

        Assert.True(result.ContainsKey("en"));
        Assert.Equal("New Game", result["en"]["menu.new_game"]);
        Assert.Equal("Quit", result["en"]["menu.quit"]);
    }

    [Fact]
    public void Parse_HonoursQuotedFieldsWithCommas()
    {
        var result = LocCatalog.Parse("keys,en\nmenu.subtitle,\"Choose, or become its Ash King.\"\n");

        Assert.Equal("Choose, or become its Ash King.", result["en"]["menu.subtitle"]);
    }

    [Fact]
    public void Parse_UnescapesDoubledQuotes()
    {
        var result = LocCatalog.Parse("keys,en\ngreeting,\"She said \"\"hi\"\"\"\n");

        Assert.Equal("She said \"hi\"", result["en"]["greeting"]);
    }

    [Fact]
    public void Parse_SkipsCommentAndBlankLines()
    {
        var result = LocCatalog.Parse("keys,en\n# a comment\n\nmenu.quit,Quit\n");

        Assert.Single(result["en"]);
        Assert.Equal("Quit", result["en"]["menu.quit"]);
    }

    [Fact]
    public void Parse_HandlesMultipleLocaleColumns()
    {
        var result = LocCatalog.Parse("keys,en,fr\nmenu.quit,Quit,Quitter\n");

        Assert.Equal("Quit", result["en"]["menu.quit"]);
        Assert.Equal("Quitter", result["fr"]["menu.quit"]);
    }

    [Fact]
    public void Parse_EmptyCellContributesNoEntryForThatLocale()
    {
        // fr cell is empty → the key should be absent for fr (falls back to the key at runtime).
        var result = LocCatalog.Parse("keys,en,fr\nmenu.quit,Quit,\n");

        Assert.Equal("Quit", result["en"]["menu.quit"]);
        Assert.False(result["fr"].ContainsKey("menu.quit"));
    }

    [Fact]
    public void Parse_HandlesCrlfAndMissingTrailingNewline()
    {
        var result = LocCatalog.Parse("keys,en\r\nmenu.quit,Quit");

        Assert.Equal("Quit", result["en"]["menu.quit"]);
    }

    [Fact]
    public void Parse_EmptyInputYieldsNoLocales()
    {
        Assert.Empty(LocCatalog.Parse(string.Empty));
    }
}
