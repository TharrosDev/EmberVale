using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Embervale.Bootstrap;
using Embervale.UI;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// Pins what the loading screen may show: a realm's painting only for a load into that realm, lore
/// only for realms already visited, and nothing at all that names the hidden one.
/// </summary>
public class LoadingCardRulesTests
{
    private static readonly string[] MappedRealms =
    {
        "region.ember_crown", "region.frostfang_reach", "region.ashen_wilds", "region.sunspire", "region.celestial",
    };

    [Fact]
    public void EveryRealm_HasItsOwnPainting()
    {
        var paintings = MappedRealms.Append(LoadingCardRules.HiddenRealmId).Select(LoadingCardRules.Painting).ToList();
        Assert.Equal(paintings.Count, paintings.Distinct().Count());
        Assert.DoesNotContain(UiTheme.GenericPainting, paintings);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("region.nowhere")]
    public void AnUnknownDestination_GetsTheGenericPainting(string? regionId) =>
        Assert.Equal(UiTheme.GenericPainting, LoadingCardRules.Painting(regionId));

    [Fact]
    public void TheHiddenRealmsPainting_IsOnlyForALoadIntoIt()
    {
        string hidden = LoadingCardRules.Painting(LoadingCardRules.HiddenRealmId);
        foreach (string regionId in MappedRealms.Append("region.nowhere").Append(string.Empty))
        {
            Assert.NotEqual(hidden, LoadingCardRules.Painting(regionId));
        }
    }

    [Fact]
    public void WithNothingVisited_OnlyTipsAreOffered()
    {
        Assert.Equal(LoadingCardRules.TipCount, LoadingCardRules.Candidates(null).Count);
        Assert.All(LoadingCardRules.Candidates(Array.Empty<string>()), key => Assert.False(LoadingCardRules.IsLore(key)));
    }

    [Fact]
    public void Lore_IsOfferedOnlyForVisitedRealms()
    {
        List<string> lore = LoadingCardRules.Candidates(new[] { "region.frostfang_reach" })
            .Where(LoadingCardRules.IsLore).ToList();
        Assert.NotEmpty(lore);
        Assert.All(lore, key => Assert.StartsWith("loading.lore.frostfang.", key));
    }

    [Fact]
    public void VisitingTheHiddenRealm_AddsNoCard()
    {
        Assert.Equal(
            LoadingCardRules.Candidates(null),
            LoadingCardRules.Candidates(new[] { LoadingCardRules.HiddenRealmId }));
    }

    [Fact]
    public void Pick_IsAlwaysACandidate_AndNeverTheCardJustShown()
    {
        string[] visited = { "region.ember_crown", "region.sunspire" };
        List<string> candidates = LoadingCardRules.Candidates(visited);
        string previous = candidates[0];
        for (int seed = -5; seed < 200; seed++)
        {
            string picked = LoadingCardRules.Pick(visited, seed, previous);
            Assert.Contains(picked, candidates);
            Assert.NotEqual(previous, picked);
            previous = picked;
        }
    }

    [Fact]
    public void EveryCard_HasTextThatNamesNoHiddenRealmAndHasNoEmDash()
    {
        Dictionary<string, string> catalogue = Catalogue();
        foreach (string key in LoadingCardRules.Candidates(MappedRealms.Append(LoadingCardRules.HiddenRealmId)))
        {
            Assert.True(catalogue.TryGetValue(key, out string? text), $"'{key}' is not in strings.csv");
            Assert.DoesNotContain("Pale", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Concord", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Hollow Queen", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("—", text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void NoLoadingRowInTheCatalogue_IsUnreachable()
    {
        var reachable = LoadingCardRules.Candidates(MappedRealms).ToHashSet();
        IEnumerable<string> authored = Catalogue().Keys.Where(k =>
            k.StartsWith("loading.tip.", StringComparison.Ordinal) || k.StartsWith("loading.lore.", StringComparison.Ordinal));
        Assert.All(authored, key => Assert.Contains(key, reachable));
    }

    [Theory]
    [InlineData(false, false, false, false, 0)]
    [InlineData(true, false, false, false, 1)]
    [InlineData(true, true, false, false, 2)]
    [InlineData(true, true, true, false, 3)]
    [InlineData(true, true, true, true, 4)]
    [InlineData(false, true, true, true, 0)] // a later stage never counts ahead of an earlier one
    public void ProgressSteps_FollowTheGatesStagesInOrder(bool streamer, bool ground, bool realm, bool placed, int step) =>
        Assert.Equal(step, LoadingProgressEvent.StepFor(streamer, ground, realm, placed));

    [Fact]
    public void TheLastStep_IsTheStepCount() =>
        Assert.Equal(LoadingProgressEvent.Steps, LoadingProgressEvent.StepFor(true, true, true, true));

    /// <summary>The first two columns of the string catalogue, read as the file is (quoted values unwrapped).</summary>
    private static Dictionary<string, string> Catalogue()
    {
        var rows = new Dictionary<string, string>();
        foreach (string line in File.ReadLines(Path.Combine(RepositoryRoot(), "data", "locale", "strings.csv")))
        {
            int comma = line.IndexOf(',');
            if (line.Length == 0 || line[0] == '#' || comma < 0)
            {
                continue;
            }

            rows[line[..comma]] = line[(comma + 1)..].Trim('"');
        }

        return rows;
    }

    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "project.godot")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("project.godot not found");
    }
}
